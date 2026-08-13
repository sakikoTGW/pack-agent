//! pack-index — SQLite FTS5 catalog for projected Agent Modpacks.
//!
//! Commands: index | search | allow | deny | list | snapshot
//! Always prints one JSON object on stdout.

use anyhow::{anyhow, bail, Context, Result};
use rusqlite::{params, Connection};
use serde::Deserialize;
use serde_json::{json, Value};
use std::collections::HashSet;
use std::env;
use std::fs;
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};
use walkdir::WalkDir;

#[derive(Deserialize)]
struct CatalogFile {
    id: String,
    #[serde(default)]
    name: String,
    #[serde(default)]
    version: String,
    #[serde(default)]
    description: String,
    #[serde(default)]
    units: Vec<CatalogUnit>,
}

#[derive(Deserialize)]
struct CatalogUnit {
    kind: String,
    name: String,
    #[serde(default)]
    path: String,
}

struct Cli {
    cmd: String,
    db: PathBuf,
    root: Option<PathBuf>,
    positional: Vec<String>,
    enabled_only: bool,
}

fn main() {
    match run() {
        Ok(value) => {
            println!("{}", value);
        }
        Err(err) => {
            eprintln!("{err:#}");
            println!("{}", json!({ "ok": false, "error": err.to_string() }));
            std::process::exit(1);
        }
    }
}

fn run() -> Result<Value> {
    let cli = parse_args()?;
    if let Some(parent) = cli.db.parent() {
        if !parent.as_os_str().is_empty() {
            fs::create_dir_all(parent)
                .with_context(|| format!("create db dir {}", parent.display()))?;
        }
    }
    let conn = Connection::open(&cli.db)
        .with_context(|| format!("open {}", cli.db.display()))?;
    init_schema(&conn)?;
    match cli.cmd.as_str() {
        "index" => {
            let root = cli
                .root
                .ok_or_else(|| anyhow!("index requires --root <modpacks-dir>"))?;
            cmd_index(&conn, &root)
        }
        "search" => {
            let query = cli.positional.join(" ");
            if query.is_empty() {
                bail!("search requires a query");
            }
            cmd_search(&conn, &query)
        }
        "allow" => {
            let id = cli
                .positional
                .first()
                .ok_or_else(|| anyhow!("allow requires <pack-id>"))?;
            cmd_set_enabled(&conn, id, true)
        }
        "deny" => {
            let id = cli
                .positional
                .first()
                .ok_or_else(|| anyhow!("deny requires <pack-id>"))?;
            cmd_set_enabled(&conn, id, false)
        }
        "list" => cmd_list(&conn, cli.enabled_only),
        "snapshot" => cmd_snapshot(&conn),
        other => bail!("unknown command {other}; use index|search|allow|deny|list|snapshot"),
    }
}

fn parse_args() -> Result<Cli> {
    let raw: Vec<String> = env::args().skip(1).collect();
    let mut db = None;
    let mut root = None;
    let mut enabled_only = false;
    let mut positional: Vec<String> = Vec::new();
    let mut i = 0;
    while i < raw.len() {
        match raw[i].as_str() {
            "--db" => {
                i += 1;
                let v = raw.get(i).ok_or_else(|| anyhow!("--db needs a path"))?;
                db = Some(PathBuf::from(v));
            }
            "--root" => {
                i += 1;
                let v = raw.get(i).ok_or_else(|| anyhow!("--root needs a path"))?;
                root = Some(PathBuf::from(v));
            }
            "--json" | "--json=true" => {}
            "--enabled" => enabled_only = true,
            "--help" | "-h" => {
                eprintln!(
                    "Usage: pack-index <index|search|allow|deny|list|snapshot> --db <file> [--root dir] [--enabled] [--json]"
                );
                std::process::exit(0);
            }
            s if s.starts_with('-') => bail!("unknown flag {s}"),
            s => positional.push(s.to_string()),
        }
        i += 1;
    }
    let cmd = positional
        .first()
        .cloned()
        .ok_or_else(|| anyhow!("missing command"))?;
    let rest = positional.into_iter().skip(1).collect();
    Ok(Cli {
        cmd,
        db: db.ok_or_else(|| anyhow!("--db is required"))?,
        root,
        positional: rest,
        enabled_only,
    })
}

