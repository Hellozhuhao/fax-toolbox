use std::io::{self, Write};
use std::path::PathBuf;

mod modules;
mod protocol;
mod registry;

use registry::Registry;

fn main() -> io::Result<()> {
    let stdin = io::stdin();
    let stdout = io::stdout();
    let mut reader = stdin.lock();
    let mut writer = stdout.lock();
    let data_dir = std::env::var_os("FAX_TOOLBOX_DATA_DIR")
        .map(PathBuf::from)
        .or_else(|| std::env::var_os("LOCALAPPDATA").map(|base| PathBuf::from(base).join("FaxToolbox")));
    let mut registry = data_dir.as_deref().map(Registry::with_data_dir).unwrap_or_default();
    while let Some(line) = protocol::read_line(&mut reader)? {
        let reply = match line {
            Ok(line) => protocol::handle_line(&mut registry, &line),
            Err(fault) => protocol::error_response(None, fault),
        };
        serde_json::to_writer(&mut writer, &reply)?;
        writer.write_all(b"\n")?;
        writer.flush()?;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use modules::smoke::MODULE_ID;
    use serde_json::{Value, json};

    fn call(registry: &mut Registry, id: &str, method: &str, params: Value) -> Value {
        protocol::handle_line(registry,
            &json!({"v": 1, "id": id, "method": method, "params": params}).to_string())
    }

    #[test]
    fn session_reaches_complete_and_rejects_duplicate_events() {
        let mut registry = Registry::default();
        let base = json!({"moduleId": MODULE_ID, "sessionId": "s1", "generation": 1});
        assert_eq!(call(&mut registry, "1", "session.open", base.clone())["result"]["state"], "CREATED");
        for (seq, event, state) in [(1, "HELLO", "READY"), (2, "VIDEO_FOUND", "OBSERVING"),
                                     (3, "VIDEO_ENDED", "COMPLETE")] {
            let mut params = base.clone();
            params["frameId"] = json!("main");
            params["seq"] = json!(seq);
            params["event"] = json!(event);
            let reply = call(&mut registry, "2", "session.event", params.clone());
            assert_eq!(reply["result"]["state"], state);
            assert_eq!(call(&mut registry, "3", "session.event", params)["error"]["code"], "DUPLICATE");
        }
    }

    #[test]
    fn cancellation_and_stale_generation_are_rejected() {
        let mut registry = Registry::default();
        let base = json!({"moduleId": MODULE_ID, "sessionId": "s2", "generation": 2});
        call(&mut registry, "1", "session.open", base.clone());
        let mut stale = base.clone();
        stale["generation"] = json!(1);
        assert_eq!(call(&mut registry, "2", "session.state", stale)["error"]["code"], "STALE_SESSION");
        assert_eq!(call(&mut registry, "3", "session.cancel", base.clone())["result"]["state"], "CANCELLED");
        assert_eq!(call(&mut registry, "4", "session.cancel", base)["error"]["code"], "INVALID_STATE");
    }

    #[test]
    fn namespaces_are_isolated() {
        let mut registry = Registry::default();
        let smoke = json!({"moduleId": MODULE_ID, "sessionId": "s3", "generation": 1});
        assert_eq!(call(&mut registry, "1", "session.open", smoke)["ok"], true);
        assert_eq!(call(&mut registry, "2", "vault.list", json!({}))["result"]["entries"], json!([]));
        assert_eq!(call(&mut registry, "3", "course.state", json!({"sessionId":"s3","generation":1}))["ok"], false);
        assert_eq!(call(&mut registry, "4", "unknown.open", json!({}))["error"]["code"], "UNKNOWN_METHOD");
    }
}
