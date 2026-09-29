mod catalog;
mod course;

use std::path::Path;
use serde_json::Value;
use crate::modules::ModuleHandler;
use crate::protocol::Fault;

#[derive(Default)]
pub struct ChaoxingModule {
    course: course::CourseEngine,
    catalog: catalog::CatalogStore,
}

impl ChaoxingModule {
    pub fn with_data_dir(root: &Path) -> Self {
        // Preserve existing user data locations; ownership is now inside this module.
        Self {
            course: course::CourseEngine::with_path(root.join("answers.json")),
            catalog: catalog::CatalogStore::with_path(root.join("catalog.json")),
        }
    }
}

impl ModuleHandler for ChaoxingModule {
    fn handle(&mut self, method: &str, params: &Value) -> Result<Value, Fault> {
        match method.split_once('.').map(|(namespace, _)| namespace) {
            Some("course" | "answers") => self.course.request(method, params),
            Some("catalog") => self.catalog.request(method, params),
            _ => Err(Fault("UNKNOWN_METHOD", "Chaoxing method is not supported")),
        }
    }
}
