//! Bagian bersama yang dipakai beberapa fitur sekaligus (config, SSL, beranda,
//! hosts). Ditaruh di sini supaya modul-modul itu tidak saling bergantung
//! melingkar seperti di versi C# (ConfigWriter <-> SslTool <-> Beranda).

use crate::atomic_file;
use crate::paths;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};

/// Alamat beranda Phoron di bawah localhost.
pub const BERANDA_ALIAS: &str = "/phoron";

/// Folder berkas beranda Phoron (PHP biasa), `<akar>\etc\dashboard`.
pub fn beranda_folder() -> PathBuf {
    paths::etc().join("dashboard")
}

static VERSI_WEB: AtomicU64 = AtomicU64::new(0);
static VERSI_DB: AtomicU64 = AtomicU64::new(0);

/// Naik setiap kali config web (httpd/nginx/php/vhost/ssl) benar-benar berubah
/// di cakram. Layanan yang menyala dengan versi lebih lama perlu dinyalakan
/// ulang - inilah dasar tanda "perlu restart" di layar.
pub fn versi_konfig_web() -> u64 {
    VERSI_WEB.load(Ordering::Acquire)
}

/// Sama seperti di atas, untuk my.ini.
pub fn versi_konfig_db() -> u64 {
    VERSI_DB.load(Ordering::Acquire)
}

/// Tandai config web berubah tanpa lewat `write_if_changed` (mis. sertifikat
/// SSL baru: berkasnya ditulis openssl, bukan oleh Phoron).
pub fn tandai_web_berubah() {
    VERSI_WEB.fetch_add(1, Ordering::AcqRel);
}

fn catat_berubah(path: &Path) {
    // Beranda Phoron berupa PHP biasa: berlaku pada permintaan berikutnya
    // tanpa menyalakan ulang apa pun.
    let beranda = beranda_folder();
    let a = path.to_string_lossy().to_lowercase();
    let b = beranda.to_string_lossy().to_lowercase();
    if a.starts_with(&(b.trim_end_matches('\\').to_string() + "\\")) {
        return;
    }
    let nama = path.file_name().map(|n| n.to_string_lossy().to_lowercase()).unwrap_or_default();
    if nama == "my.ini" {
        VERSI_DB.fetch_add(1, Ordering::AcqRel);
    } else {
        VERSI_WEB.fetch_add(1, Ordering::AcqRel);
    }
}

/// Tulis hanya bila isinya berubah; kembalikan true bila ditulis.
///
/// Akhir baris dinormalkan ke CRLF seperti versi C#. Pembacaan pembanding boleh
/// gagal (berkas sedang dipegang httpd): anggap berubah dan tulis - lebih baik
/// menulis sekali tanpa perlu daripada menggagalkan seluruh penerapan config.
pub fn write_if_changed(path: &Path, content: &str) -> std::io::Result<bool> {
    let isi = content.replace("\r\n", "\n").replace('\n', "\r\n");
    if let Ok(lama) = std::fs::read(path) {
        if lama == isi.as_bytes() {
            return Ok(false);
        }
    }
    atomic_file::write_all_text(path, &isi)?;
    catat_berubah(path);
    Ok(true)
}

/// Proses ini berjalan dengan hak Administrator (token ter-elevasi).
pub fn is_admin() -> bool {
    unsafe { windows_sys::Win32::UI::Shell::IsUserAnAdmin() != 0 }
}

#[cfg(test)]
mod uji {
    use super::*;

    #[test]
    fn hanya_menulis_bila_berubah_dan_menaikkan_versi() {
        let (_g, d) = paths::sandbox_uji("tulis");
        let f = d.join("etc").join("httpd.conf");
        let v0 = versi_konfig_web();
        assert!(write_if_changed(&f, "a\nb\n").unwrap());
        assert_eq!(std::fs::read(&f).unwrap(), b"a\r\nb\r\n");
        assert!(!write_if_changed(&f, "a\r\nb\r\n").unwrap());
        assert!(versi_konfig_web() > v0);
        let db0 = versi_konfig_db();
        assert!(write_if_changed(&d.join("my.ini"), "x").unwrap());
        assert!(versi_konfig_db() > db0);
        let _ = std::fs::remove_dir_all(&d);
    }
}
