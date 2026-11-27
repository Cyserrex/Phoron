//! Titik pasang Engine: semua perintah dari UI dan dari menu baki berakhir di
//! sebuah `Pelaksana`.
//!
//! # Cara memasang Engine
//!
//! 1. Buat tipe yang menerapkan [`Pelaksana`]. Simpan salinan [`Penyiar`] yang
//!    diberikan saat dibuat; dari situlah Engine mengirim `Kabar` (status,
//!    log, Node, `PerluMuatUlang`) ke semua UI yang tersambung.
//! 2. Ganti `PelaksanaSementara::baru(...)` di `main.rs` dengan tipe itu.
//!    Bagian lain daemon (pipe, baki, siklus hidup) tidak perlu disentuh.
//!
//! # Kontrak
//!
//! - `tangani` dipanggil dari utas klien pipe (satu utas per klien, perintah
//!   satu klien dijalankan berurutan) dan dari utas pembantu menu baki. Jadi
//!   bisa dipanggil BERSAMAAN dari beberapa utas: tipe ini `Send + Sync`,
//!   kunci sendiri keadaan di dalamnya. Perintah yang lama (menyalakan
//!   layanan) boleh menahan utas pemanggil; klien lain tidak ikut tertahan.
//! - `Perintah::Halo` sudah diperiksa versi protokolnya sebelum sampai ke
//!   sini; `tangani` cukup menjawab `HasilHalo`.
//! - `Perintah::Keluar` TIDAK pernah sampai ke `tangani`. Daemon menjawabnya
//!   sendiri lalu menempuh jalur keluar tunggal (lihat `baki::keluar_rapi`),
//!   yang memanggil [`Pelaksana::sebelum_keluar`]. Jalur yang sama dipakai
//!   menu Keluar dan sinyal `Phoron.KeluarSekarang` dari pemasang.
//! - Perintah yang belum diterapkan dijawab `Galat::BelumAda`, bukan diam.
//! - Teks untuk orang dikirim sebagai `Pesan` (kunci Indonesia + argumen);
//!   UI yang menerjemahkan.

use crate::penyiar::Penyiar;
use phoron_inti::protokol::{
    BarisLogJarak, Galat, HasilHalo, IsiJawaban, Kabar, Keadaan, Perintah, StatusLayanan,
};
use phoron_inti::model::ServiceState;
use phoron_inti::waktu::Saat;

/// Satu butir menu baki yang berasal dari Pelaksana. "Buka Phoron" dan
/// "Keluar" dipasang daemon sendiri di atas dan di bawah butir-butir ini.
// Pemisah dan Sub belum dipakai PelaksanaSementara; Engine yang memakainya.
#[allow(dead_code)]
#[derive(Debug, Clone, PartialEq)]
pub enum ItemMenu {
    /// Butir biasa: memilihnya menjalankan `perintah` lewat `tangani` di utas
    /// pembantu (loop pesan baki tidak boleh tertahan).
    Perintah { teks: String, perintah: Perintah, aktif: bool, tercentang: bool },
    Pemisah,
    /// Sub-menu, mis. daftar proyek Node atau ganti profil.
    Sub { teks: String, isi: Vec<ItemMenu> },
}

impl ItemMenu {
    pub fn biasa(teks: &str, perintah: Perintah) -> ItemMenu {
        ItemMenu::Perintah { teks: teks.into(), perintah, aktif: true, tercentang: false }
    }
}

pub trait Pelaksana: Send + Sync + 'static {
    /// Menjalankan satu perintah dan mengembalikan isi jawabannya.
    fn tangani(&self, perintah: Perintah) -> IsiJawaban;

    /// Butir menu baki di antara "Buka Phoron" dan "Keluar". Dipanggil di
    /// utas utama SETIAP kali menu dibuka, jadi isinya selalu segar (proyek
    /// Node, situs terakhir, profil). Harus cepat: jangan menyentuh cakram
    /// atau jaringan di sini, baca dari keadaan yang sudah ada di memori.
    fn menu_baki(&self) -> Vec<ItemMenu> {
        vec![
            ItemMenu::biasa("Nyalakan semua", Perintah::NyalakanSemua),
            ItemMenu::biasa("Matikan semua", Perintah::MatikanSemua),
        ]
    }

    /// Kesempatan terakhir sebelum daemon berhenti: matikan layanan dengan
    /// rapi, tulis log. Dipanggil sekali, di utas utama, sebelum
    /// `Kabar::Pamit` disiarkan dan ikon baki dihapus.
    fn sebelum_keluar(&self) {}
}

/// Pelaksana selama Engine Rust belum ada: cukup untuk menguji pipe dan baki.
///
/// - `Halo` dan `Keadaan` dijawab (keadaan kosong: semua layanan berhenti).
/// - `Catat` disiarkan apa adanya sebagai `Kabar::Log` - juga yang dipakai uji
///   integrasi untuk memeriksa siaran ke banyak klien.
/// - Sisanya `Galat::BelumAda`.
pub struct PelaksanaSementara {
    penyiar: Penyiar,
}

impl PelaksanaSementara {
    pub fn baru(penyiar: Penyiar) -> Self {
        PelaksanaSementara { penyiar }
    }
}

/// Nama perintah seperti di kabel (`nyalakan_web`), untuk `Galat::BelumAda`.
pub fn nama_perintah(p: &Perintah) -> String {
    let v = serde_json::to_value(p).unwrap_or_default();
    v.get("perintah").and_then(|n| n.as_str()).unwrap_or("?").to_string()
}

fn ok<T: serde::Serialize>(hasil: T) -> IsiJawaban {
    IsiJawaban::Ok { hasil: serde_json::to_value(hasil).unwrap_or_default(), peringatan: Vec::new() }
}

impl Pelaksana for PelaksanaSementara {
    fn tangani(&self, perintah: Perintah) -> IsiJawaban {
        match perintah {
            Perintah::Halo { .. } => ok(HasilHalo {
                versi_daemon: phoron_inti::VERSI.to_string(),
                protokol: phoron_inti::protokol::VERSI_PROTOKOL,
            }),
            Perintah::Keadaan => ok(Keadaan {
                status: StatusLayanan {
                    web: ServiceState::Berhenti,
                    db: ServiceState::Berhenti,
                    galat_web: None,
                    galat_db: None,
                },
                profil_aktif: String::new(),
                node: Vec::new(),
                perlu_restart_web: false,
                perlu_restart_db: false,
                peringatan_terapkan: Vec::new(),
                masalah_muat: Vec::new(),
                admin: false,
            }),
            Perintah::Catat { pesan } => {
                self.penyiar.siar(&Kabar::Log(BarisLogJarak { waktu: Saat::sekarang().ke_o(), pesan }));
                ok(serde_json::Value::Null)
            }
            lain => IsiJawaban::Galat { galat: Galat::BelumAda { perintah: nama_perintah(&lain) } },
        }
    }
}

#[cfg(test)]
mod uji {
    use super::*;

    #[test]
    fn perintah_belum_ada_menyebut_namanya() {
        let p = PelaksanaSementara::baru(Penyiar::baru());
        assert_eq!(
            p.tangani(Perintah::NyalakanWeb),
            IsiJawaban::Galat { galat: Galat::BelumAda { perintah: "nyalakan_web".into() } }
        );
        assert!(matches!(p.tangani(Perintah::Keadaan), IsiJawaban::Ok { .. }));
    }
}
