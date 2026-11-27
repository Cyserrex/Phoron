//! Fitur: proyek Node/TypeScript yang didaftarkan (Next.js, Astro, Vite, ...).
//!
//! Daftar periksa:
//! - Tersimpan di `<akar>\apps.ini`, satu seksi per folder proyek - format
//!   ini milik pengguna dan harus sama dengan versi C#.
//! - Port 0 = biarkan server pengembangannya memilih; ditulis kosong.
//! - Skrip dibaca dari "scripts" di package.json tanpa pengurai JSON penuh:
//!   nilai skrip bisa memuat { } dan kutip ("astro check && astro build"),
//!   jadi kurung pasangannya dicari sambil melewati isi string.
//! - Pengelola paket dari berkas kunci: pnpm, yarn, bun, selain itu npm.

use crate::ini::Ini;
use crate::paths;
use crate::pola;
use serde::{Deserialize, Serialize};
use std::path::Path;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct NodeApp {
    /// Folder proyek - jadi kunci; boleh di mana saja.
    pub path: String,
    pub name: String,
    /// Nama skrip di package.json, mis. "dev".
    pub script: String,
    /// npm | pnpm | yarn | bun
    pub manager: String,
    /// Folder paket Node yang dipakai; kosong = ikut PATH sistem.
    pub node_id: String,
    /// Port tetap; 0 = biarkan server pengembangannya memilih.
    pub port: i32,
}

impl Default for NodeApp {
    fn default() -> Self {
        NodeApp {
            path: String::new(),
            name: String::new(),
            script: "dev".into(),
            manager: "npm".into(),
            node_id: String::new(),
            port: 0,
        }
    }
}

impl NodeApp {
    pub fn display_name(&self) -> String {
        if !self.name.trim().is_empty() {
            return self.name.clone();
        }
        if self.path.is_empty() {
            return "(tanpa nama)".into();
        }
        let t = self.path.trim_end_matches('\\');
        t.rsplit('\\').next().unwrap_or(t).to_string()
    }
}

const KEPALA: &str =
    "Proyek Node/TypeScript yang terdaftar di Phoron.\nNama seksi = folder proyeknya. Boleh disunting tangan.";

fn berkas() -> std::path::PathBuf {
    paths::root().join("apps.ini")
}

pub fn load_all() -> Vec<NodeApp> {
    let Ok(ini) = Ini::load(&berkas()) else { return Vec::new() };
    ini.sections()
        .filter(|s| !s.trim().is_empty())
        .map(|s| NodeApp {
            path: s.to_string(),
            name: ini.get_or(s, "nama", "").into(),
            script: ini.get_or(s, "skrip", "dev").into(),
            manager: ini.get_or(s, "manager", "npm").into(),
            node_id: ini.get_or(s, "node", "").into(),
            port: ini.get_int(s, "port", 0).max(0),
        })
        .collect()
}

pub fn save_all(apps: &[NodeApp]) -> std::io::Result<()> {
    let mut ini = Ini::baru();
    for a in apps.iter().filter(|a| !a.path.trim().is_empty()) {
        let s = a.path.trim_end_matches('\\');
        ini.set(s, "nama", &a.name);
        ini.set(s, "skrip", &a.script);
        ini.set(s, "manager", &a.manager);
        ini.set(s, "node", &a.node_id);
        ini.set(s, "port", &if a.port > 0 { a.port.to_string() } else { String::new() });
    }
    ini.save(&berkas(), Some(KEPALA))
}

fn baca_package_json(folder: &str) -> Option<String> {
    let f = Path::new(folder).join("package.json");
    std::fs::read(f).ok().map(|b| crate::ini::dekode(&b))
}

