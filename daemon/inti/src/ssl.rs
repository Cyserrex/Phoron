//! Fitur dari SslTool.cs - CA lokal dan sertifikat situs lewat openssl.exe milik Apache.
//!
//! Jalur berkasnya sudah tetap (dipakai config.rs untuk ssl.conf); pembuatan
//! sertifikatnya dikerjakan di tahap C (lihat daemon/RENCANA.md).

use crate::paths;
use std::path::PathBuf;

pub fn crt_path() -> PathBuf {
    paths::etc_ssl().join("phoron.crt")
}

pub fn key_path() -> PathBuf {
    paths::etc_ssl().join("phoron.key")
}

pub fn ca_crt_path() -> PathBuf {
    paths::etc_ssl().join("phoron-ca.crt")
}

pub fn ca_key_path() -> PathBuf {
    paths::etc_ssl().join("phoron-ca.key")
}

/// CA ikut disyaratkan: instalasi lama hanya punya phoron.crt model lama, dan
/// dengan begini pasangan baru dibuat pada penerapan berikutnya alih-alih terus
/// menyajikan sertifikat yang ditolak Firefox.
pub fn exists() -> bool {
    crt_path().is_file() && key_path().is_file() && ca_crt_path().is_file()
}
