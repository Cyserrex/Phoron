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
//! - `Perintah::MulaiUlangAdmin` juga tidak sampai ke `tangani`: daemon
//!   sendiri yang menjalankan penggantinya (lihat `admin.rs`). Pelaksana hanya
//!   ditanya [`Pelaksana::layanan_hidup`] dan, di daemon baru, diminta
//!   [`Pelaksana::nyalakan_awal`].
//! - Begitu jalur keluar dimulai (gerbang di `siklus.rs`), `tangani` tidak
//!   dipanggil lagi untuk perintah baru: pipe menjawabnya `Galat::Gagal`
//!   "Phoron sedang ditutup." dan butir menu baki diabaikan. Perintah yang
//!   SUDAH berjalan tetap berjalan bersamaan dengan `sebelum_keluar`; Engine
//!   yang menjaga agar layanan yang dinyalakan perintah itu ikut dimatikan
//!   (Job Object KILL_ON_JOB_CLOSE).
//! - Panik di dalam `tangani` ditangkap (`tangani_aman`): perintah itu dijawab
//!   `Galat::Gagal`, berkas crash ditulis, daemon tetap hidup. Keadaan yang
//!   dikunci Mutex bisa saja "teracuni"; Engine sebaiknya memakai
//!   `lock().unwrap_or_else(|e| e.into_inner())`.
//! - Perintah yang belum diterapkan dijawab `Galat::BelumAda`, bukan diam.
//! - Teks untuk orang dikirim sebagai `Pesan` (kunci Indonesia + argumen);
//!   UI yang menerjemahkan.

use crate::penyiar::Penyiar;
use crate::siklus;
use phoron_inti::protokol::{
    BarisLogJarak, Galat, HasilHalo, IsiJawaban, Kabar, Keadaan, Perintah, Pesan, StatusLayanan,
};
use std::panic::{catch_unwind, AssertUnwindSafe};
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

    /// Layanan yang sedang hidup, untuk dinyalakan lagi oleh daemon pengganti
    /// ber-Administrator (`MulaiUlangAdmin`). Bentuk tiap butir bebas milik
    /// Engine (mis. "web", "db", "node:<path>"), asal tanpa karakter `|`
    /// (pemisah di baris perintah; tidak mungkin muncul di jalur Windows).
    fn layanan_hidup(&self) -> Vec<String> {
        Vec::new()
    }

    /// Dipanggil SEKALI di daemon pengganti sesudah pipe siap, di utas
    /// pembantu, dengan daftar dari `layanan_hidup` daemon lama.
    fn nyalakan_awal(&self, _daftar: &[String]) {}
}

/// Teks galat untuk perintah yang panik.
pub const PESAN_PANIK: &str = "Perintah {0} gagal karena galat tak terduga di Phoron. Rinciannya ada di logs\\crash-phorond-*.txt.";

/// Satu-satunya jalan memanggil `Pelaksana::tangani`: menolak perintah baru
/// sesudah gerbang keluar ditutup, dan mengubah panik menjadi `Galat::Gagal`
/// (panic hook sudah menulis berkas crash).
pub fn tangani_aman(p: &dyn Pelaksana, perintah: Perintah) -> IsiJawaban {
    if siklus::gerbang_tertutup() {
        return IsiJawaban::Galat { galat: Galat::Gagal { pesan: Pesan::baru(siklus::PESAN_DITUTUP) } };
    }
    let nama = nama_perintah(&perintah);
    match catch_unwind(AssertUnwindSafe(|| p.tangani(perintah))) {
        Ok(isi) => isi,
        Err(_) => IsiJawaban::Galat { galat: Galat::Gagal { pesan: Pesan::dengan(PESAN_PANIK, &[&nama]) } },
    }
}

/// Jalankan `f`; panik di dalamnya menjadi `None` (berkas crash sudah ditulis).
pub fn aman<T>(f: impl FnOnce() -> T) -> Option<T> {
    catch_unwind(AssertUnwindSafe(f)).ok()
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

/// Pembungkus untuk uji integrasi, aktif hanya bila env
/// `PHORON_UJI_PELAKSANA=1` DAN nama objek berakhiran uji. Meneruskan semua
/// ke pelaksana di dalamnya, kecuali:
/// - `catat` berkunci `uji:panik` panik di dalam `tangani`;
/// - `catat` berkunci `uji:panik-utama` membuat utas utama (wndproc) panik;
/// - `sebelum_keluar` dan `nyalakan_awal` menulis penanda di akar, dan
///   `sebelum_keluar` menahan selama `PHORON_UJI_TAHAN_KELUAR` milidetik;
/// - `layanan_hidup` = isi env `PHORON_UJI_HIDUP` dipisah `|`.
pub struct PelaksanaUji {
    dalam: Box<dyn Pelaksana>,
}

impl PelaksanaUji {
    pub fn bungkus(dalam: Box<dyn Pelaksana>) -> Self {
        PelaksanaUji { dalam }
    }

    fn penanda(nama: &str, isi: &str) {
        let _ = std::fs::write(phoron_inti::paths::root().join(nama), isi);
    }
}

impl Pelaksana for PelaksanaUji {
    fn tangani(&self, perintah: Perintah) -> IsiJawaban {
        if let Perintah::Catat { pesan } = &perintah {
            match pesan.kunci.as_str() {
                "uji:panik" => panic!("panik uji di tangani"),
                "uji:panik-utama" => {
                    crate::baki::uji_panik_utama();
                    return ok(serde_json::Value::Null);
                }
                _ => {}
            }
        }
        self.dalam.tangani(perintah)
    }

    fn menu_baki(&self) -> Vec<ItemMenu> {
        self.dalam.menu_baki()
    }

    fn sebelum_keluar(&self) {
        Self::penanda("sebelum_keluar.txt", "ya");
        let ms = std::env::var("PHORON_UJI_TAHAN_KELUAR").ok().and_then(|v| v.parse().ok()).unwrap_or(0u64);
        std::thread::sleep(std::time::Duration::from_millis(ms));
        self.dalam.sebelum_keluar();
    }

    fn layanan_hidup(&self) -> Vec<String> {
        let v = std::env::var("PHORON_UJI_HIDUP").unwrap_or_default();
        v.split('|').filter(|s| !s.is_empty()).map(String::from).collect()
    }

    fn nyalakan_awal(&self, daftar: &[String]) {
        Self::penanda("nyalakan_awal.txt", &daftar.join("|"));
        self.dalam.nyalakan_awal(daftar);
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

    struct Panik;
    impl Pelaksana for Panik {
        fn tangani(&self, _: Perintah) -> IsiJawaban {
            panic!("sengaja");
        }
    }

    #[test]
    fn panik_di_tangani_menjadi_galat() {
        match tangani_aman(&Panik, Perintah::NyalakanDb) {
            IsiJawaban::Galat { galat: Galat::Gagal { pesan } } => {
                assert_eq!(pesan.kunci, PESAN_PANIK);
                assert_eq!(pesan.args, vec!["nyalakan_db".to_string()]);
            }
            lain => panic!("{lain:?}"),
        }
        assert_eq!(aman(|| 5), Some(5));
    }
}
