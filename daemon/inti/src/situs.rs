//! Fitur: daftar situs PHP dari folder proyek, dengan nama host .test-nya.
//!
//! Daftar periksa:
//! - Folder proyek kosong di profil = `<akar>\www`. Yang pertama jadi akar utama
//!   (http://localhost); folder lain dijangkau lewat nama situs masing-masing.
//! - Folder proyek yang hilang atau tak terbaca dilaporkan, bukan diam.
//! - Subfolder berawalan "." atau "_" bukan situs.
//! - Nama host hanya a-z, 0-9, "-". Huruf beraksen diturunkan (kafé -> kafe);
//!   dulu "kafé.test" masuk hosts apa adanya, padahal browser mengirim bentuk
//!   punycode - situsnya tidak pernah terbuka.
//! - Nama host unik di seluruh folder proyek: yang bentrok diberi angka (-2,
//!   -3) dan dilaporkan. Dua vhost bernama sama membuat Apache selalu melayani
//!   yang pertama, dan folder kedua seolah tidak pernah ada.
//! - DocumentRoot memakai public/, public_html/, web/, html/ HANYA bila di
//!   dalamnya ada index.php atau index.html; folder "public" berisi aset saja
//!   akan membuat situsnya 404 seluruhnya.

use crate::bin::gabung;
use crate::model::{Profile, Site};
use crate::paths;
use crate::protokol::Pesan;
use std::collections::HashSet;
use std::path::Path;
use unicode_normalization::char::is_combining_mark;
use unicode_normalization::UnicodeNormalization;

const PUBLIC_DIRS: &[&str] = &["public", "public_html", "web", "html"];

/// Akhiran nama situs profil, tanpa titik di depan; kosong = "test".
pub fn suffix(profile: Option<&Profile>) -> String {
    match profile.map(|p| p.site_suffix.trim()) {
        Some(s) if !s.is_empty() => s.trim_start_matches('.').to_string(),
        _ => "test".into(),
    }
}

/// Semua folder proyek profil; selalu minimal satu.
pub fn roots(profile: Option<&Profile>) -> Vec<String> {
    let mut v: Vec<String> = profile
        .map(|p| p.project_roots.iter().map(|r| r.trim().to_string()).filter(|r| !r.is_empty()).collect())
        .unwrap_or_default();
    if v.is_empty() {
        v.push(paths::www().to_string_lossy().into_owned());
    }
    v
}

/// Akar utama: yang dilayani http://localhost dan dibuka tombol "Buka www".
pub fn document_root(profile: Option<&Profile>) -> String {
    roots(profile).remove(0)
}

pub fn vhost_path(site: &Site) -> std::path::PathBuf {
    paths::sites_enabled().join(format!("auto.{}.conf", site.host_name))
}

/// Nama folder -> bagian nama host yang sah.
pub fn safe_host(folder: &str) -> String {
    let s: String = folder
        .nfd()
        .filter(|c| !is_combining_mark(*c))
        .flat_map(|c| c.to_lowercase())
        .map(|k| if k.is_ascii_lowercase() || k.is_ascii_digit() || k == '-' { k } else { '-' })
        .collect();
    let mut s = s.trim_matches('-').to_string();
    while s.contains("--") {
        s = s.replace("--", "-");
    }
    if s.is_empty() { "situs".into() } else { s }
}

/// Semua nama di berkas hosts (dikecilkan), dari teksnya. Baris komentar
/// dilewati; nama pertama tiap baris adalah alamat IP, bukan nama.
pub fn nama_hosts(teks: &str) -> HashSet<String> {
    let mut set = HashSet::new();
    for baris in teks.lines() {
        let t = baris.trim();
        if t.is_empty() || t.starts_with('#') {
            continue;
        }
        for n in t.split([' ', '\t']).filter(|x| !x.is_empty()).skip(1) {
            set.insert(n.to_lowercase());
        }
    }
    set
}

fn doc_root(dir: &str) -> String {
    for p in PUBLIC_DIRS {
        let calon = gabung(dir, p);
        let c = Path::new(&calon);
        if c.is_dir() && (c.join("index.php").is_file() || c.join("index.html").is_file()) {
            return calon;
        }
    }
    dir.to_string()
}

