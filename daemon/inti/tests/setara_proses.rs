//! Uji setara fitur "proses anak dan log": log berputar dan pengenalan
//! keluaran proyek Node harus sama dengan Core C#.
//!
//! Bahannya dibangkitkan `Phoron.Tests.exe setara-modul proses <folder>`
//! (lihat `src/Phoron.Tests/UjiSetaraProses.cs`); folder itu diberikan lewat
//! `PHORON_SETARA_PROSES`. Tanpa variabel itu uji ini dilewati - jalankan
//! lewat `daemon\uji_setara_modul.ps1 proses`.

use phoron_inti::{log_file, node_runner};
use std::fs;
use std::path::{Path, PathBuf};

fn isi_folder(d: &Path) -> Vec<(String, Vec<u8>)> {
    let mut v: Vec<(String, Vec<u8>)> = fs::read_dir(d)
        .unwrap()
        .map(|e| e.unwrap())
        .map(|e| (e.file_name().to_string_lossy().into_owned(), fs::read(e.path()).unwrap()))
        .collect();
    v.sort();
    v
}

fn ringkas(b: &[u8]) -> String {
    let t = String::from_utf8_lossy(b);
    if t.len() <= 200 {
        format!("{t:?}")
    } else {
        format!("{} byte, awal {:?}, akhir {:?}", b.len(), &t[..60], &t[t.len() - 60..])
    }
}

fn urai(s: &str) -> String {
    let mut h = String::new();
    let mut cs = s.chars();
    while let Some(c) = cs.next() {
        if c == '\\' {
            match cs.next() {
                Some('n') => h.push('\n'),
                Some('r') => h.push('\r'),
                Some('t') => h.push('\t'),
                Some(x) => h.push(x),
                None => h.push('\\'),
            }
        } else {
            h.push(c);
        }
    }
    h
}

fn log(akar: &Path, gagal: &mut Vec<String>) {
    let dasar = akar.join("log");
    let kerja = std::env::temp_dir().join(format!("phoron-setara-proses-rust-{}", std::process::id()));
    let _ = fs::remove_dir_all(&kerja);
    for e in fs::read_dir(dasar.join("masuk")).unwrap() {
        let e = e.unwrap();
        let tuju = kerja.join(e.file_name());
        fs::create_dir_all(&tuju).unwrap();
        for f in fs::read_dir(e.path()).unwrap() {
            let f = f.unwrap();
            fs::copy(f.path(), tuju.join(f.file_name())).unwrap();
        }
    }
    let ops = fs::read_to_string(dasar.join("log-ops.txt")).unwrap();
    for baris in ops.lines().filter(|b| !b.is_empty()) {
        let b: Vec<&str> = baris.splitn(4, '\t').collect();
        let path: PathBuf = kerja.join(b[0]).join(b[1]);
        match b[2] {
            "t" => log_file::tambah(&path, &urai(b[3])),
            "u" => {
                let (c, n) = b[3].split_once(':').unwrap();
                log_file::tambah(&path, &c.repeat(n.parse().unwrap()));
            }
            "p" => log_file::putar(&path),
            x => panic!("operasi tak dikenal {x}"),
        }
    }
    let mut skenario: Vec<_> = fs::read_dir(dasar.join("csharp")).unwrap().map(|e| e.unwrap().file_name()).collect();
    skenario.sort();
    for s in skenario {
        let cs = isi_folder(&dasar.join("csharp").join(&s));
        let rs = isi_folder(&kerja.join(&s));
        let nama = |v: &[(String, Vec<u8>)]| v.iter().map(|x| x.0.clone()).collect::<Vec<_>>();
        if nama(&cs) != nama(&rs) {
            gagal.push(format!("log {s:?}: berkas C# {:?} vs Rust {:?}", nama(&cs), nama(&rs)));
            continue;
        }
        for ((n, a), (_, b)) in cs.iter().zip(&rs) {
            if a != b {
                gagal.push(format!("log {s:?}/{n}:\n  C#:   {}\n  Rust: {}", ringkas(a), ringkas(b)));
            }
        }
    }
    let _ = fs::remove_dir_all(&kerja);
}

fn keluaran(akar: &Path, gagal: &mut Vec<String>) {
    let d = akar.join("node");
    let contoh = fs::read_to_string(d.join("keluaran.txt")).unwrap();
    let cs = fs::read_to_string(d.join("keluaran-csharp.txt")).unwrap();
    let contoh: Vec<&str> = contoh.split('\n').collect();
    let cs: Vec<&str> = cs.split('\n').collect();
    assert_eq!(contoh.len(), cs.len(), "jumlah baris contoh dan hasil C#");
    for (b, h) in contoh.iter().zip(&cs) {
        let rust = format!(
            "{}\t{}",
            node_runner::port_dari_keluhan(b),
            node_runner::url_dari_baris(b).unwrap_or_default()
        );
        if &rust != h {
            gagal.push(format!("keluaran {b:?}:\n  C#:   {h:?}\n  Rust: {rust:?}"));
        }
    }
    for baris in fs::read_to_string(d.join("kerangka-csharp.txt")).unwrap().split('\n') {
        let (k, v) = baris.split_once('\t').unwrap();
        let rust = if node_runner::butuh_argumen_port(k) { "1" } else { "0" };
        if rust != v {
            gagal.push(format!("butuh_argumen_port({k:?}): C# {v}, Rust {rust}"));
        }
    }
}

#[test]
fn setara_proses() {
    let Ok(akar) = std::env::var("PHORON_SETARA_PROSES") else { return };
    let akar = PathBuf::from(akar);
    let mut gagal = Vec::new();
    log(&akar, &mut gagal);
    keluaran(&akar, &mut gagal);
    assert!(gagal.is_empty(), "{} beda:\n{}", gagal.len(), gagal.join("\n"));
}
