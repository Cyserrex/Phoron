//! Padanan `Deteksi.cs`: pengelola lain (Laragon, XAMPP, WAMP, MAMP) yang
//! benar-benar terpasang di komputer ini, supaya Phoron langsung punya
//! daftar versi tanpa disuruh.

use crate::teks::sama_abaikan_besar;
use std::path::Path;

const POLA_JALUR: &[&str] = &[
    r"{}:\laragon\bin",
    r"{}:\xampp",
    r"{}:\wamp64\bin",
    r"{}:\wamp\bin",
    r"{}:\MAMP\bin",
];

const DRIVE: &[&str] = &["C", "D", "E", "F"];

pub fn folder_bin_terpasang() -> Vec<String> {
    let mut hasil: Vec<String> = Vec::new();
    for d in DRIVE {
        for pola in POLA_JALUR {
            let p = pola.replace("{}", d);
            if Path::new(&p).is_dir() && !hasil.iter().any(|x| sama_abaikan_besar(x, &p)) {
                hasil.push(p);
            }
        }
    }
    hasil
}

/// Folder terpasang yang belum ada di daftar `terdaftar`.
pub fn belum_terdaftar(terdaftar: &[String]) -> Vec<String> {
    folder_bin_terpasang()
        .into_iter()
        .filter(|p| {
            let p = p.trim_end_matches('\\');
            !terdaftar.iter().any(|t| sama_abaikan_besar(t.trim_end_matches('\\'), p))
        })
        .collect()
}
