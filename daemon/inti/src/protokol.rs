//! Protokol antara UI (WPF) dan daemon lewat named pipe `\\.\pipe\phoron`.
//!
//! Satu pesan = satu baris JSON UTF-8 diakhiri `\n`. Baris, bukan bingkai
//! biner: mudah dibaca saat menelusuri masalah, dan C# cukup memakai
//! `StreamReader.ReadLine`.
//!
//! Tiga jenis pesan:
//! - `Permintaan` (UI -> daemon) membawa `id`; daemon menjawab dengan
//!   `Jawaban` ber-`id` sama, urutan jawaban boleh berbeda dari permintaan.
//! - `Kabar` (daemon -> UI) tanpa `id`: perubahan status dan baris log,
//!   dikirim ke semua klien yang tersambung.
//!
//! Teks untuk orang TIDAK diterjemahkan di daemon. Daemon mengirim `Pesan`
//! (kunci teks Indonesia + argumen), UI yang memanggil `Lang.T` - sama seperti
//! sekarang, jadi kamus terjemahan tetap satu dan tetap di C#.

use crate::model::{BinPackage, Profile, ServiceState};
use crate::settings::Settings;
use serde::{Deserialize, Serialize};

/// Naik setiap kali bentuk pesan berubah tanpa kompatibilitas mundur.
pub const VERSI_PROTOKOL: u32 = 1;

pub const NAMA_PIPE: &str = r"\\.\pipe\phoron";

/// Batas satu baris. Baris lebih panjang ditolak, bukan ditampung: klien yang
/// rusak tidak boleh bisa menghabiskan memori daemon.
pub const BATAS_BARIS: usize = 4 * 1024 * 1024;

/// Teks untuk orang, belum diterjemahkan: `Lang.T(kunci, args...)` di UI.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct Pesan {
    pub kunci: String,
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub args: Vec<String>,
}

