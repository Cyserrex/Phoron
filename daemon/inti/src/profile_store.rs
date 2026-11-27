//! Padanan `ProfileStore.cs`: baca/tulis profil di folder profiles\.
//!
//! `Seed` (profil awal per versi PHP) menyusul di tahap C, karena butuh
//! `ConfigWriter.EkstensiDisarankan`.

use crate::ini::Ini;
use crate::model::{kamus_set, BinPackage, Profile};
use crate::paths;
use crate::teks::{huruf_atau_angka, sama_abaikan_besar, urut_budaya};
use std::io;
use std::path::{Path, PathBuf};

const KEPALA: &str =
    "Profil Phoron - boleh disunting tangan.\nNama folder versi harus persis seperti di folder bin.";

/// Berkas profil di folder profiles\, diurutkan seperti `OrderBy(x => x)`.
///
/// Pola `*.ini` di Windows juga cocok dengan ekstensi yang DIAWALI "ini"
/// (".inix") - perilaku warisan 8.3 yang ikut ditiru, supaya kedua versi
/// melihat daftar berkas yang sama.
fn berkas_profil() -> Vec<PathBuf> {
    let mut v: Vec<PathBuf> = std::fs::read_dir(paths::profiles())
        .map(|rd| {
            rd.filter_map(|e| e.ok())
                .map(|e| e.path())
                .filter(|p| p.is_file())
                .filter(|p| {
                    p.extension()
                        .map(|e| e.to_string_lossy().to_lowercase())
                        .is_some_and(|e| e.starts_with("ini"))
                })
                .collect()
        })
        .unwrap_or_default();
    v.sort_by(|a, b| urut_budaya(&a.to_string_lossy(), &b.to_string_lossy()));
    v
}

/// Profil yang rusak dilewati - satu berkas cacat bukan alasan menggagalkan
/// seluruh start - tapi tidak DIAM: kalau yang rusak justru profil aktif,
/// orang harus tahu kenapa versi yang jalan berbeda dari yang dikiranya.
pub fn load_all(mut keluhan: Option<&mut Vec<String>>) -> Vec<Profile> {
    let mut list = Vec::new();
    for f in berkas_profil() {
        let nama = f.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
        // Dibaca SEKALI: isi dan keadaannya dari satu pembacaan.
        let periksa = Ini::baca(&f);
        if periksa.gagal_baca {
            if let Some(k) = keluhan.as_deref_mut() {
                k.push(format!(
                    "Profil {} tidak terbaca ({}), jadi dilewati.",
                    nama,
                    periksa.sebab.as_deref().unwrap_or("sebab tidak diketahui")
                ));
            }
            continue;
        }
        list.push(dari(&periksa.isi, &f));
        if periksa.rusak {
            if let Some(k) = keluhan.as_deref_mut() {
                k.push(format!(
                    "Profil {} ada yang tidak terbaca: {}",
                    nama,
                    periksa.keluhan.join("; ")
                ));
            }
        }
    }
    list
}

pub fn load(path: &Path) -> io::Result<Profile> {
    Ok(dari(&Ini::load(path)?, path))
}

fn batang(path: &Path) -> String {
    path.file_stem().map(|s| s.to_string_lossy().into_owned()).unwrap_or_default()
}

/// Bentuk profil dari isi ini yang SUDAH dibaca.
fn dari(ini: &Ini, path: &Path) -> Profile {
    let berkas = batang(path);
    let mut p = Profile {
        file_name: berkas.clone(),
        name: ini.get_or("profil", "nama", &berkas).to_string(),
        php_id: ini.get_or("profil", "php", "").into(),
        apache_id: ini.get_or("profil", "apache", "").into(),
        nginx_id: ini.get_or("profil", "nginx", "").into(),
        mysql_id: ini.get_or("profil", "mysql", "").into(),
        web_server: ini.get_or("profil", "web_server", "apache").into(),
        http_port: ini.get_int("profil", "port_http", 80),
        https_port: ini.get_int("profil", "port_https", 443),
        mysql_port: ini.get_int("profil", "port_mysql", 3306),
        site_suffix: ini.get_or("profil", "akhiran_situs", "test").into(),
        notes: ini.get_or("profil", "catatan", "").into(),
        ..Profile::default()
    };
    // folder_proyek menggantikan document_root; berkas lama tetap dibaca lewat
    // kunci lamanya, supaya proyek orang tidak hilang dari daftar.
    let roots = ini
        .get("profil", "folder_proyek")
        .or_else(|| ini.get("profil", "document_root"))
        .unwrap_or("");
    p.project_roots = split_roots(roots);

    let mut ext: Vec<String> = Vec::new();
    for x in ini.get_or("php", "ekstensi", "").split(',') {
        let x = x.trim().to_lowercase();
        if !x.is_empty() && !ext.contains(&x) {
            ext.push(x);
        }
    }
    p.php_extensions = ext;

    for (k, v) in ini.items("php.ini") {
        kamus_set(&mut p.php_ini_overrides, k, v);
    }
    // Nilai kosong berarti "ikut profil" dan tidak disimpan sebagai entri.
    for (k, v) in ini.items("php_situs") {
        if !k.trim().is_empty() && !v.trim().is_empty() {
            kamus_set(&mut p.php_per_situs, k.trim(), v.trim());
        }
    }
    p
}

