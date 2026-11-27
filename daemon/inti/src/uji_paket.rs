//! Pembantu uji nyata: folder paket sungguhan (C:\laragon\bin, C:\xampp)
//! hanya boleh DIBACA. Uji memotret waktu ubah setiap berkas dan folder di
//! folder paket yang dipakainya sebelum menyalakan apa pun, lalu memastikan
//! tidak ada yang baru atau berubah sesudahnya.

use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::time::SystemTime;

/// Potret waktu ubah semua berkas dan folder di bawah folder-folder paket.
pub struct JagaPaket {
    akar: Vec<PathBuf>,
    awal: HashMap<PathBuf, Option<SystemTime>>,
}

fn potret(akar: &[PathBuf]) -> HashMap<PathBuf, Option<SystemTime>> {
    let mut hasil = HashMap::new();
    let mut antre: Vec<PathBuf> = akar.iter().filter(|a| a.exists()).cloned().collect();
    while let Some(p) = antre.pop() {
        let meta = std::fs::symlink_metadata(&p);
        let waktu = meta.as_ref().ok().and_then(|m| m.modified().ok());
        if meta.as_ref().is_ok_and(|m| m.is_dir()) {
            if let Ok(isi) = std::fs::read_dir(&p) {
                antre.extend(isi.filter_map(|e| e.ok()).map(|e| e.path()));
            }
        }
        hasil.insert(p, waktu);
    }
    hasil
}

impl JagaPaket {
    pub fn baru(folder: &[&str]) -> JagaPaket {
        let akar: Vec<PathBuf> = folder.iter().map(PathBuf::from).collect();
        let awal = potret(&akar);
        JagaPaket { akar, awal }
    }

    /// Berkas atau folder yang baru muncul atau berubah sejak potret awal.
    pub fn berubah(&self) -> Vec<PathBuf> {
        let mut v: Vec<PathBuf> = potret(&self.akar)
            .into_iter()
            .filter(|(p, w)| self.awal.get(p).is_none_or(|lama| lama != w))
            .map(|(p, _)| p)
            .collect();
        v.sort();
        v
    }

    pub fn pastikan_utuh(&self) {
        let v = self.berubah();
        assert!(v.is_empty(), "uji menulisi folder paket: {v:#?}");
    }
}

/// Folder paket yang ada di komputer ini dari daftar ini.
pub fn yang_ada<'a>(folder: &[&'a str]) -> Vec<&'a str> {
    folder.iter().copied().filter(|f| Path::new(f).exists()).collect()
}
