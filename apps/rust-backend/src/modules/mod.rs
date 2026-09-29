pub mod chaoxing;
pub mod smoke;
pub mod vault;

use serde_json::Value;
use crate::protocol::Fault;

/// A module owns its state and validates every command in its own namespace.
pub trait ModuleHandler {
    fn handle(&mut self, method: &str, params: &Value) -> Result<Value, Fault>;
}
