use serde_json::{Value, json};
use std::collections::HashMap;
use std::fs;
use std::path::PathBuf;

use crate::{Fault, text};

#[derive(Default)]
pub struct VaultStore {
    entries: HashMap<String, Value>,
    path: Option<PathBuf>,
    load_error: bool,
}

impl VaultStore {
    pub fn with_path(path: PathBuf) -> Self {
        let (entries, load_error) = match fs::read(&path) {
            Ok(bytes) => match serde_json::from_slice::<HashMap<String, Value>>(&bytes) {
                Ok(entries) => (entries, false),
                Err(_) => (HashMap::new(), true),
            },
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => (HashMap::new(), false),
            Err(_) => (HashMap::new(), true),
        };
        Self { entries, path: Some(path), load_error }
    }

    fn save(&self) -> Result<(), Fault> {
        let Some(path) = &self.path else { return Ok(()); };
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent).map_err(|_| Fault("IO_ERROR", "cannot create vault directory"))?;
        }
        let bytes = serde_json::to_vec(&self.entries)
            .map_err(|_| Fault("IO_ERROR", "cannot serialize vault"))?;
        let temporary = path.with_extension("tmp");
        fs::write(&temporary, bytes).map_err(|_| Fault("IO_ERROR", "cannot write vault"))?;
        fs::rename(&temporary, path).map_err(|_| Fault("IO_ERROR", "cannot replace vault"))
    }

    pub fn request(&mut self, method: &str, params: &Value) -> Result<Value, Fault> {
        if self.load_error { return Err(Fault("IO_ERROR", "vault file cannot be read; preserving it")); }
        match method {
            "vault.list" => {
                let mut entries: Vec<Value> = self.entries.iter().map(|(id, record)| {
                    json!({"id": id, "encryptedUsername": record["encryptedUsername"]})
                }).collect();
                entries.sort_by(|a, b| a["id"].as_str().cmp(&b["id"].as_str()));
                Ok(json!({"entries": entries}))
            }
            "vault.put" => {
                let id = text(params, "id")?;
                let encrypted_username = params.get("encryptedUsername").and_then(Value::as_str)
                    .filter(|value| !value.is_empty() && value.len() <= 4096 &&
                            value.bytes().all(|byte| byte.is_ascii_alphanumeric() || byte == b'+' || byte == b'/' || byte == b'='))
                    .ok_or(Fault("BAD_REQUEST", "invalid protected username"))?;
                let ciphertext = params.get("ciphertext").and_then(Value::as_str)
                    .filter(|value| !value.is_empty() && value.len() <= 16 * 1024 &&
                            value.bytes().all(|byte| byte.is_ascii_alphanumeric() || byte == b'+' || byte == b'/' || byte == b'='))
                    .ok_or(Fault("BAD_REQUEST", "invalid protected credential"))?;
                if self.entries.len() >= 32 && !self.entries.contains_key(id) {
                    return Err(Fault("BAD_REQUEST", "vault entry limit exceeded"));
                }
                let previous = self.entries.insert(id.to_owned(), json!({"encryptedUsername": encrypted_username,
                    "ciphertext": ciphertext}));
                if let Err(error) = self.save() {
                    if let Some(record) = previous { self.entries.insert(id.to_owned(), record); }
                    else { self.entries.remove(id); }
                    return Err(error);
                }
                Ok(json!({"id": id}))
            }
            "vault.delete" => {
                let id = text(params, "id")?;
                let previous = self.entries.remove(id)
                    .ok_or(Fault("NOT_FOUND", "credential does not exist"))?;
                if let Err(error) = self.save() {
                    self.entries.insert(id.to_owned(), previous);
                    return Err(error);
                }
                Ok(json!({"deleted": id}))
            }
            "vault.get" => {
                let id = text(params, "id")?;
                self.entries.get(id).cloned()
                    .ok_or(Fault("NOT_FOUND", "credential does not exist"))
            }
            _ => Err(Fault("UNKNOWN_METHOD", "vault method is not supported")),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn listing_never_contains_password_ciphertext() {
        let mut vault = VaultStore::default();
        vault.request("vault.put", &json!({"id":"chaoxing","encryptedUsername":"U1RVREVOVA==",
            "ciphertext":"QUJDRA=="})).unwrap();
        let list = vault.request("vault.list", &json!({})).unwrap().to_string();
        assert!(list.contains("U1RVREVOVA=="));
        assert!(!list.contains("QUJDRA=="));
        assert_eq!(vault.request("vault.get", &json!({"id":"chaoxing"})).unwrap()["ciphertext"], "QUJDRA==");
    }

    #[test]
    fn protected_entries_survive_replacement_and_restart() {
        let stamp = SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos();
        let directory = std::env::temp_dir().join(format!("fax-vault-test-{}-{stamp}", std::process::id()));
        let path = directory.join("vault.json");
        let mut vault = VaultStore::with_path(path.clone());
        vault.request("vault.put", &json!({"id":"first","encryptedUsername":"YQ==","ciphertext":"QUJD"})).unwrap();
        vault.request("vault.put", &json!({"id":"second","encryptedUsername":"Yg==","ciphertext":"REVG"})).unwrap();
        let restored = VaultStore::with_path(path);
        assert_eq!(restored.entries.len(), 2);
        assert_eq!(restored.entries["second"]["ciphertext"], "REVG");
        fs::remove_dir_all(directory).unwrap();
    }

    #[test]
    fn damaged_vault_is_not_overwritten() {
        let stamp = SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos();
        let directory = std::env::temp_dir().join(format!("fax-vault-damaged-{}-{stamp}", std::process::id()));
        fs::create_dir_all(&directory).unwrap();
        let path = directory.join("vault.json");
        fs::write(&path, b"{not valid json").unwrap();
        let mut vault = VaultStore::with_path(path.clone());
        let error = vault.request("vault.put", &json!({"id":"x","encryptedUsername":"YQ==",
            "ciphertext":"Yg=="})).unwrap_err();
        assert_eq!(error.0, "IO_ERROR");
        assert_eq!(fs::read(&path).unwrap(), b"{not valid json");
        fs::remove_dir_all(directory).unwrap();
    }
}
