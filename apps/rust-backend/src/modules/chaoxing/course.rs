use serde_json::{Value, json};
use std::collections::HashMap;
use std::fs;
use std::path::PathBuf;

use crate::protocol::{Fault, positive, text};

const MAX_ANSWER_CHAPTERS: usize = 512;
const MAX_ANSWERS_PER_CHAPTER: usize = 200;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum CourseState {
    Ready,
    Running,
    Paused,
    NeedsInput,
    Stopped,
}

impl CourseState {
    fn as_str(self) -> &'static str {
        match self {
            Self::Ready => "READY",
            Self::Running => "RUNNING",
            Self::Paused => "PAUSED",
            Self::NeedsInput => "NEEDS_INPUT",
            Self::Stopped => "STOPPED",
        }
    }
}

struct CourseSession {
    generation: u64,
    course_url: String,
    state: CourseState,
    last_seq: HashMap<String, u64>,
    last_action: String,
}

#[derive(Default)]
pub struct CourseEngine {
    sessions: HashMap<String, CourseSession>,
    answers: HashMap<String, Value>,
    answers_path: Option<PathBuf>,
    load_error: bool,
}

impl CourseEngine {
    pub fn with_path(path: PathBuf) -> Self {
        let (answers, load_error) = match fs::read(&path) {
            Ok(bytes) => match serde_json::from_slice::<HashMap<String, Value>>(&bytes) {
                Ok(answers) => (answers, false),
                Err(_) => (HashMap::new(), true),
            },
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => (HashMap::new(), false),
            Err(_) => (HashMap::new(), true),
        };
        Self { answers, answers_path: Some(path), load_error, ..Self::default() }
    }

