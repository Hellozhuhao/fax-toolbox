use std::collections::HashMap;
use serde_json::{Value, json};
use crate::modules::ModuleHandler;
use crate::protocol::{Fault, positive, text};

#[derive(Clone)]
struct Lease {
    request_id: String,
    operation: String,
    generation: u64,
}

/// AI owns request coordination only. Credentials and model payloads stay in the
/// Windows process; consumers communicate through this namespace, never by
/// reaching into each other's course or homework state.
#[derive(Default)]
pub struct AiModule {
    active: HashMap<(String, String), Lease>,
}

impl AiModule {
    fn identity(params: &Value) -> Result<(String, String), Fault> {
        let scope = text(params, "scopeId")?;
        if scope.len() != 32 || !scope.bytes().all(|byte| byte.is_ascii_hexdigit()) {
            return Err(Fault("BAD_REQUEST", "invalid AI scope"));
        }
        let consumer = text(params, "consumer")?;
        if consumer != "course" && consumer != "homework" {
            return Err(Fault("BAD_REQUEST", "unknown AI consumer"));
        }
        Ok((scope.to_owned(), consumer.to_owned()))
    }

    fn request_id(params: &Value) -> Result<String, Fault> {
        let id = text(params, "requestId")?;
        if id.len() != 32 || !id.bytes().all(|byte| byte.is_ascii_hexdigit()) {
            return Err(Fault("BAD_REQUEST", "invalid AI request ID"));
        }
        Ok(id.to_owned())
    }
}

impl ModuleHandler for AiModule {
    fn handle(&mut self, method: &str, params: &Value) -> Result<Value, Fault> {
        let key = Self::identity(params)?;
        match method {
            "ai.begin" => {
                let request_id = Self::request_id(params)?;
                let generation = positive(params, "generation")?;
                let operation = text(params, "operation")?;
                let valid = matches!((key.1.as_str(), operation),
                    ("course", "answer") | ("homework", "review" | "explain"));
                if !valid { return Err(Fault("BAD_REQUEST", "operation is not allowed for consumer")); }
                if let Some(previous) = self.active.get(&key) {
                    if generation <= previous.generation {
                        return Err(Fault("STALE_REQUEST", "AI generation must advance"));
                    }
                }
                self.active.insert(key, Lease { request_id: request_id.clone(),
                    operation: operation.to_owned(), generation });
                Ok(json!({"requestId": request_id, "state": "RUNNING"}))
            }
            "ai.finish" | "ai.cancel" => {
                let request_id = Self::request_id(params)?;
                let lease = self.active.get(&key)
                    .ok_or(Fault("STALE_REQUEST", "AI request is no longer active"))?;
                if lease.request_id != request_id {
                    return Err(Fault("STALE_REQUEST", "AI request was superseded"));
                }
                self.active.remove(&key);
                Ok(json!({"requestId": request_id, "state": if method == "ai.finish" {"FINISHED"} else {"CANCELLED"}}))
            }
            "ai.state" => Ok(match self.active.get(&key) {
                Some(lease) => json!({"active": true, "requestId": lease.request_id,
                    "operation": lease.operation, "generation": lease.generation}),
                None => json!({"active": false}),
            }),
            _ => Err(Fault("UNKNOWN_METHOD", "AI method is not supported")),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const A: &str = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const B: &str = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    const R1: &str = "11111111111111111111111111111111";
    const R2: &str = "22222222222222222222222222222222";

    #[test]
    fn consumers_and_tabs_keep_independent_leases() {
        let mut module = AiModule::default();
        let course = json!({"scopeId": A, "consumer": "course", "requestId": R1,
            "generation": 1, "operation": "answer"});
        let homework = json!({"scopeId": A, "consumer": "homework", "requestId": R1,
            "generation": 1, "operation": "review"});
        let other_tab = json!({"scopeId": B, "consumer": "course", "requestId": R1,
            "generation": 1, "operation": "answer"});
        for request in [&course, &homework, &other_tab] {
            assert!(module.handle("ai.begin", request).is_ok());
        }
        assert!(module.handle("ai.finish", &course).is_ok());
        assert_eq!(module.handle("ai.state", &homework).unwrap()["active"], true);
        assert_eq!(module.handle("ai.state", &other_tab).unwrap()["active"], true);
        assert_eq!(module.handle("ai.begin", &json!({"scopeId": A,
            "consumer": "homework", "requestId": R2, "generation": 2,
            "operation": "explain"})).unwrap()["state"], "RUNNING");
        assert_eq!(module.handle("ai.finish", &homework).unwrap_err().0, "STALE_REQUEST");
    }

    #[test]
    fn consumer_cannot_dispatch_another_modules_operation() {
        let mut module = AiModule::default();
        let request = json!({"scopeId": A, "consumer": "course", "requestId": R1,
            "generation": 1, "operation": "review"});
        assert_eq!(module.handle("ai.begin", &request).unwrap_err().0, "BAD_REQUEST");
    }
}