impl Pesan {
    pub fn baru(kunci: &str) -> Self {
        Pesan { kunci: kunci.into(), args: Vec::new() }
    }
    pub fn dengan(kunci: &str, args: &[&str]) -> Self {
        Pesan { kunci: kunci.into(), args: args.iter().map(|s| s.to_string()).collect() }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Permintaan {
    pub id: u64,
    #[serde(flatten)]
    pub perintah: Perintah,
}

/// Perintah UI ke daemon. Daftar ini tumbuh mengikuti tahap port; yang belum
/// diterapkan daemon dijawab `Galat::BelumAda`, bukan diam.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(tag = "perintah", content = "data", rename_all = "snake_case")]
pub enum Perintah {
    /// Jabat tangan pertama: UI menyebut versinya, daemon menolak bila
    /// protokolnya tidak sepadan.
    Halo { versi_ui: String, protokol: u32 },
    /// Keadaan lengkap untuk mengisi layar.
    Keadaan,
    Setelan,
    SimpanSetelan { setelan: Settings },
    DaftarProfil,
    SimpanProfil { profil: Profile },
    HapusProfil { file_name: String },
    GantiProfil { file_name: String, restart: bool },
    NyalakanSemua,
    MatikanSemua,
    NyalakanWeb,
    MatikanWeb,
    NyalakanDb,
    MatikanDb,
    /// Daemon ikut berhenti: semua layanan dimatikan dengan rapi.
    Keluar,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Jawaban {
    pub id: u64,
    #[serde(flatten)]
    pub isi: IsiJawaban,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(tag = "status", rename_all = "snake_case")]
pub enum IsiJawaban {
    Ok {
        #[serde(default, skip_serializing_if = "serde_json::Value::is_null")]
        hasil: serde_json::Value,
        /// Peringatan yang tidak menggagalkan perintah (mis. hosts tidak bisa
        /// ditulis tanpa Administrator).
        #[serde(default, skip_serializing_if = "Vec::is_empty")]
        peringatan: Vec<Pesan>,
    },
    Galat { galat: Galat },
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(tag = "jenis", rename_all = "snake_case")]
pub enum Galat {
    /// Perintah dikenal tapi belum diterapkan di daemon tahap ini.
    BelumAda { perintah: String },
    /// Versi protokol UI dan daemon tidak sepadan.
    ProtokolBeda { daemon: u32, ui: u32 },
    /// Baris tidak bisa diurai.
    TidakTerbaca { sebab: String },
    /// Galat biasa dengan pesan untuk orang.
    Gagal { pesan: Pesan },
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct HasilHalo {
    pub versi_daemon: String,
    pub protokol: u32,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct StatusLayanan {
    pub web: ServiceState,
    pub db: ServiceState,
    /// Pesan galat terakhir per layanan, bila ada.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub galat_web: Option<Pesan>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub galat_db: Option<Pesan>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Keadaan {
    pub status: StatusLayanan,
    pub profil_aktif: Option<Profile>,
    pub profil: Vec<Profile>,
    pub paket: Vec<BinPackage>,
    /// Keluhan saat memuat (berkas cacat, profil dilewati).
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub masalah_muat: Vec<Pesan>,
}

/// Kabar daemon ke semua klien, tanpa diminta.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(tag = "kabar", content = "data", rename_all = "snake_case")]
pub enum Kabar {
    Status(StatusLayanan),
    /// Satu baris log Aktivitas; `waktu` dalam format "o" UTC.
    Log { waktu: String, pesan: Pesan },
    /// Profil atau setelan berubah dari klien lain/menu baki: muat ulang.
    PerluMuatUlang,
    /// Daemon akan berhenti; UI sebaiknya ikut keluar.
    Pamit,
}

/// Semua yang bisa datang dari daemon dalam satu aliran.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(untagged)]
pub enum DariDaemon {
    Jawaban(Jawaban),
    Kabar(Kabar),
}

/// Satu baris siap kirim (sudah berakhiran `\n`).
pub fn baris<T: Serialize>(pesan: &T) -> String {
    // Serialisasi tipe di modul ini tidak bisa gagal: tidak ada peta berkunci
    // non-string dan tidak ada implementasi Serialize buatan tangan.
    let mut s = serde_json::to_string(pesan).expect("serialisasi protokol");
    s.push('\n');
    s
}

pub fn urai<'a, T: Deserialize<'a>>(baris: &'a str) -> Result<T, Galat> {
    if baris.len() > BATAS_BARIS {
        return Err(Galat::TidakTerbaca { sebab: format!("baris melebihi {} byte", BATAS_BARIS) });
    }
    serde_json::from_str(baris.trim_end_matches(['\r', '\n']))
        .map_err(|e| Galat::TidakTerbaca { sebab: e.to_string() })
}

#[cfg(test)]
mod uji {
    use super::*;

    #[test]
    fn bentuk_permintaan_di_kabel() {
        let p = Permintaan { id: 7, perintah: Perintah::GantiProfil { file_name: "php-8".into(), restart: true } };
        let s = baris(&p);
        assert_eq!(s, "{\"id\":7,\"perintah\":\"ganti_profil\",\"data\":{\"file_name\":\"php-8\",\"restart\":true}}\n");
        assert_eq!(urai::<Permintaan>(&s).unwrap(), p);

        let tanpa_data = baris(&Permintaan { id: 1, perintah: Perintah::Keadaan });
        assert_eq!(tanpa_data, "{\"id\":1,\"perintah\":\"keadaan\"}\n");
    }

    #[test]
    fn jawaban_dan_kabar_dibedakan_tanpa_tanda_tambahan() {
        let j = baris(&Jawaban {
            id: 3,
            isi: IsiJawaban::Galat { galat: Galat::BelumAda { perintah: "nyalakan_web".into() } },
        });
        assert!(matches!(urai::<DariDaemon>(&j).unwrap(), DariDaemon::Jawaban(_)));

        let k = baris(&Kabar::Log {
            waktu: "2026-11-27T07:50:29.0000000Z".into(),
            pesan: Pesan::dengan("Menghentikan {0}", &["httpd"]),
        });
        assert!(matches!(urai::<DariDaemon>(&k).unwrap(), DariDaemon::Kabar(_)));
    }

    #[test]
    fn baris_rusak_jadi_galat_bukan_panik() {
        assert!(matches!(urai::<Permintaan>("{ngawur"), Err(Galat::TidakTerbaca { .. })));
        assert!(matches!(
            urai::<Permintaan>("{\"id\":1,\"perintah\":\"tidak_dikenal\"}"),
            Err(Galat::TidakTerbaca { .. })
        ));
    }
}
