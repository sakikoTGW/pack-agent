use std::path::PathBuf;
use std::process::Command;

#[tauri::command]
fn launcher_api(method: String, params: String) -> Result<String, String> {
  let root = std::env::var("PACK_LAUNCHER_ROOT")
    .map_err(|_| "PACK_LAUNCHER_ROOT is required".to_string())?;
  let bridge = std::env::var("PACK_LAUNCHER_BRIDGE").unwrap_or_else(|_| {
    PathBuf::from(env!("CARGO_MANIFEST_DIR"))
      .join("../../modpack/tauri-bridge.ts")
      .to_string_lossy()
      .into_owned()
  });
  let out = Command::new("bun")
    .args([&bridge, &method, &params])
    .env("PACK_LAUNCHER_ROOT", &root)
    .output()
    .map_err(|e| e.to_string())?;
  let stdout = String::from_utf8_lossy(&out.stdout).to_string();
  let stderr = String::from_utf8_lossy(&out.stderr).to_string();
  if stdout.trim().is_empty() && !out.status.success() {
    return Err(stderr);
  }
  Ok(stdout)
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
  tauri::Builder::default()
    .invoke_handler(tauri::generate_handler![launcher_api])
    .run(tauri::generate_context!())
    .expect("error while running tauri application");
}
