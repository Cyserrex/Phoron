//! Keadaan siklus hidup yang dibagi semua utas: gerbang keluar dan alasan
//! keluar.
//!
//! Gerbang ditutup SEKALI, begitu jalur keluar dimulai (perintah `keluar`,
//! menu Keluar, sinyal pemasang, WM_CLOSE, akhir sesi, mulai ulang sebagai
//! Administrator, atau panik di utas utama). Sesudah itu tidak ada perintah
//! baru yang sampai ke Pelaksana: `sebelum_keluar` sedang (atau sudah)
//! mematikan layanan, dan perintah `nyalakan_web` yang lolos akan
//! meninggalkan httpd yatim tepat sebelum proses berakhir.

use std::sync::atomic::{AtomicBool, AtomicI32, Ordering};

/// Kode keluar bila pipe tidak bisa dibuat (dipegang proses lain).
pub const KELUAR_PIPE_GAGAL: i32 = 2;
/// Kode keluar sesudah panik yang tidak tertangkap (berkas crash sudah ditulis).
pub const KELUAR_PANIK: i32 = 101;

/// Teks galat untuk perintah yang datang sesudah gerbang ditutup.
pub const PESAN_DITUTUP: &str = "Phoron sedang ditutup.";

static GERBANG_TUTUP: AtomicBool = AtomicBool::new(false);
static KODE: AtomicI32 = AtomicI32::new(0);
static TANPA_PAMIT: AtomicBool = AtomicBool::new(false);

/// Kenapa daemon keluar.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Alasan {
    /// Keluar biasa: klien menerima `Kabar::Pamit` dan ikut berhenti.
    Biasa,
    /// Digantikan daemon baru ber-Administrator: TANPA `Kabar::Pamit`, supaya
    /// UI menganggapnya putus biasa lalu menyambung ulang ke daemon baru.
    MulaiUlang,
    /// Panik yang tidak tertangkap: keluar rapi, kode keluar `KELUAR_PANIK`.
    Panik,
}

/// Tutup gerbang dan catat alasannya. Aman dipanggil berkali-kali dari utas
/// mana pun; panik selalu menang atas kode keluar, alasan pertama menentukan
/// ada tidaknya Pamit.
pub fn tutup_gerbang(alasan: Alasan) {
    let pertama = !GERBANG_TUTUP.swap(true, Ordering::SeqCst);
    match alasan {
        Alasan::Panik => KODE.store(KELUAR_PANIK, Ordering::SeqCst),
        Alasan::MulaiUlang if pertama => TANPA_PAMIT.store(true, Ordering::SeqCst),
        _ => {}
    }
}

/// `true` sesudah jalur keluar dimulai.
pub fn gerbang_tertutup() -> bool {
    GERBANG_TUTUP.load(Ordering::SeqCst)
}

pub fn kode_keluar() -> i32 {
    KODE.load(Ordering::SeqCst)
}

pub fn tanpa_pamit() -> bool {
    TANPA_PAMIT.load(Ordering::SeqCst)
}
