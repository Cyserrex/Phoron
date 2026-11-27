//! Padanan `Paths.cs`: semua lokasi folder Phoron dihitung dari satu akar.
//!
//! Akar itu BUKAN folder exe: saat pengembangan exe ada jauh di bawah folder
//! build, sedangkan www/etc/data tetap di akar instalasi. Menaiki folder induk
//! sampai ketemu penanda membuat exe hasil build dan exe hasil rilis melihat
//! data yang sama.

use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::{Mutex, RwLock};

static AKAR: RwLock<Option<PathBuf>> = RwLock::new(None);
static HOSTS: RwLock<Option<PathBuf>> = RwLock::new(None);

/// Folder yang sudah dipastikan ada, diikat pada akarnya.
///
/// Disinggahkan karena dibaca sangat sering (tiap baris log menyentuh folder
/// logs); tanpa singgahan, sekadar MEMBACA sebuah jalur berarti satu
/// panggilan create_dir yang menemui cakram. Diikat pada akar sebab akar bisa
/// diganti - uji menggantinya di tiap sandbox.
static SINGGAHAN: Mutex<Option<(PathBuf, HashMap<String, PathBuf>)>> = Mutex::new(None);

/// Akar instalasi Phoron.
pub fn root() -> PathBuf {
    if let Some(a) = AKAR.read().unwrap().as_ref() {
        return a.clone();
    }
    let a = cari_akar();
    *AKAR.write().unwrap() = Some(a.clone());
    a
}

/// Ganti akar (uji, atau daemon yang diberi `PHORON_HOME` lewat argumen).
pub fn set_root(p: impl Into<PathBuf>) {
    *AKAR.write().unwrap() = Some(p.into());
}

fn cari_akar() -> PathBuf {
    if let Ok(env) = std::env::var("PHORON_HOME") {
        if !env.is_empty() && Path::new(&env).is_dir() {
            return PathBuf::from(env);
        }
    }
    let dasar = std::env::current_exe()
        .ok()
        .and_then(|e| e.parent().map(Path::to_path_buf))
        .unwrap_or_else(|| PathBuf::from("."));
    let mut dir = Some(dasar.as_path());
    while let Some(d) = dir {
        // Penanda: berkas phoron.ini, atau pasangan folder www+etc.
        if d.join("phoron.ini").is_file() || (d.join("www").is_dir() && d.join("etc").is_dir()) {
            return d.to_path_buf();
        }
        dir = d.parent();
    }
    dasar
}

/// Folder di bawah akar; dibuat kalau belum ada.
fn sub(nama: &str) -> PathBuf {
    let akar = root();
    let mut g = SINGGAHAN.lock().unwrap();
    let ganti = match g.as_ref() {
        Some((a, _)) => !crate::teks::sama_abaikan_besar(&a.to_string_lossy(), &akar.to_string_lossy()),
        None => true,
    };
    if ganti {
        *g = Some((akar.clone(), HashMap::new()));
    }
    let peta = &mut g.as_mut().unwrap().1;
    let kunci = nama.to_lowercase();
    if let Some(p) = peta.get(&kunci) {
        return p.clone();
    }
    let p = akar.join(nama);
    let _ = std::fs::create_dir_all(&p);
    // Hanya disinggahkan kalau foldernya memang jadi ada: kegagalan (cakram
    // penuh, hak akses) harus dicoba lagi di panggilan berikutnya.
    if p.is_dir() {
        peta.insert(kunci, p.clone());
    }
    p
}

pub fn bin() -> PathBuf { sub("bin") }
pub fn www() -> PathBuf { sub("www") }
pub fn etc() -> PathBuf { sub("etc") }
pub fn data() -> PathBuf { sub("data") }
pub fn tmp() -> PathBuf { sub("tmp") }
pub fn logs() -> PathBuf { sub("logs") }
pub fn profiles() -> PathBuf { sub("profiles") }
pub fn etc_apache() -> PathBuf { sub(r"etc\apache2") }
pub fn sites_enabled() -> PathBuf { sub(r"etc\apache2\sites-enabled") }
pub fn etc_nginx() -> PathBuf { sub(r"etc\nginx") }
pub fn etc_ssl() -> PathBuf { sub(r"etc\ssl") }
pub fn etc_mysql() -> PathBuf { sub(r"etc\mysql") }
pub fn settings_file() -> PathBuf { root().join("phoron.ini") }

/// Apache dan MySQL hanya menerima garis miring maju di berkas konfigurasinya.
pub fn fwd(p: &str) -> String {
    p.replace('\\', "/")
}

/// Berkas hosts Windows. Bisa disetel supaya uji tidak pernah menyentuh
/// berkas sistem mesin yang sedang dipakai.
pub fn hosts_file() -> PathBuf {
    if let Some(h) = HOSTS.read().unwrap().as_ref() {
        return h.clone();
    }
    let sistem = std::env::var("SystemRoot").unwrap_or_else(|_| r"C:\Windows".into());
    Path::new(&sistem).join(r"System32\drivers\etc\hosts")
}

pub fn set_hosts_file(p: Option<PathBuf>) {
    *HOSTS.write().unwrap() = p;
}

/// Mutex untuk uji yang mengganti akar global. Uji Rust berjalan paralel,
/// sedangkan akar adalah keadaan seluruh proses - sama seperti di C#.
#[cfg(test)]
pub(crate) static KUNCI_UJI: Mutex<()> = Mutex::new(());

#[cfg(test)]
pub(crate) fn sandbox_uji(nama: &str) -> (std::sync::MutexGuard<'static, ()>, PathBuf) {
    let g = KUNCI_UJI.lock().unwrap_or_else(|e| e.into_inner());
    let d = std::env::temp_dir().join(format!("phoron-rs-{}-{}", nama, std::process::id()));
    let _ = std::fs::remove_dir_all(&d);
    std::fs::create_dir_all(&d).unwrap();
    set_root(&d);
    (g, d)
}

#[cfg(test)]
mod uji {
    use super::*;

    #[test]
    fn folder_dibuat_dan_singgahan_ikut_akar() {
        let (_g, d) = sandbox_uji("paths");
        let l = logs();
        assert!(l.is_dir());
        assert_eq!(l, d.join("logs"));
        let d2 = d.join("lain");
        std::fs::create_dir_all(&d2).unwrap();
        set_root(&d2);
        assert_eq!(logs(), d2.join("logs"));
        let _ = std::fs::remove_dir_all(&d);
    }

    #[test]
    fn garis_maju() {
        assert_eq!(fwd(r"C:\Phoron\www"), "C:/Phoron/www");
    }
}
