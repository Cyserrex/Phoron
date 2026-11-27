//! Core Phoron dalam Rust - port bertahap dari `src/Phoron.Core` (C#).
//!
//! Aturan utamanya: berkas yang dibaca dan ditulis di sini HARUS identik
//! dengan buatan versi C#, supaya orang bisa berpindah bolak-balik antara
//! keduanya tanpa kehilangan setelan. Lihat `daemon/RENCANA.md`.
//!
//! Sejak tahap B, modul ditulis per fitur (lihat RENCANA.md): yang wajib
//! sama dengan C# adalah format berkas dan hasil yang dilihat pengguna.

pub mod atomic_file;
pub mod beranda;
pub mod bin;
pub mod config;
pub mod deteksi;
pub mod engine;
pub mod hosts;
pub mod hsts;
pub mod ini;
pub mod log_file;
pub mod model;
pub mod node_apps;
pub mod node_runner;
pub mod oracle;
pub mod paths;
pub mod pola;
pub mod port;
pub mod process_job;
pub mod profile_store;
pub mod protokol;
pub mod rahasia;
pub mod services;
pub mod settings;
pub mod shell;
pub mod situs;
pub mod ssl;
pub mod teks;
pub mod tulis;
pub mod waktu;

/// Nomor versi aplikasi. Mengikuti `AppInfo.Version` di C#; selama peralihan
/// daemon melaporkannya lewat protokol supaya UI bisa menolak daemon yang
/// tidak sepadan.
pub const VERSI: &str = "1.41.3";
