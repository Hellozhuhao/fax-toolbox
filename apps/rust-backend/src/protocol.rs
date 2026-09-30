use std::io::{self, BufRead};
use serde_json::{Value, json};
use crate::registry::Registry;

pub const PROTOCOL_VERSION: u64 = 1;
pub const MAX_LINE_BYTES: usize = 32 * 1024;

#[derive(Debug)]
pub struct Fault(pub &'static str, pub &'static str);

pub fn text<'a>(value: &'a Value, key: &str) -> Result<&'a str, Fault> {
    let result = value.get(key).and_then(Value::as_str)
        .ok_or(Fault("BAD_REQUEST", "required text field is missing"))?;
    if result.is_empty() || result.len() > 128 {
        return Err(Fault("BAD_REQUEST", "text field length is invalid"));
    }
    Ok(result)
}

pub fn positive(value: &Value, key: &str) -> Result<u64, Fault> {
    value.get(key).and_then(Value::as_u64).filter(|number| *number > 0)
        .ok_or(Fault("BAD_REQUEST", "required positive integer is missing"))
}

pub fn error_response(id: Option<&str>, Fault(code, message): Fault) -> Value {
    json!({"v": PROTOCOL_VERSION, "id": id, "ok": false,
           "error": {"code": code, "message": message}})
}

pub fn handle_line(registry: &mut Registry, line: &str) -> Value {
    if line.len() > MAX_LINE_BYTES {
        return error_response(None, Fault("TOO_LARGE", "request exceeds 32 KiB"));
    }
    let request: Value = match serde_json::from_str(line) {
        Ok(value) => value,
        Err(_) => return error_response(None, Fault("BAD_JSON", "invalid JSON")),
    };
    let id = match request.get("id").and_then(Value::as_str)
        .filter(|value| !value.is_empty() && value.len() <= 128) {
        Some(value) => value.to_owned(),
        None => return error_response(None, Fault("BAD_REQUEST", "invalid request ID")),
    };
    let result = (|| {
        if request.get("v").and_then(Value::as_u64) != Some(PROTOCOL_VERSION) {
            return Err(Fault("BAD_VERSION", "unsupported protocol version"));
        }
        let method = text(&request, "method")?;
        let params = request.get("params").filter(|item| item.is_object())
            .ok_or(Fault("BAD_REQUEST", "params must be an object"))?;
        registry.request(method, params)
    })();
    match result {
        Ok(value) => json!({"v": PROTOCOL_VERSION, "id": id, "ok": true, "result": value}),
        Err(fault) => error_response(Some(&id), fault),
    }
}

/// Reads at most MAX_LINE_BYTES into memory, draining an oversized line so the
/// next request remains aligned with the JSON-lines transport.
pub fn read_line<R: BufRead>(reader: &mut R) -> io::Result<Option<Result<String, Fault>>> {
    let mut bytes = Vec::with_capacity(1024);
    let mut saw_data = false;
    let mut too_large = false;
    loop {
        let available = reader.fill_buf()?;
        if available.is_empty() { break; }
        saw_data = true;
        let newline = available.iter().position(|byte| *byte == b'\n');
        let count = newline.map_or(available.len(), |position| position + 1);
        if !too_large {
            if bytes.len() + count <= MAX_LINE_BYTES { bytes.extend_from_slice(&available[..count]); }
            else { too_large = true; }
        }
        reader.consume(count);
        if newline.is_some() { break; }
    }
    if !saw_data { return Ok(None); }
    if too_large {
        return Ok(Some(Err(Fault("TOO_LARGE", "request exceeds 32 KiB"))));
    }
    Ok(Some(String::from_utf8(bytes).map_err(|_| Fault("BAD_JSON", "invalid UTF-8"))))
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Cursor;

    #[test]
    fn oversized_line_is_drained_without_losing_next_request() {
        let mut input = Vec::new();
        input.extend(std::iter::repeat_n(b'x', MAX_LINE_BYTES));
        input.extend_from_slice(b"\n{\"v\":1}\n");
        let mut reader = Cursor::new(input);
        assert_eq!(read_line(&mut reader).unwrap().unwrap().unwrap_err().0, "TOO_LARGE");
        assert_eq!(read_line(&mut reader).unwrap().unwrap().unwrap(), "{\"v\":1}\n");
    }
}