fn init_schema(conn: &Connection) -> Result<()> {
    conn.execute_batch(
        r#"
        PRAGMA journal_mode=WAL;
        PRAGMA synchronous=NORMAL;
        CREATE TABLE IF NOT EXISTS packs (
          id TEXT PRIMARY KEY,
          name TEXT NOT NULL,
          version TEXT NOT NULL DEFAULT '',
          description TEXT NOT NULL DEFAULT '',
          dir TEXT NOT NULL,
          enabled INTEGER NOT NULL DEFAULT 0,
          indexed_at INTEGER NOT NULL DEFAULT 0
        );
        CREATE TABLE IF NOT EXISTS units (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          pack_id TEXT NOT NULL,
          kind TEXT NOT NULL,
          name TEXT NOT NULL,
          path TEXT NOT NULL DEFAULT '',
          body TEXT NOT NULL DEFAULT '',
          UNIQUE(pack_id, kind, name)
        );
        CREATE TABLE IF NOT EXISTS terms (
          term TEXT NOT NULL,
          unit_id INTEGER NOT NULL,
          PRIMARY KEY (term, unit_id)
        );
        CREATE INDEX IF NOT EXISTS idx_terms_term ON terms(term);
        "#,
    )?;
    Ok(())
}

fn cmd_index(conn: &Connection, root: &Path) -> Result<Value> {
    fs::create_dir_all(root).ok();
    let mut seen: HashSet<String> = HashSet::new();
    let mut pack_count = 0u64;
    let mut unit_count = 0u64;
    let indexed_at = now_secs();

    if root.is_dir() {
        for entry in fs::read_dir(root).with_context(|| format!("read {}", root.display()))? {
            let entry = entry?;
            if !entry.file_type()?.is_dir() {
                continue;
            }
            let dir = entry.path();
            if let Some((id, n_units)) = index_one_pack(conn, &dir, indexed_at)? {
                seen.insert(id);
                pack_count += 1;
                unit_count += n_units;
            }
        }
    }

    let existing: Vec<String> = {
        let mut stmt = conn.prepare("SELECT id FROM packs")?;
        let rows = stmt.query_map([], |row| row.get::<_, String>(0))?;
        rows.collect::<rusqlite::Result<Vec<_>>>()?
    };
    for id in existing {
        if !seen.contains(&id) {
            conn.execute("DELETE FROM units WHERE pack_id = ?1", params![id])?;
            conn.execute("DELETE FROM packs WHERE id = ?1", params![id])?;
        }
    }

    conn.execute_batch("DELETE FROM terms;")?;
    rebuild_terms(&conn)?;

    Ok(json!({
        "ok": true,
        "packs": pack_count,
        "units": unit_count
    }))
}

fn index_one_pack(conn: &Connection, dir: &Path, indexed_at: i64) -> Result<Option<(String, u64)>> {
    let catalog_path = dir.join("catalog.json");
    let (id, name, version, description, units) = if catalog_path.is_file() {
        let raw = fs::read_to_string(&catalog_path)
            .with_context(|| format!("read {}", catalog_path.display()))?;
        let doc: CatalogFile = serde_json::from_str(&raw)
            .with_context(|| format!("parse {}", catalog_path.display()))?;
        let id = if doc.id.trim().is_empty() {
            dir.file_name()
                .map(|s| s.to_string_lossy().into_owned())
                .unwrap_or_else(|| "pack".into())
        } else {
            doc.id
        };
        let name = if doc.name.trim().is_empty() {
            id.clone()
        } else {
            doc.name
        };
        (id, name, doc.version, doc.description, doc.units)
    } else if dir.join("package.json").is_file() {
        let pkg_raw = fs::read_to_string(dir.join("package.json"))?;
        let pkg: Value = serde_json::from_str(&pkg_raw).unwrap_or_else(|_| json!({}));
        let id = pkg
            .get("name")
            .and_then(|v| v.as_str())
            .unwrap_or("pack")
            .to_string();
        let version = pkg
            .get("version")
            .and_then(|v| v.as_str())
            .unwrap_or("")
            .to_string();
        let description = pkg
            .get("description")
            .and_then(|v| v.as_str())
            .unwrap_or("")
            .to_string();
        (id.clone(), id, version, description, walk_skill_units(dir))
    } else {
        return Ok(None);
    };

    conn.execute(
        r#"
        INSERT INTO packs (id, name, version, description, dir, enabled, indexed_at)
        VALUES (?1, ?2, ?3, ?4, ?5, 0, ?6)
        ON CONFLICT(id) DO UPDATE SET
          name=excluded.name,
          version=excluded.version,
          description=excluded.description,
          dir=excluded.dir,
          indexed_at=excluded.indexed_at
        "#,
        params![
            id,
            name,
            version,
            description,
            dir.to_string_lossy().to_string(),
            indexed_at
        ],
    )?;

    conn.execute("DELETE FROM units WHERE pack_id = ?1", params![id])?;

    let mut n = 0u64;
    for unit in units {
        if unit.name.trim().is_empty() {
            continue;
        }
        let abs = if unit.path.trim().is_empty() {
            String::new()
        } else {
            dir.join(&unit.path).to_string_lossy().into_owned()
        };
        let body = if abs.is_empty() {
            format!("{} {}", unit.kind, unit.name)
        } else {
            fs::read_to_string(&abs).unwrap_or_else(|_| format!("{} {}", unit.kind, unit.name))
        };
        conn.execute(
            "INSERT OR REPLACE INTO units (pack_id, kind, name, path, body) VALUES (?1, ?2, ?3, ?4, ?5)",
            params![id, unit.kind, unit.name, abs, body],
        )?;
        n += 1;
    }
    Ok(Some((id, n)))
}