/// Nama skrip di package.json, menurut urutan tulisnya.
pub fn scripts(folder: &str) -> Vec<String> {
    let Some(teks) = baca_package_json(folder) else { return Vec::new() };
    let Some(mulai) = teks.find("\"scripts\"") else { return Vec::new() };
    let Some(rel) = teks[mulai..].find('{') else { return Vec::new() };
    let buka = mulai + rel;

    // Kurung tutup pasangannya, melewati isi string.
    let b = teks.as_bytes();
    let (mut dalam, mut di_string, mut i) = (0i32, false, buka);
    let mut tutup = None;
    while i < b.len() {
        let c = b[i];
        if di_string {
            if c == b'\\' {
                i += 2;
                continue;
            }
            if c == b'"' {
                di_string = false;
            }
        } else if c == b'"' {
            di_string = true;
        } else if c == b'{' {
            dalam += 1;
        } else if c == b'}' {
            dalam -= 1;
            if dalam == 0 {
                tutup = Some(i);
                break;
            }
        }
        i += 1;
    }
    let Some(t) = tutup else { return Vec::new() };
    let mut hasil: Vec<String> = Vec::new();
    for k in pola::kunci_json(&teks[buka + 1..t]) {
        if !k.is_empty() && !hasil.contains(&k) {
            hasil.push(k);
        }
    }
    hasil
}

pub fn detect_manager(folder: &str) -> &'static str {
    if folder.is_empty() {
        return "npm";
    }
    let ada = |f: &str| Path::new(folder).join(f).is_file();
    if ada("pnpm-lock.yaml") {
        "pnpm"
    } else if ada("yarn.lock") {
        "yarn"
    } else if ada("bun.lockb") || ada("bun.lock") {
        "bun"
    } else {
        "npm"
    }
}

/// Kerangka yang terdeteksi, sekadar keterangan di layar.
pub fn detect_framework(folder: &str) -> &'static str {
    let Some(teks) = baca_package_json(folder) else { return "" };
    const KERANGKA: &[&str] =
        &["next", "astro", "nuxt", "vite", "@sveltejs/kit", "@angular/core", "react-scripts", "express", "nest"];
    let kunci = pola::kunci_json(&teks);
    KERANGKA.iter().copied().find(|k| kunci.iter().any(|x| x == k)).unwrap_or("")
}

pub fn looks_like_node_project(folder: &str) -> bool {
    !folder.is_empty() && Path::new(folder).join("package.json").is_file()
}

pub fn has_node_modules(folder: &str) -> bool {
    !folder.is_empty() && Path::new(folder).join("node_modules").is_dir()
}

#[cfg(test)]
mod uji {
    use super::*;
    use std::fs;

    #[test]
    fn simpan_dan_muat_apps_ini() {
        let (_g, d) = paths::sandbox_uji("apps");
        let a = vec![
            NodeApp { path: r"D:\web\toko\".into(), port: 3001, ..NodeApp::default() },
            NodeApp { path: r"D:\web\blog".into(), name: "Blog".into(), manager: "pnpm".into(), ..NodeApp::default() },
        ];
        save_all(&a).unwrap();
        let isi = fs::read_to_string(d.join("apps.ini")).unwrap();
        assert!(isi.contains("[D:\\web\\toko]\r\nnama=\r\nskrip=dev\r\nmanager=npm\r\nnode=\r\nport=3001\r\n"));
        assert!(isi.contains("port=\r\n"));
        let b = load_all();
        assert_eq!(b[0].path, r"D:\web\toko");
        assert_eq!(b[0].port, 3001);
        assert_eq!(b[1].display_name(), "Blog");
        assert_eq!(b[0].display_name(), "toko");
        let _ = fs::remove_dir_all(&d);
    }

    #[test]
    fn skrip_package_json_dan_kerangka() {
        let (_g, d) = paths::sandbox_uji("pkg");
        fs::write(
            d.join("package.json"),
            r#"{ "name": "x", "scripts": { "dev": "next dev", "build": "echo { } && next build", "dev": "lagi" },
               "dependencies": { "next": "15.0.0", "react": "19" } }"#,
        )
        .unwrap();
        fs::write(d.join("pnpm-lock.yaml"), "").unwrap();
        let f = d.to_string_lossy();
        assert_eq!(scripts(&f), vec!["dev", "build"]);
        assert_eq!(detect_framework(&f), "next");
        assert_eq!(detect_manager(&f), "pnpm");
        assert!(looks_like_node_project(&f));
        assert!(!has_node_modules(&f));
        let _ = fs::remove_dir_all(&d);
    }
}