/// Pindai seluruh folder proyek profil. `nama_di_hosts` = semua nama yang ada
/// di berkas hosts (tidak peka besar-kecil, sudah dikecilkan).
pub fn scan(profile: Option<&Profile>, nama_di_hosts: &HashSet<String>, warnings: &mut Vec<Pesan>) -> Vec<Site> {
    let akhiran = suffix(profile);
    let mut list: Vec<Site> = Vec::new();

    for root in roots(profile) {
        if !Path::new(&root).is_dir() {
            warnings.push(Pesan::dengan("Folder proyek tidak ada: {0}", &[&root]));
            continue;
        }
        let mut dirs: Vec<String> = match std::fs::read_dir(&root) {
            Ok(rd) => rd
                .filter_map(|e| e.ok())
                .filter(|e| e.file_type().map(|t| t.is_dir()).unwrap_or(false))
                .map(|e| gabung(&root, &e.file_name().to_string_lossy()))
                .collect(),
            Err(e) => {
                warnings.push(Pesan::dengan("Folder proyek tidak terbaca ({0}): {1}", &[&root, &e.to_string()]));
                continue;
            }
        };
        dirs.sort_by_key(|d| d.to_uppercase());

        for dir in dirs {
            let name = dir.rsplit('\\').next().unwrap_or(&dir).to_string();
            if name.starts_with('.') || name.starts_with('_') {
                continue;
            }
            let dasar = safe_host(&name);
            let mut host = format!("{dasar}.{akhiran}");
            let dipakai = |h: &str, l: &[Site]| l.iter().position(|s| s.host_name.eq_ignore_ascii_case(h));
            if let Some(i) = dipakai(&host, &list) {
                let asli = host.clone();
                let mut n = 2;
                while dipakai(&format!("{dasar}-{n}.{akhiran}"), &list).is_some() {
                    n += 1;
                }
                host = format!("{dasar}-{n}.{akhiran}");
                warnings.push(Pesan::dengan(
                    "Nama {0} sudah dipakai {1}; folder {2} memakai {3} sebagai gantinya.",
                    &[&asli, &list[i].path, &dir, &host],
                ));
            }
            let mut site = Site {
                folder: name,
                path: dir.clone(),
                root: root.clone(),
                in_hosts: nama_di_hosts.contains(&host.to_lowercase()),
                doc_root: doc_root(&dir),
                host_name: host,
                has_vhost: false,
            };
            site.has_vhost = vhost_path(&site).is_file();
            list.push(site);
        }
    }
    list
}

#[cfg(test)]
mod uji {
    use super::*;
    use std::fs;

    #[test]
    fn nama_host_aman() {
        assert_eq!(safe_host("Kafé Lama"), "kafe-lama");
        assert_eq!(safe_host("toko_online"), "toko-online");
        assert_eq!(safe_host("--x--y--"), "x-y");
        assert_eq!(safe_host("日本"), "situs");
        assert_eq!(safe_host("blog"), "blog");
    }

    #[test]
    fn situs_bentrok_diberi_angka_dan_public_hanya_bila_ada_index() {
        let (_g, d) = paths::sandbox_uji("situs");
        let www = d.join("proyek");
        for f in [r"Kafé", r"kafe", r"_draf", r".git", r"laravel\public", r"aset\public"] {
            fs::create_dir_all(www.join(f)).unwrap();
        }
        fs::write(www.join(r"laravel\public\index.php"), "").unwrap();
        let p = Profile { project_roots: vec![www.to_string_lossy().into(), r"Z:\tidak-ada".into()], ..Profile::default() };
        let mut hosts = HashSet::new();
        hosts.insert("laravel.test".to_string());
        let mut w = Vec::new();
        let s = scan(Some(&p), &hosts, &mut w);
        let nama: Vec<&str> = s.iter().map(|x| x.host_name.as_str()).collect();
        assert_eq!(nama, vec!["aset.test", "kafe.test", "kafe-2.test", "laravel.test"]);
        assert!(s[3].in_hosts && s[3].doc_root.ends_with(r"laravel\public"));
        assert!(s[0].doc_root.ends_with("aset"));
        assert_eq!(w.len(), 2); // bentrok + folder tidak ada
        let _ = fs::remove_dir_all(&d);
    }

    #[test]
    fn akhiran_dan_akar_bawaan() {
        let (_g, d) = paths::sandbox_uji("situs-akar");
        let p = Profile { site_suffix: " .local ".into(), ..Profile::default() };
        assert_eq!(suffix(Some(&p)), "local");
        assert_eq!(suffix(None), "test");
        assert_eq!(document_root(Some(&p)), d.join("www").to_string_lossy());
        let _ = fs::remove_dir_all(&d);
    }
}
