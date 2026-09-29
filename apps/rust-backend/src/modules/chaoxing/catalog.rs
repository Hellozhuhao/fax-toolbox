use serde_json::{Value, json};
use std::collections::HashMap;
use std::fs;
use std::path::PathBuf;

use crate::protocol::Fault;

#[derive(Default)]
pub struct CatalogStore {
    entries: HashMap<String, Vec<Value>>,
    path: Option<PathBuf>,
    load_error: bool,
}

impl CatalogStore {
    pub fn with_path(path: PathBuf) -> Self {
        let (entries, load_error) = match fs::read(&path) {
            Ok(bytes) => match serde_json::from_slice::<HashMap<String, Vec<Value>>>(&bytes) {
                Ok(value) => (value, false),
                Err(_) => (HashMap::new(), true),
            },
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => (HashMap::new(), false),
            Err(_) => (HashMap::new(), true),
        };
        Self { entries, path: Some(path), load_error }
    }

    pub fn request(&mut self, method: &str, params: &Value) -> Result<Value, Fault> {
        if self.load_error { return Err(Fault("IO_ERROR", "catalog cannot be read; preserving it")); }
        let key = params.get("courseKey").and_then(Value::as_str)
            .filter(|value| value.len() == 64 && value.bytes().all(|byte| byte.is_ascii_hexdigit()))
            .ok_or(Fault("BAD_REQUEST", "invalid course key"))?;
        match method {
            "catalog.get" => Ok(json!({"entries": self.entries.get(key).cloned().unwrap_or_default()})),
            "catalog.put" => {
                let incoming = params.get("entries").and_then(Value::as_array)
                    .filter(|items| !items.is_empty() && items.len() <= 120)
                    .ok_or(Fault("BAD_REQUEST", "invalid catalog size"))?;
                let mut sanitized = Vec::with_capacity(incoming.len());
                let mut seen = std::collections::HashSet::new();
                for entry in incoming {
                    let id = entry.get("id").and_then(Value::as_str)
                        .filter(|value| !value.is_empty() && value.len() <= 128)
                        .ok_or(Fault("BAD_REQUEST", "invalid chapter ID"))?;
                    let name = entry.get("name").and_then(Value::as_str)
                        .map(str::trim).filter(|value| !value.is_empty() && value.chars().count() <= 80)
                        .ok_or(Fault("BAD_REQUEST", "invalid chapter name"))?;
                    if !seen.insert(id) { return Err(Fault("BAD_REQUEST", "duplicate chapter ID")); }
                    sanitized.push(json!({"id": id, "name": name}));
                }
                if self.entries.len() >= 128 && !self.entries.contains_key(key) {
                    return Err(Fault("BAD_REQUEST", "catalog limit exceeded"));
                }
                let previous = self.entries.insert(key.to_owned(), sanitized.clone());
                if let Err(error) = self.save() {
                    if let Some(value) = previous { self.entries.insert(key.to_owned(), value); }
                    else { self.entries.remove(key); }
                    return Err(error);
                }
                Ok(json!({"count": sanitized.len()}))
            }
            _ => Err(Fault("UNKNOWN_METHOD", "catalog method is not supported")),
        }
    }

    fn save(&self) -> Result<(), Fault> {
        let Some(path) = &self.path else { return Ok(()); };
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent).map_err(|_| Fault("IO_ERROR", "cannot create data directory"))?;
        }
        let temporary = path.with_extension("tmp");
        let bytes = serde_json::to_vec(&self.entries)
            .map_err(|_| Fault("IO_ERROR", "cannot serialize catalog"))?;
        fs::write(&temporary, bytes).map_err(|_| Fault("IO_ERROR", "cannot write catalog"))?;
        fs::rename(&temporary, path).map_err(|_| Fault("IO_ERROR", "cannot replace catalog"))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn catalog_preserves_names_and_rejects_unexpected_fields() {
        let stamp = SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos();
        let dir = std::env::temp_dir().join(format!("fax-catalog-test-{}-{stamp}", std::process::id()));
        let path = dir.join("catalog.json");
        let key = "a".repeat(64);
        let mut store = CatalogStore::with_path(path.clone());
        store.request("catalog.put", &json!({"courseKey":key,"entries":[
            {"id":"123","name":"2.1 课程介绍","password":"secret"}]})).unwrap();
        store.request("catalog.put", &json!({"courseKey":key,"entries":[
            {"id":"123","name":"2.1 课程介绍"},{"id":"124","name":"2.2 视频任务"}]})).unwrap();
        let restored = CatalogStore::with_path(path.clone());
        let entries = restored.entries.get(&key).unwrap();
        assert_eq!(entries[0]["name"], "2.1 课程介绍");
        assert_eq!(entries[1]["name"], "2.2 视频任务");
        assert!(entries[0].get("password").is_none());
        assert!(!fs::read_to_string(path).unwrap().contains("secret"));
        fs::remove_dir_all(dir).unwrap();
    }
}