/// Isi INI sebuah profil, persis urutan `ProfileStore.Save` C#.
pub fn ke_ini(p: &Profile) -> Ini {
    let mut ini = Ini::baru();
    ini.set("profil", "nama", &p.name);
    ini.set("profil", "web_server", &p.web_server);
    ini.set("profil", "php", &p.php_id);
    ini.set("profil", "apache", &p.apache_id);
    ini.set("profil", "nginx", &p.nginx_id);
    ini.set("profil", "mysql", &p.mysql_id);
    ini.set("profil", "port_http", &p.http_port.to_string());
    ini.set("profil", "port_https", &p.https_port.to_string());
    ini.set("profil", "port_mysql", &p.mysql_port.to_string());
    ini.set("profil", "folder_proyek", &p.project_roots.join(";"));
    ini.set("profil", "akhiran_situs", &p.site_suffix);
    ini.set("profil", "catatan", &p.notes.replace(['\r', '\n'], " "));
    ini.set("php", "ekstensi", &p.php_extensions.join(","));
    for (k, v) in &p.php_ini_overrides {
        ini.set("php.ini", k, v);
    }
    for (k, v) in &p.php_per_situs {
        if !v.trim().is_empty() {
            ini.set("php_situs", k, v);
        }
    }
    ini
}

pub fn save(p: &mut Profile) -> io::Result<()> {
    if p.file_name.trim().is_empty() {
        p.file_name = slug(&p.name);
    }
    let path = paths::profiles().join(format!("{}.ini", p.file_name));
    ke_ini(p).save(&path, Some(KEPALA))
}

pub fn delete(p: &Profile) -> io::Result<()> {
    if p.file_name.is_empty() {
        return Ok(());
    }
    let path = paths::profiles().join(format!("{}.ini", p.file_name));
    if path.exists() {
        std::fs::remove_file(path)?;
    }
    Ok(())
}

/// Pemisahnya titik koma, sama seperti bin_roots. Jalur Windows tidak pernah
/// memuat titik koma, jadi tidak ada yang perlu di-escape.
pub fn split_roots(value: &str) -> Vec<String> {
    let mut v: Vec<String> = Vec::new();
    for x in value.split(';') {
        let x = x.trim().trim_end_matches('\\');
        if !x.is_empty() && !v.iter().any(|y| sama_abaikan_besar(y, x)) {
            v.push(x.to_string());
        }
    }
    v
}

pub fn slug(name: &str) -> String {
    let s: String = name
        .chars()
        .map(|c| if huruf_atau_angka(c) { c.to_lowercase().next().unwrap_or(c) } else { '-' })
        .collect();
    let mut s = s.trim_matches('-').to_string();
    while s.contains("--") {
        s = s.replace("--", "-");
    }
    if s.is_empty() { "profil".into() } else { s }
}

/// Nama berkas yang belum dipakai, supaya profil baru tidak menimpa yang lama.
pub fn unique_file_name(name: &str) -> String {
    let dasar = slug(name);
    let mut calon = dasar.clone();
    let mut n = 2;
    while paths::profiles().join(format!("{calon}.ini")).exists() {
        calon = format!("{dasar}-{n}");
        n += 1;
    }
    calon
}

/// Arsitektur yang TIDAK diketahui dianggap sepadan.
pub fn arsitektur_sepadan(a: &str, b: &str) -> bool {
    a.is_empty() || b.is_empty() || sama_abaikan_besar(a, b)
}

/// MySQL untuk sebuah profil: versi tertinggi. Arsitektur bukan syarat -
/// mysqld proses terpisah, tidak dimuat ke dalam PHP.
pub fn pick_mysql<'a>(mysqls: impl IntoIterator<Item = &'a BinPackage>) -> Option<&'a BinPackage> {
    tertinggi(mysqls)
}

/// `OrderByDescending(Parsed).FirstOrDefault()`: urutan stabil, jadi saat
/// versinya seri yang PERTAMA dalam daftar yang menang.
fn tertinggi<'a>(it: impl IntoIterator<Item = &'a BinPackage>) -> Option<&'a BinPackage> {
    let mut best: Option<&BinPackage> = None;
    for p in it {
        if best.is_none_or(|b| p.parsed() > b.parsed()) {
            best = Some(p);
        }
    }
    best
}

