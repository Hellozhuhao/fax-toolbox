use std::path::Path;
use serde_json::{Value, json};
use crate::modules::{ModuleHandler, ai::AiModule, chaoxing::ChaoxingModule, smoke::SmokeModule, vault::VaultStore};
use crate::protocol::{Fault, PROTOCOL_VERSION};

/// The only place that maps protocol namespaces to owners. Modules cannot reach
/// each other's state or dispatch commands into another namespace.
#[derive(Default)]
pub struct Registry {
    smoke: SmokeModule,
    ai: AiModule,
    chaoxing: ChaoxingModule,
    vault: VaultStore,
}

impl Registry {
    pub fn with_data_dir(root: &Path) -> Self {
        Self {
            smoke: SmokeModule::default(),
            ai: AiModule::default(),
            chaoxing: ChaoxingModule::with_data_dir(root),
            vault: VaultStore::with_path(root.join("vault.json")),
        }
    }

    pub fn request(&mut self, method: &str, params: &Value) -> Result<Value, Fault> {
        match method.split_once('.') {
            Some(("system", "ping")) => Ok(json!({"name": "toolbox-backend", "protocol": PROTOCOL_VERSION})),
            Some(("session", _)) => self.smoke.handle(method, params),
            Some(("ai", _)) => self.ai.handle(method, params),
            Some(("course" | "answers" | "catalog", _)) => self.chaoxing.handle(method, params),
            Some(("vault", _)) => self.vault.handle(method, params),
            _ => Err(Fault("UNKNOWN_METHOD", "method is not supported")),
        }
    }
}