    fn save_answers(&self) -> Result<(), Fault> {
        let Some(path) = &self.answers_path else { return Ok(()); };
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent).map_err(|_| Fault("IO_ERROR", "cannot create data directory"))?;
        }
        let bytes = serde_json::to_vec(&self.answers)
            .map_err(|_| Fault("IO_ERROR", "cannot serialize answer bank"))?;
        let temporary = path.with_extension("tmp");
        fs::write(&temporary, bytes).map_err(|_| Fault("IO_ERROR", "cannot write answer bank"))?;
        fs::rename(&temporary, path).map_err(|_| Fault("IO_ERROR", "cannot replace answer bank"))
    }

    fn session<'a>(&'a mut self, params: &Value) -> Result<&'a mut CourseSession, Fault> {
        let session_id = text(params, "sessionId")?;
        let generation = positive(params, "generation")?;
        let session = self.sessions.get_mut(session_id)
            .ok_or(Fault("NOT_FOUND", "course session does not exist"))?;
        if session.generation != generation {
            return Err(Fault("STALE_SESSION", "course generation mismatch"));
        }
        Ok(session)
    }

    pub fn request(&mut self, method: &str, params: &Value) -> Result<Value, Fault> {
        if self.load_error { return Err(Fault("IO_ERROR", "answer bank cannot be read; preserving it")); }
        match method {
            "course.open" => {
                let id = text(params, "sessionId")?;
                let generation = positive(params, "generation")?;
                let course_url = params.get("courseUrl").and_then(Value::as_str)
                    .filter(|url| !url.is_empty() && url.len() <= 4096)
                    .ok_or(Fault("BAD_REQUEST", "invalid course URL"))?;
                let authority = course_url.strip_prefix("https://")
                    .and_then(|url| url.split('/').next()).unwrap_or("");
                if !authority.ends_with(".chaoxing.com") || authority.contains('@') {
                    return Err(Fault("BAD_REQUEST", "course URL must use Chaoxing HTTPS"));
                }
                if self.sessions.contains_key(id) {
                    return Err(Fault("ALREADY_OPEN", "course session already exists"));
                }
                self.sessions.insert(id.to_owned(), CourseSession {
                    generation, course_url: course_url.to_owned(), state: CourseState::Ready,
                    last_seq: HashMap::new(), last_action: String::new(),
                });
                Ok(json!({"state": "READY", "sessionId": id}))
            }
            "course.command" => {
                let command = text(params, "command")?;
                let session = self.session(params)?;
                session.state = match (session.state, command) {
                    (CourseState::Ready | CourseState::Paused | CourseState::NeedsInput, "start") => CourseState::Running,
                    (CourseState::Running, "pause") => CourseState::Paused,
                    (_, "stop") => CourseState::Stopped,
                    _ => return Err(Fault("INVALID_STATE", "course command is not valid in current state")),
                };
                Ok(json!({"state": session.state.as_str(), "courseUrl": session.course_url}))
            }
            "course.state" => {
                let session = self.session(params)?;
                Ok(json!({"state": session.state.as_str(), "courseUrl": session.course_url,
                           "lastAction": session.last_action}))
            }
            "course.observe" => self.observe(params),
            "answers.put" => {
                let chapter = text(params, "chapter")?;
                let answers = params.get("answers").and_then(Value::as_array)
                    .ok_or(Fault("BAD_REQUEST", "answers must be an array"))?;
                if answers.is_empty() || answers.len() > MAX_ANSWERS_PER_CHAPTER ||
                    self.answers.len() >= MAX_ANSWER_CHAPTERS && !self.answers.contains_key(chapter) {
                    return Err(Fault("BAD_REQUEST", "answer bank limit exceeded"));
                }
                for entry in answers {
                    positive(entry, "number")?;
                    let answer = entry.get("answer").and_then(Value::as_str)
                        .filter(|value| !value.trim().is_empty() && value.len() <= 512)
                        .ok_or(Fault("BAD_REQUEST", "answer text is invalid"))?;
                    if answer.contains('\0') { return Err(Fault("BAD_REQUEST", "answer contains a null byte")); }
                }
                let previous = self.answers.insert(chapter.to_owned(), Value::Array(answers.clone()));
                if let Err(error) = self.save_answers() {
                    if let Some(record) = previous { self.answers.insert(chapter.to_owned(), record); }
                    else { self.answers.remove(chapter); }
                    return Err(error);
                }
                Ok(json!({"chapter": chapter, "count": answers.len()}))
            }
            "answers.list" => {
                let mut chapters: Vec<_> = self.answers.keys().cloned().collect();
                chapters.sort();
                Ok(json!({"chapters": chapters}))
            }
            _ => Err(Fault("UNKNOWN_METHOD", "course method is not supported")),
        }
    }

    fn observe(&mut self, params: &Value) -> Result<Value, Fault> {
        let frame = text(params, "frameId")?.to_owned();
        let seq = positive(params, "seq")?;
        let kind = text(params, "kind")?.to_owned();
        let chapter = params.get("chapter").and_then(Value::as_str).unwrap_or("").to_owned();
        let answers = self.answers.get(&chapter).cloned();
        let session = self.session(params)?;
        if session.state != CourseState::Running {
            return Ok(json!({"state": session.state.as_str(), "action": "NONE"}));
        }
        let last = session.last_seq.entry(frame).or_default();
        if seq <= *last { return Err(Fault("DUPLICATE", "course event sequence did not increase")); }
        *last = seq;
        let (action, data) = match kind.as_str() {
            "VIDEO_FOUND" => ("PLAY_VIDEO", Value::Null),
            "VIDEO_TAB_FOUND" => ("SWITCH_VIDEO_TAB", Value::Null),
            "PROMPT_FOUND" => ("DISMISS_PROMPT", Value::Null),
            "VIDEO_ENDED" => ("ADVANCE_AFTER_VIDEO", Value::Null),
            "DOCUMENT_DONE" | "UNIT_EMPTY" => ("NEXT_UNIT", Value::Null),
            "DOCUMENT_FOUND" => ("DOCUMENT_NEXT", Value::Null),
            "QUIZ_FOUND" => match answers {
                Some(data) => ("FILL_QUIZ", data),
                None => {
                    session.state = CourseState::NeedsInput;
                    ("NEEDS_ANSWERS", Value::Null)
                }
            },
            "QUIZ_UNMATCHED" => {
                session.state = CourseState::NeedsInput;
                ("REVIEW_QUIZ", Value::Null)
            }
            "QUIZ_SUBMIT_UNCONFIRMED" => {
                session.state = CourseState::NeedsInput;
                ("REVIEW_SUBMISSION", Value::Null)
            }
            "CHALLENGE_FOUND" => {
                session.state = CourseState::NeedsInput;
                ("WAIT_FOR_HUMAN", Value::Null)
            }
            "PLAYBACK_BLOCKED" => {
                session.state = CourseState::NeedsInput;
                ("MANUAL_PLAY", Value::Null)
            }
            "PLAYBACK_FAILED" => {
                session.state = CourseState::NeedsInput;
                ("CHECK_VIDEO", Value::Null)
            }
            "QUIZ_CONFIRM_PENDING" => ("CONFIRM_QUIZ_SUBMISSION", Value::Null),
            "QUIZ_SUBMITTED" => ("ACK_QUIZ_SUBMISSION", Value::Null),
            "QUIZ_RESULT_READY" => ("NEXT_UNIT", Value::Null),
            "NEXT_UNAVAILABLE" => ("NONE", Value::Null),
            _ => return Err(Fault("BAD_REQUEST", "unsupported course observation")),
        };
        session.last_action = action.to_owned();
        Ok(json!({"state": session.state.as_str(), "action": action, "data": data,
                   "chapter": chapter}))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn course_requires_start_and_halts_for_unknown_quiz() {
        let mut engine = CourseEngine::default();
        let base = json!({"sessionId":"s1","generation":1});
        engine.request("course.open", &json!({"sessionId":"s1","generation":1,
            "courseUrl":"https://mooc1.chaoxing.com/mycourse/studentstudy"})).unwrap();
        assert_eq!(engine.request("course.observe", &json!({"sessionId":"s1","generation":1,
            "frameId":"main","seq":1,"kind":"VIDEO_FOUND"})).unwrap()["action"], "NONE");
        let mut start = base.clone(); start["command"] = json!("start");
        engine.request("course.command", &start).unwrap();
        assert_eq!(engine.request("course.observe", &json!({"sessionId":"s1","generation":1,
            "frameId":"main","seq":1,"kind":"QUIZ_FOUND","chapter":"1.1"})).unwrap()["state"], "NEEDS_INPUT");
    }

    #[test]
    fn bank_answers_are_returned_only_for_matching_chapter() {
        let mut engine = CourseEngine::default();
        engine.request("answers.put", &json!({"chapter":"2.1","answers":[{"number":1,"answer":"A"}]})).unwrap();
        engine.request("course.open", &json!({"sessionId":"s2","generation":1,
            "courseUrl":"https://mooc1.chaoxing.com/mycourse/studentstudy"})).unwrap();
        engine.request("course.command", &json!({"sessionId":"s2","generation":1,"command":"start"})).unwrap();
        let result = engine.request("course.observe", &json!({"sessionId":"s2","generation":1,
            "frameId":"quiz","seq":1,"kind":"QUIZ_FOUND","chapter":"2.1"})).unwrap();
        assert_eq!(result["action"], "FILL_QUIZ");
        assert_eq!(result["data"][0]["answer"], "A");
    }

    #[test]
    fn verification_and_blocked_playback_require_manual_resume() {
        let mut engine = CourseEngine::default();
        let base = json!({"sessionId":"manual","generation":1});
        engine.request("course.open", &json!({"sessionId":"manual","generation":1,
            "courseUrl":"https://mooc1.chaoxing.com/mycourse/studentstudy"})).unwrap();
        engine.request("course.command", &json!({"sessionId":"manual","generation":1,"command":"start"})).unwrap();
        let challenge = engine.request("course.observe", &json!({"sessionId":"manual","generation":1,
            "frameId":"main","seq":1,"kind":"CHALLENGE_FOUND"})).unwrap();
        assert_eq!(challenge["state"], "NEEDS_INPUT");
        assert_eq!(challenge["action"], "WAIT_FOR_HUMAN");
        assert_eq!(engine.request("course.observe", &json!({"sessionId":"manual","generation":1,
            "frameId":"main","seq":2,"kind":"VIDEO_FOUND"})).unwrap()["action"], "NONE");
        let mut resume = base.clone(); resume["command"] = json!("start");
        engine.request("course.command", &resume).unwrap();
        let playback = engine.request("course.observe", &json!({"sessionId":"manual","generation":1,
            "frameId":"main","seq":2,"kind":"PLAYBACK_BLOCKED"})).unwrap();
        assert_eq!(playback["state"], "NEEDS_INPUT");
        assert_eq!(playback["action"], "MANUAL_PLAY");
        engine.request("course.command", &resume).unwrap();
        let failed = engine.request("course.observe", &json!({"sessionId":"manual","generation":1,
            "frameId":"main","seq":3,"kind":"PLAYBACK_FAILED"})).unwrap();
        assert_eq!(failed["state"], "NEEDS_INPUT");
        assert_eq!(failed["action"], "CHECK_VIDEO");
    }
}