fn walk_skill_units(dir: &Path) -> Vec<CatalogUnit> {
    let skills = dir.join("skills");
    if !skills.is_dir() {
        return Vec::new();
    }
    let mut out = Vec::new();
    for entry in WalkDir::new(&skills).max_depth(3).into_iter().flatten() {
        let path = entry.path();
        if !path.is_file() {
            continue;
        }
        let name = path.file_name().and_then(|s| s.to_str()).unwrap_or("");
        let rel = path.strip_prefix(dir).unwrap_or(path);
        let rel_s = rel.to_string_lossy().replace('\\', "/");
        if name.eq_ignore_ascii_case("SKILL.md") {
            let skill_name = path
                .parent()
                .and_then(|p| p.file_name())
                .and_then(|s| s.to_str())
                .unwrap_or("skill")
                .to_string();
            out.push(CatalogUnit {
                kind: "skill".into(),
                name: skill_name,
                path: rel_s,
            });
        } else if name.to_ascii_lowercase().ends_with(".md") {
            let skill_name = name.trim_end_matches(".md").trim_end_matches(".MD").to_string();
            out.push(CatalogUnit {
                kind: "skill".into(),
                name: skill_name,
                path: rel_s,
            });
        }
    }
    out
}

fn rebuild_terms(conn: &Connection) -> Result<()> {
    let units: Vec<(i64, String, String)> = {
        let mut stmt = conn.prepare("SELECT id, name, body FROM units")?;
        let rows = stmt.query_map([], |row| {
            Ok((
                row.get::<_, i64>(0)?,
                row.get::<_, String>(1)?,
                row.get::<_, String>(2)?,
            ))
        })?;
        rows.collect::<rusqlite::Result<Vec<_>>>()?
    };
    let mut insert = conn.prepare("INSERT OR IGNORE INTO terms (term, unit_id) VALUES (?1, ?2)")?;
    for (id, name, body) in units {
        let mut text = name;
        text.push(' ');
        text.push_str(&body);
        for term in tokenize(&text) {
            insert.execute(params![term, id])?;
        }
    }
    Ok(())
}

fn tokenize(text: &str) -> HashSet<String> {
    let mut out = HashSet::new();
    let mut cur = String::new();
    let flush = |cur: &mut String, out: &mut HashSet<String>| {
        if cur.len() >= 2 {
            out.insert(cur.clone());
        }
        cur.clear();
    };
    for c in text.chars() {
        if c.is_ascii_alphanumeric() {
            cur.push(c.to_ascii_lowercase());
        } else {
            flush(&mut cur, &mut out);
        }
    }
    flush(&mut cur, &mut out);
    out
}

fn cmd_search(conn: &Connection, query: &str) -> Result<Value> {
    let tokens: Vec<String> = tokenize(query).into_iter().collect();
    let mut hits = if tokens.is_empty() {
        Vec::new()
    } else {
        search_terms(conn, &tokens)?
    };
    if hits.is_empty() {
        hits = like_search(conn, query)?;
    }
    Ok(json!({ "ok": true, "hits": hits }))
}

