//! Fitur: memeriksa Oracle Instant Client yang akan ditemukan PHP (oci8, pdo_oci).
//!
//! Ekstensi oci8 dan pdo_oci memanggil oci.dll milik Instant Client, yang TIDAK
//! ikut dalam paket PHP. DLL ekstensinya bisa ada dan sepadan, tapi pemuatannya
//! tetap gagal - dengan pesan Windows yang sama sekali tidak menyebut Oracle.
//!
//! Daftar periksa:
//! - Client 32-bit dengan PHP 64-bit berbunyi "%1 is not a valid Win32
//!   application"; tanpa client sama sekali "The specified module could not be
//!   found". Keduanya menunjuk php_oci8_*.dll yang sebenarnya baik-baik saja,
//!   jadi Phoron menjelaskan apa yang SEBENARNYA kurang.
//! - Urutan pencarian mengikuti Windows untuk DLL yang diimpor: folder PHP
//!   lebih dulu, baru PATH.
//! - SELURUH PATH ditelusuri: client 32-bit di posisi paling depan diikuti
//!   client 64-bit tetap memuat oci8 dengan mulus (Windows melewati yang tidak
//!   sepadan). Berhenti di yang pertama akan menuduh keadaan yang sehat lalu
//!   membuang oci8 dari php.ini tanpa alasan.
//! - Kolam PHP per situs: PATH bisa berisi client 12.1 lebih dulu daripada 19.x.
//!   php_oci8_19.dll butuh client 19 ke atas, jadi `folder_untuk` mencari folder
//!   client yang cukup baru dan sepadan untuk ditaruh paling depan di PATH.
//!   Client yang lebih baru boleh, yang lebih tua tidak.
//! - Arsitektur yang tidak diketahui dianggap sepadan.

use crate::bin::{arsitektur, gabung};
use crate::model::BinPackage;
use crate::profile_store::arsitektur_sepadan;
use crate::protokol::Pesan;
use std::path::Path;

/// Nama ekstensi yang bergantung pada Instant Client.
pub const EKSTENSI: &[&str] = &["oci8", "oci8_11g", "oci8_12c", "oci8_19", "pdo_oci"];

#[derive(Debug, Clone, Default, PartialEq)]
pub struct Hasil {
    /// oci.dll pertama yang akan ditemukan PHP.
    pub jalur_dll: Option<String>,
    pub arsitektur: String,
    pub arsitektur_php: String,
    /// Benar bila oci8 punya peluang termuat.
    pub layak: bool,
    /// Penjelasan siap tampil; `None` bila tidak ada masalah.
    pub pesan: Option<Pesan>,
}

impl Hasil {
    pub fn ada(&self) -> bool {
        self.jalur_dll.is_some()
    }
}

pub fn adalah_ekstensi_oracle(nama: &str) -> bool {
    EKSTENSI.iter().any(|x| crate::teks::sama_abaikan_besar(x, nama))
}

fn arsitektur_php(php: &BinPackage) -> String {
    if php.arch.is_empty() { arsitektur(&php.main_exe) } else { php.arch.clone() }
}

fn path_env(path: Option<&str>) -> String {
    match path {
        Some(p) => p.to_string(),
        None => std::env::var("PATH").unwrap_or_default(),
    }
}