/// Apache untuk sebuah PHP. Arsitektur syarat MUTLAK (Apache x64 mustahil
/// memuat modul PHP x86, dan galatnya "%1 is not a valid Win32 application"
/// tidak menyebut sebabnya); toolset hanya preferensi.
pub fn pick_apache<'a>(php: Option<&BinPackage>, apaches: &'a [BinPackage]) -> Option<&'a BinPackage> {
    let list: Vec<&BinPackage> = apaches.iter().collect();
    let Some(php) = php else { return tertinggi(list) };
    if list.is_empty() {
        return None;
    }
    let se_arsitektur: Vec<&BinPackage> =
        list.iter().copied().filter(|a| arsitektur_sepadan(&a.arch, &php.arch)).collect();
    let kandidat = if se_arsitektur.is_empty() { list } else { se_arsitektur };
    let se_toolset = tertinggi(
        kandidat
            .iter()
            .copied()
            .filter(|a| !a.compiler.is_empty() && sama_abaikan_besar(&a.compiler, &php.compiler)),
    );
    se_toolset.or_else(|| tertinggi(kandidat.iter().copied()))
}

#[cfg(test)]
mod uji {
    use super::*;
    use crate::model::BinKind;
    use std::fs;

    #[test]
    fn simpan_lalu_muat_sama() {
        let (_g, d) = paths::sandbox_uji("profil");
        let mut p = Profile {
            name: "PHP 8.3 + Apache".into(),
            php_id: "php-8.3.12".into(),
            project_roots: vec![r"D:\proyek".into(), r"E:\lain".into()],
            php_extensions: vec!["mbstring".into(), "mysqli".into()],
            notes: "baris\r\nkedua".into(),
            ..Profile::default()
        };
        kamus_set(&mut p.php_ini_overrides, "memory_limit", "512M");
        kamus_set(&mut p.php_per_situs, r"D:\proyek\lama", "php-5.6.40");
        save(&mut p).unwrap();
        assert_eq!(p.file_name, "php-8-3-apache");

        let semua = load_all(None);
        assert_eq!(semua.len(), 1);
        let q = &semua[0];
        assert_eq!(q.name, p.name);
        assert_eq!(q.project_roots, p.project_roots);
        assert_eq!(q.php_ini_overrides, p.php_ini_overrides);
        assert_eq!(q.php_per_situs, p.php_per_situs);
        assert_eq!(q.notes, "baris  kedua");
        let _ = fs::remove_dir_all(&d);
    }

    #[test]
    fn kunci_lama_document_root_masih_dibaca() {
        let (_g, d) = paths::sandbox_uji("profil-lama");
        fs::write(
            paths::profiles().join("lama.ini"),
            "[profil]\r\ndocument_root=D:\\www\\;D:\\WWW\r\n[php]\r\nekstensi= MbString , ,mbstring\r\n",
        )
        .unwrap();
        let p = &load_all(None)[0];
        assert_eq!(p.name, "lama");
        assert_eq!(p.project_roots, vec![r"D:\www".to_string()]);
        assert_eq!(p.php_extensions, vec!["mbstring".to_string()]);
        let _ = fs::remove_dir_all(&d);
    }

    #[test]
    fn profil_rusak_dikeluhkan_bukan_diam() {
        let (_g, d) = paths::sandbox_uji("profil-rusak");
        fs::write(paths::profiles().join("a.ini"), "[profil]\r\nnama=A\r\nngawur\r\n").unwrap();
        let mut k = Vec::new();
        let semua = load_all(Some(&mut k));
        assert_eq!(semua.len(), 1);
        assert_eq!(k.len(), 1);
        let _ = fs::remove_dir_all(&d);
    }

    #[test]
    fn slug_dan_nama_unik() {
        assert_eq!(slug("PHP 8.3 + Apache 2.4"), "php-8-3-apache-2-4");
        assert_eq!(slug("  "), "profil");
        assert_eq!(slug("Kopi Pagi!"), "kopi-pagi");
        let (_g, d) = paths::sandbox_uji("profil-unik");
        fs::write(paths::profiles().join("a.ini"), "").unwrap();
        assert_eq!(unique_file_name("A"), "a-2");
        let _ = fs::remove_dir_all(&d);
    }

    #[test]
    fn apache_dipilih_menurut_arsitektur_lalu_toolset() {
        let paket = |id: &str, v: &str, c: &str, a: &str| {
            let mut p = BinPackage::baru(BinKind::Apache, id, id, v);
            p.compiler = c.into();
            p.arch = a.into();
            p
        };
        let apaches = vec![
            paket("a-x86-vc15", "2.4.54", "VC15", "x86"),
            paket("a-x64-vs16", "2.4.62", "VS16", "x64"),
            paket("a-x64-vc15", "2.4.50", "VC15", "x64"),
        ];
        let mut php = BinPackage::baru(BinKind::Php, "php", "php", "7.4.33");
        php.compiler = "vc15".into();
        php.arch = "x64".into();
        assert_eq!(pick_apache(Some(&php), &apaches).unwrap().id, "a-x64-vc15");
        php.compiler = "vs17".into();
        assert_eq!(pick_apache(Some(&php), &apaches).unwrap().id, "a-x64-vs16");
        assert_eq!(pick_apache(None, &apaches).unwrap().id, "a-x64-vs16");
    }
}