fn search_terms(conn: &Connection, tokens: &[String]) -> Result<Vec<Value>> {
    let placeholders = tokens.iter().map(|_| "?").collect::<Vec<_>>().join(", ");
    let sql = format!(
        r#"
        SELECT u.pack_id, u.kind, u.name, u.path, substr(u.body, 1, 160)
        FROM units u
        WHERE (
          SELECT COUNT(DISTINCT t.term) FROM terms t
          WHERE t.unit_id = u.id AND t.term IN ({placeholders})
        ) = ?
        LIMIT 50
        "#,
        placeholders = placeholders,
    );
    let mut stmt = conn.prepare(&sql)?;
    let mut params: Vec<rusqlite::types::Value> = tokens
        .iter()
        .map(|t| rusqlite::types::Value::Text(t.clone()))
        .collect();
    params.push(rusqlite::types::Value::Integer(tokens.len() as i64));
    let mut rows = stmt.query(rusqlite::params_from_iter(params))?;
    let mut hits = Vec::new();
    while let Some(row) = rows.next()? {
        hits.push(hit_row(row)?);
    }
    Ok(hits)
}

fn like_search(conn: &Connection, query: &str) -> Result<Vec<Value>> {
    let like = format!("%{}%", query);
    let mut stmt = conn.prepare(
        r#"
        SELECT pack_id, kind, name, path, substr(body, 1, 160)
        FROM units
        WHERE body LIKE ?1 OR name LIKE ?1
        LIMIT 50
        "#,
    )?;
    let mut rows = stmt.query(params![like])?;
    let mut hits = Vec::new();
    while let Some(row) = rows.next()? {
        hits.push(hit_row(row)?);
    }
    Ok(hits)
}

fn hit_row(row: &rusqlite::Row<'_>) -> rusqlite::Result<Value> {
    Ok(json!({
        "pack_id": row.get::<_, String>(0)?,
        "kind": row.get::<_, String>(1)?,
        "name": row.get::<_, String>(2)?,
        "path": row.get::<_, String>(3)?,
        "snippet": row.get::<_, String>(4).unwrap_or_default(),
    }))
}

fn cmd_set_enabled(conn: &Connection, id: &str, enabled: bool) -> Result<Value> {
    let n = conn.execute(
        "UPDATE packs SET enabled = ?1 WHERE id = ?2",
        params![if enabled { 1 } else { 0 }, id],
    )?;
    if n == 0 {
        bail!("unknown pack id {id}");
    }
    Ok(json!({ "ok": true, "id": id, "enabled": enabled }))
}

fn cmd_list(conn: &Connection, enabled_only: bool) -> Result<Value> {
    let sql = if enabled_only {
        "SELECT id, name, version, description, dir, enabled FROM packs WHERE enabled = 1 ORDER BY name"
    } else {
        "SELECT id, name, version, description, dir, enabled FROM packs ORDER BY name"
    };
    let mut stmt = conn.prepare(sql)?;
    let mut packs: Vec<Value> = Vec::new();
    let mut rows = stmt.query([])?;
    while let Some(row) = rows.next()? {
        let enabled: i64 = row.get(5)?;
        packs.push(json!({
            "id": row.get::<_, String>(0)?,
            "name": row.get::<_, String>(1)?,
            "version": row.get::<_, String>(2)?,
            "description": row.get::<_, String>(3)?,
            "dir": row.get::<_, String>(4)?,
            "enabled": enabled != 0,
        }));
    }
    Ok(json!({ "ok": true, "packs": packs }))
}

fn cmd_snapshot(conn: &Connection) -> Result<Value> {
    let mut stmt = conn.prepare(
        r#"
        SELECT u.pack_id, u.kind, u.name, u.path, u.body
        FROM units u
        JOIN packs p ON p.id = u.pack_id
        WHERE p.enabled = 1 AND u.kind = 'skill'
        ORDER BY p.id, u.name
        "#,
    )?;
    let mut skills: Vec<Value> = Vec::new();
    let mut rows = stmt.query([])?;
    while let Some(row) = rows.next()? {
        let body: String = row.get(4)?;
        skills.push(json!({
            "pack_id": row.get::<_, String>(0)?,
            "kind": row.get::<_, String>(1)?,
            "name": row.get::<_, String>(2)?,
            "path": row.get::<_, String>(3)?,
            "description": description_from_body(&body),
        }));
    }
    Ok(json!({ "ok": true, "skills": skills }))
}

fn description_from_body(body: &str) -> String {
    for line in body.lines().take(40) {
        let t = line.trim();
        if let Some(rest) = t.strip_prefix("description:") {
            return rest.trim().trim_matches('"').to_string();
        }
    }
    String::new()
}

fn now_secs() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs() as i64)
        .unwrap_or(0)
}
