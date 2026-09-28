use serde_json::{Value, json};
use std::collections::HashMap;
use std::io::{self, BufRead, Write};

const PROTOCOL_VERSION: u64 = 1;
const MAX_LINE_BYTES: usize = 32 * 1024;
const MODULE_ID: &str = "toolbox.smoke";

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum State {
    Created,
    Ready,
    Observing,
    Complete,
    Cancelled,
}

impl State {
    fn as_str(self) -> &'static str {
        match self {
            Self::Created => "CREATED",
            Self::Ready => "READY",
            Self::Observing => "OBSERVING",
            Self::Complete => "COMPLETE",
            Self::Cancelled => "CANCELLED",
        }
    }
}

#[derive(Debug)]
struct Session {
    module_id: String,
    generation: u64,
    last_seq: u64,
    state: State,
}

#[derive(Default)]
struct Backend {
    sessions: HashMap<String, Session>,
}

struct Fault(&'static str, &'static str);

fn text<'a>(value: &'a Value, key: &str) -> Result<&'a str, Fault> {
    let result = value.get(key).and_then(Value::as_str)
        .ok_or(Fault("BAD_REQUEST", "required text field is missing"))?;
    if result.is_empty() || result.len() > 128 {
        return Err(Fault("BAD_REQUEST", "text field length is invalid"));
    }
    Ok(result)
}

fn positive(value: &Value, key: &str) -> Result<u64, Fault> {
    value.get(key).and_then(Value::as_u64).filter(|number| *number > 0)
        .ok_or(Fault("BAD_REQUEST", "required positive integer is missing"))
}

fn snapshot(session: &Session) -> Value {
    json!({"state": session.state.as_str(), "lastSeq": session.last_seq,
           "generation": session.generation})
}

impl Backend {
    fn request(&mut self, request: &Value) -> Result<Value, Fault> {
        if request.get("v").and_then(Value::as_u64) != Some(PROTOCOL_VERSION) {
            return Err(Fault("BAD_VERSION", "unsupported protocol version"));
        }
        let method = text(request, "method")?;
        let params = request.get("params").filter(|item| item.is_object())
            .ok_or(Fault("BAD_REQUEST", "params must be an object"))?;
        if method == "system.ping" {
            return Ok(json!({"name": "toolbox-backend", "protocol": PROTOCOL_VERSION}));
        }
        let module_id = text(params, "moduleId")?;
        if module_id != MODULE_ID {
            return Err(Fault("MODULE_NOT_FOUND", "module is not registered"));
        }
        let session_id = text(params, "sessionId")?;
        let generation = positive(params, "generation")?;
        match method {
            "session.open" => {
                if self.sessions.contains_key(session_id) {
                    return Err(Fault("ALREADY_OPEN", "session ID already exists"));
                }
                let session = Session {
                    module_id: module_id.to_owned(), generation,
                    last_seq: 0, state: State::Created,
                };
                let result = snapshot(&session);
                self.sessions.insert(session_id.to_owned(), session);
                Ok(result)
            }
            "session.event" | "session.state" | "session.cancel" => {
                let session = self.sessions.get_mut(session_id)
                    .ok_or(Fault("NOT_FOUND", "session does not exist"))?;
                if session.module_id != module_id || session.generation != generation {
                    return Err(Fault("STALE_SESSION", "module or generation mismatch"));
                }
                match method {
                    "session.state" => Ok(snapshot(session)),
                    "session.cancel" => {
                        if matches!(session.state, State::Complete | State::Cancelled) {
                            return Err(Fault("INVALID_STATE", "session is already terminal"));
                        }
                        session.state = State::Cancelled;
                        Ok(snapshot(session))
                    }
                    _ => {
                        if text(params, "frameId")? != "main" {
                            return Err(Fault("BAD_FRAME", "only main-frame events are supported"));
                        }
                        let seq = positive(params, "seq")?;
                        if seq <= session.last_seq {
                            return Err(Fault("DUPLICATE", "event sequence did not increase"));
                        }
                        let event = text(params, "event")?;
                        session.state = match (session.state, event) {
                            (State::Created, "HELLO") => State::Ready,
                            (State::Ready, "VIDEO_FOUND") => State::Observing,
                            (State::Observing, "VIDEO_ENDED") => State::Complete,
                            _ => return Err(Fault("INVALID_STATE", "event is not valid in current state")),
                        };
                        session.last_seq = seq;
                        Ok(snapshot(session))
                    }
                }
            }
            _ => Err(Fault("UNKNOWN_METHOD", "method is not supported")),
        }
    }

