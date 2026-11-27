//! Core Phoron dalam Rust - port bertahap dari `src/Phoron.Core` (C#).
//!
//! Aturan utamanya: berkas yang dibaca dan ditulis di sini HARUS identik
//! dengan buatan versi C#, supaya orang bisa berpindah bolak-balik antara
//! keduanya tanpa kehilangan setelan. Lihat `daemon/RENCANA.md`.
//!
//! Nama modul dan fungsi mengikuti padanan C#-nya supaya keduanya mudah
//! dibandingkan baris demi baris selama masa peralihan.

pub mod atomic_file;
pub mod bin;
pub mod deteksi;
pub mod ini;
pub mod model;
pub mod node_apps;
pub mod paths;
pub mod pola;
pub mod port;
pub mod profile_store;
pub mod protokol;
pub mod rahasia;
pub mod settings;
pub mod shell;
pub mod situs;
pub mod teks;
pub mod waktu;

/// Nomor versi aplikasi. Mengikuti `AppInfo.Version` di C#; selama peralihan
/// daemon melaporkannya lewat protokol supaya UI bisa menolak daemon yang
/// tidak sepadan.
pub const VERSI: &str = "1.41.3";