/// Periksa client yang akan dipakai PHP ini. `path` = isi PATH pengganti
/// (uji); `None` = PATH proses.
pub fn periksa(php: Option<&BinPackage>, path: Option<&str>) -> Hasil {
    let mut h = Hasil::default();
    let Some(php) = php else {
        h.layak = true;
        return h;
    };
    h.arsitektur_php = arsitektur_php(php);

    let daftar = path_env(path);
    let folder = std::iter::once(php.path.as_str()).chain(daftar.split(';'));

    let mut pertama: Option<(String, String)> = None;
    for d in folder {
        if d.trim().is_empty() {
            continue;
        }
        let calon = gabung(d.trim(), "oci.dll");
        if !Path::new(&calon).is_file() {
            continue;
        }
        let arch = arsitektur(&calon);
        if pertama.is_none() {
            pertama = Some((calon.clone(), arch.clone()));
        }
        if arsitektur_sepadan(&arch, &h.arsitektur_php) {
            // Ketemu yang sepadan; itulah yang akan dipakai Windows.
            h.jalur_dll = Some(calon);
            h.arsitektur = arch;
            h.layak = true;
            return h;
        }
    }

    if let Some((j, a)) = pertama {
        h.jalur_dll = Some(j);
        h.arsitektur = a;
    }
    h.layak = false;
    h.pesan = Some(match &h.jalur_dll {
        Some(j) => Pesan::dengan(
            "Oracle Instant Client yang ada di komputer ini ({0}) berarsitektur {1}, \
             sedangkan PHP {2} berarsitektur {3}. Keduanya HARUS sama, dan tidak ada client \
             {3} lain di PATH. Pasang Instant Client {3} lalu tambahkan foldernya ke PATH \
             Windows, atau pakai PHP {1}.",
            &[j, &h.arsitektur, &php.version, &h.arsitektur_php],
        ),
        None if !h.arsitektur_php.is_empty() => Pesan::dengan(
            "Oracle Instant Client tidak ditemukan di PATH, jadi oci8 dan pdo_oci tidak akan \
             bisa dimuat. Pasang Instant Client {0}, lalu tambahkan foldernya ke PATH Windows.",
            &[&h.arsitektur_php],
        ),
        None => Pesan::baru(
            "Oracle Instant Client tidak ditemukan di PATH, jadi oci8 dan pdo_oci tidak akan \
             bisa dimuat. Pasang Instant Client yang sesuai arsitektur PHP, lalu tambahkan \
             foldernya ke PATH Windows.",
        ),
    });
    h
}

/// Versi client minimal yang dibutuhkan daftar ekstensi; 0 = tidak ada.
fn versi_perlu<S: AsRef<str>>(ekstensi: &[S]) -> u32 {
    ekstensi
        .iter()
        .map(|e| match e.as_ref().to_lowercase().as_str() {
            "oci8_19" => 19,
            "oci8_12c" => 12,
            "oci8_11g" => 11,
            _ => 0,
        })
        .max()
        .unwrap_or(0)
}

/// Folder Instant Client yang harus ditaruh PALING DEPAN di PATH untuk PHP
/// ini dan daftar ekstensinya, atau `None` bila tidak ada yang perlu diatur.
pub fn folder_untuk<S: AsRef<str>>(php: Option<&BinPackage>, ekstensi: &[S], path: Option<&str>) -> Option<String> {
    let php = php?;
    let perlu = versi_perlu(ekstensi);
    if perlu == 0 {
        return None;
    }
    let arch_php = arsitektur_php(php);
    let daftar = path_env(path);
    for d in daftar.split(';') {
        if d.trim().is_empty() {
            continue;
        }
        let calon = gabung(d.trim(), "oci.dll");
        if !Path::new(&calon).is_file() || !arsitektur_sepadan(&arsitektur(&calon), &arch_php) {
            continue;
        }
        let Some(mayor) = versi_mayor_berkas(&calon) else { continue };
        if mayor >= perlu {
            return Some(d.trim().to_string());
        }
    }
    None
}

/// `FileVersionInfo.FileMajorPart`: bagian mayor versi berkas dari sumber daya
/// VERSIONINFO. Berkas tanpa sumber daya versi bernilai 0, seperti .NET.
pub fn versi_mayor_berkas(jalur: &str) -> Option<u32> {
    use windows_sys::Win32::Storage::FileSystem::{
        GetFileVersionInfoSizeW, GetFileVersionInfoW, VerQueryValueW, VS_FIXEDFILEINFO,
    };
    if !Path::new(jalur).is_file() {
        return None;
    }
    let w: Vec<u16> = jalur.encode_utf16().chain(std::iter::once(0)).collect();
    unsafe {
        let mut h = 0u32;
        let n = GetFileVersionInfoSizeW(w.as_ptr(), &mut h);
        if n == 0 {
            return Some(0);
        }
        let mut buf = vec![0u8; n as usize];
        if GetFileVersionInfoW(w.as_ptr(), 0, n, buf.as_mut_ptr().cast()) == 0 {
            return Some(0);
        }
        let akar: Vec<u16> = "\\".encode_utf16().chain(std::iter::once(0)).collect();
        let mut p: *mut core::ffi::c_void = std::ptr::null_mut();
        let mut len = 0u32;
        if VerQueryValueW(buf.as_ptr().cast(), akar.as_ptr(), &mut p, &mut len) == 0
            || p.is_null()
            || (len as usize) < std::mem::size_of::<VS_FIXEDFILEINFO>()
        {
            return Some(0);
        }
        let info = &*(p as *const VS_FIXEDFILEINFO);
        Some(info.dwFileVersionMS >> 16)
    }
}