    fn handle_line(&mut self, line: &str) -> Value {
        if line.len() > MAX_LINE_BYTES {
            return json!({"v": PROTOCOL_VERSION, "id": null, "ok": false,
                          "error": {"code": "TOO_LARGE", "message": "request exceeds 32 KiB"}});
        }
        let request: Value = match serde_json::from_str(line) {
            Ok(value) => value,
            Err(_) => return json!({"v": PROTOCOL_VERSION, "id": null, "ok": false,
                                    "error": {"code": "BAD_JSON", "message": "invalid JSON"}}),
        };
        let id = request.get("id").and_then(Value::as_str).filter(|id| !id.is_empty() && id.len() <= 128);
        let id = match id {
            Some(id) => id.to_owned(),
            None => return json!({"v": PROTOCOL_VERSION, "id": null, "ok": false,
                                  "error": {"code": "BAD_REQUEST", "message": "invalid request ID"}}),
        };
        match self.request(&request) {
            Ok(result) => json!({"v": PROTOCOL_VERSION, "id": id, "ok": true, "result": result}),
            Err(Fault(code, message)) => json!({"v": PROTOCOL_VERSION, "id": id, "ok": false,
                                                "error": {"code": code, "message": message}}),
        }
    }
}

fn main() -> io::Result<()> {
    let stdin = io::stdin();
    let stdout = io::stdout();
    let mut reader = stdin.lock();
    let mut writer = stdout.lock();
    let mut backend = Backend::default();
    loop {
        let mut line = String::new();
        if reader.read_line(&mut line)? == 0 { break; }
        let reply = backend.handle_line(&line);
        serde_json::to_writer(&mut writer, &reply)?;
        writer.write_all(b"\n")?;
        writer.flush()?;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn call(backend: &mut Backend, id: &str, method: &str, params: Value) -> Value {
        backend.handle_line(&json!({"v": 1, "id": id, "method": method, "params": params}).to_string())
    }

    #[test]
    fn session_reaches_complete_and_rejects_duplicate_events() {
        let mut backend = Backend::default();
        let base = json!({"moduleId": MODULE_ID, "sessionId": "s1", "generation": 1});
        assert_eq!(call(&mut backend, "1", "session.open", base.clone())["result"]["state"], "CREATED");
        for (seq, event, state) in [(1, "HELLO", "READY"), (2, "VIDEO_FOUND", "OBSERVING"),
                                     (3, "VIDEO_ENDED", "COMPLETE")] {
            let mut params = base.clone();
            params["frameId"] = json!("main");
            params["seq"] = json!(seq);
            params["event"] = json!(event);
            let reply = call(&mut backend, "2", "session.event", params.clone());
            assert_eq!(reply["result"]["state"], state);
            assert_eq!(call(&mut backend, "3", "session.event", params)["error"]["code"], "DUPLICATE");
        }
    }

    #[test]
    fn cancellation_and_stale_generation_are_rejected() {
        let mut backend = Backend::default();
        let base = json!({"moduleId": MODULE_ID, "sessionId": "s2", "generation": 2});
        call(&mut backend, "1", "session.open", base.clone());
        let mut stale = base.clone();
        stale["generation"] = json!(1);
        assert_eq!(call(&mut backend, "2", "session.state", stale)["error"]["code"], "STALE_SESSION");
        assert_eq!(call(&mut backend, "3", "session.cancel", base.clone())["result"]["state"], "CANCELLED");
        assert_eq!(call(&mut backend, "4", "session.cancel", base)["error"]["code"], "INVALID_STATE");
    }
}
