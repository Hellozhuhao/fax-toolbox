use std::collections::HashMap;
use serde_json::{Value, json};
use crate::modules::ModuleHandler;
use crate::protocol::{Fault, positive, text};

pub const MODULE_ID: &str = "toolbox.smoke";
const MAX_SESSIONS: usize = 256;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum State { Created, Ready, Observing, Complete, Cancelled }

impl State {
    fn as_str(self) -> &'static str {
        match self {
            Self::Created => "CREATED", Self::Ready => "READY", Self::Observing => "OBSERVING",
            Self::Complete => "COMPLETE", Self::Cancelled => "CANCELLED",
        }
    }
    fn terminal(self) -> bool { matches!(self, Self::Complete | Self::Cancelled) }
}

#[derive(Debug)]
struct Session { generation: u64, last_seq: u64, state: State }

fn snapshot(session: &Session) -> Value {
    json!({"state": session.state.as_str(), "lastSeq": session.last_seq,
           "generation": session.generation})
}

#[derive(Default)]
pub struct SmokeModule { sessions: HashMap<String, Session> }

impl ModuleHandler for SmokeModule {
    fn handle(&mut self, method: &str, params: &Value) -> Result<Value, Fault> {
        if text(params, "moduleId")? != MODULE_ID {
            return Err(Fault("MODULE_NOT_FOUND", "module is not registered"));
        }
        let session_id = text(params, "sessionId")?;
        let generation = positive(params, "generation")?;
        match method {
            "session.open" => {
                if self.sessions.contains_key(session_id) {
                    return Err(Fault("ALREADY_OPEN", "session ID already exists"));
                }
                if self.sessions.len() >= MAX_SESSIONS {
                    self.sessions.retain(|_, session| !session.state.terminal());
                    if self.sessions.len() >= MAX_SESSIONS {
                        return Err(Fault("LIMIT_REACHED", "too many active sessions"));
                    }
                }
                let session = Session { generation, last_seq: 0, state: State::Created };
                let result = snapshot(&session);
                self.sessions.insert(session_id.to_owned(), session);
                Ok(result)
            }
            "session.event" | "session.state" | "session.cancel" => {
                let session = self.sessions.get_mut(session_id)
                    .ok_or(Fault("NOT_FOUND", "session does not exist"))?;
                if session.generation != generation {
                    return Err(Fault("STALE_SESSION", "module or generation mismatch"));
                }
                match method {
                    "session.state" => Ok(snapshot(session)),
                    "session.cancel" => {
                        if session.state.terminal() {
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
}