#[cfg(test)]
mod uji {
    use super::*;
    use crate::model::BinKind;
    use std::fs;

    /// Berkas PE terkecil yang cukup untuk dibaca `arsitektur`.
    fn pe(jalur: &Path, mesin: u16) {
        let mut b = vec![0u8; 0x100];
        b[0] = b'M';
        b[1] = b'Z';
        b[0x3C] = 0x80;
        b[0x80..0x84].copy_from_slice(b"PE\0\0");
        b[0x84..0x86].copy_from_slice(&mesin.to_le_bytes());
        fs::create_dir_all(jalur.parent().unwrap()).unwrap();
        fs::write(jalur, b).unwrap();
    }

    fn php(dir: &Path) -> BinPackage {
        let mut p = BinPackage::baru(BinKind::Php, "php-8.3", &dir.to_string_lossy(), "8.3.12");
        p.arch = "x64".into();
        p
    }

    #[test]
    fn nama_ekstensi_oracle() {
        assert!(adalah_ekstensi_oracle("OCI8_12c"));
        assert!(adalah_ekstensi_oracle("pdo_oci"));
        assert!(!adalah_ekstensi_oracle("pdo_mysql"));
    }

    #[test]
    fn seluruh_path_ditelusuri_bukan_berhenti_di_yang_pertama() {
        let d = std::env::temp_dir().join(format!("phoron-rs-oracle-{}", std::process::id()));
        let _ = fs::remove_dir_all(&d);
        pe(&d.join(r"ic32\oci.dll"), 0x014c);
        pe(&d.join(r"ic64\oci.dll"), 0x8664);
        let p = php(&d.join("php"));
        let path = format!("{};;{}", d.join("ic32").display(), d.join("ic64").display());
        let h = periksa(Some(&p), Some(&path));
        assert!(h.layak, "client 64-bit di belakang tetap dipakai Windows");
        assert_eq!(h.arsitektur, "x64");

        // Hanya client 32-bit: tidak layak, pesannya menyebut keduanya.
        let h = periksa(Some(&p), Some(&d.join("ic32").to_string_lossy()));
        assert!(!h.layak);
        let pesan = h.pesan.unwrap();
        assert_eq!(pesan.args[1], "x86");
        assert_eq!(pesan.args[3], "x64");

        // Tanpa client.
        let h = periksa(Some(&p), Some(""));
        assert!(!h.layak && !h.ada());
        assert_eq!(h.pesan.unwrap().args, vec!["x64".to_string()]);

        // Tanpa PHP: tidak ada yang perlu diperiksa.
        assert!(periksa(None, Some("")).layak);
        let _ = fs::remove_dir_all(&d);
    }

    #[test]
    fn client_dicari_menurut_versi_ekstensi() {
        let p = php(Path::new(r"Z:\tidak-ada"));
        assert_eq!(folder_untuk(Some(&p), &["mbstring", "pdo_oci"], Some("")), None);
        assert_eq!(versi_perlu(&["oci8_11g", "OCI8_19"]), 19);
        // PE buatan tanpa VERSIONINFO bermayor 0: lebih tua dari yang diminta.
        let d = std::env::temp_dir().join(format!("phoron-rs-oracle-v-{}", std::process::id()));
        let _ = fs::remove_dir_all(&d);
        pe(&d.join(r"ic\oci.dll"), 0x8664);
        assert_eq!(folder_untuk(Some(&p), &["oci8_12c"], Some(&d.join("ic").to_string_lossy())), None);
        let _ = fs::remove_dir_all(&d);
        // DLL sistem punya versi: kernel32 pasti >= 6.
        let k32 = std::env::var("SystemRoot").unwrap_or_else(|_| r"C:\Windows".into()) + r"\System32\kernel32.dll";
        assert!(versi_mayor_berkas(&k32).unwrap() >= 6);
    }
}
