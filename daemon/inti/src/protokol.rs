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

use crate::model::ServiceState;
use crate::settings::Settings;
use serde::{Deserialize, Serialize};

/// Naik setiap kali bentuk pesan berubah tanpa kompatibilitas mundur.
pub const VERSI_PROTOKOL: u32 = 2;

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

/// Perintah UI ke daemon (protokol v2).
///
/// Pembagian kerja: UI tetap MEMBACA berkas sendiri (profil, paket, situs,
/// apps.ini) karena isinya identik antara versi C# dan Rust. Yang dikirim ke
/// daemon hanya AKSI - menyalakan, mematikan, menulis config, menjalankan Node -
/// dan penulisan phoron.ini: daemon satu-satunya penulis berkas itu, supaya
/// dua proses tidak saling menimpa setelan. Profil dan apps.ini boleh ditulis
/// UI; sesudahnya UI mengirim `MuatUlang`.
///
/// Perintah yang belum diterapkan dijawab `Galat::BelumAda`, bukan diam.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(tag = "perintah", content = "data", rename_all = "snake_case")]
pub enum Perintah {
    /// Jabat tangan pertama: UI menyebut versinya, daemon menolak bila
    /// protokolnya tidak sepadan. Hasil: `HasilHalo`.
    Halo { versi_ui: String, protokol: u32 },
    /// Keadaan lengkap untuk mengisi layar. Hasil: `Keadaan`.
    Keadaan,
    /// Baca ulang phoron.ini, profil, apps.ini, dan pindai ulang bin/situs
    /// (Engine.Reload). Dikirim UI sesudah menulis profil/apps.ini.
    MuatUlang,
    /// Paket hasil pindaian terakhir daemon. Hasil: daftar `BinPackage`.
    ///
    /// Pengecualian dari "UI membaca berkas sendiri" di atas: memindai bin
    /// berarti menjalankan php/httpd/mysqld untuk folder yang namanya tidak
    /// memuat versi (XAMPP), dan singgahan versinya hanya hidup selama
    /// prosesnya. Daemon hidup terus, UI tidak - di 1.42.0 setiap jendela
    /// yang dibuka menghabiskan sekitar dua detik untuk itu. Sesudah perintah
    /// lain pada sambungan yang sama (`muat_ulang`, `ubah_setelan`) hasilnya
    /// sudah memuat pindaian barunya: perintah satu klien dijalankan berurutan.
    /// Perintah aditif: protokol tetap v2, dan UI yang mendapat `BelumAda`
    /// dari daemon lama memindai sendiri seperti dulu.
    Paket,
    /// Simpan setelan global seutuhnya; daemon menulis phoron.ini lalu memuat
    /// ulang. UI sebaiknya memakai `UbahSetelan`: salinan utuh yang basi akan
    /// menimpa perubahan dari menu baki atau klien lain (lost update).
    SimpanSetelan { setelan: Settings },
    /// Ubah HANYA medan setelan yang disebut (nama kunci = nama medan Settings
    /// Rust, nilai JSON sesuai tipenya). Daemon menerapkannya ke setelan
    /// terkininya, menulis phoron.ini, lalu menyiarkan `PerluMuatUlang`. Medan
    /// yang tidak dikenal ditolak dengan Galat::Gagal (bukan diabaikan diam-diam).
    UbahSetelan { perubahan: serde_json::Map<String, serde_json::Value> },
    /// Jalankan ulang daemon dengan hak Administrator (dialog UAC di desktop
    /// pengguna). Layanan yang sedang hidup dinyalakan lagi oleh daemon baru.
    /// Jawaban ok dikirim sebelum daemon lama keluar; UI menyambung ulang
    /// sendiri. Dibatalkan di UAC = Galat::Gagal.
    MulaiUlangAdmin,
    /// Tulis ulang semua config profil aktif (Engine.Apply). Peringatan ikut
    /// di `peringatan` jawaban.
    Terapkan,
    /// Pindah profil (Engine.SwitchAsync); `restart` = nyalakan ulang layanan
    /// yang sedang hidup.
    GantiProfil { file_name: String, restart: bool },
    NyalakanSemua,
    MatikanSemua,
    NyalakanWeb,
    MatikanWeb,
    NyalakanDb,
    MatikanDb,
    /// Jalankan proyek Node terdaftar; `port_sekali` > 0 = port untuk sekali
    /// jalan ini saja ("Jalankan di port N"). Hasil: URL awal bila sudah ada.
    JalankanNode { path: String, port_sekali: u16 },
    HentikanNode { path: String },
    HentikanSemuaNode,
    /// Riwayat keluaran satu proyek Node. Hasil: daftar baris.
    RiwayatNode { path: String },
    /// Riwayat log Aktivitas. Hasil: daftar `BarisLogJarak`.
    RiwayatLog,
    /// Proses lain yang memegang port profil. Hasil: daftar `port::Usage`.
    SisaProses,
    /// Hentikan proses lain itu. Peringatan berisi yang tidak bisa dihentikan.
    HentikanSisa,
    /// Catat situs PHP yang baru dibuka (untuk "situs terakhir").
    CatatSitusDibuka { path: String },
    /// Tulis log Aktivitas dari UI (Engine.Say di sisi UI).
    Catat { pesan: Pesan },
    /// Kembalikan berkas hosts dari cadangan `nama` (nama berkas di folder
    /// cadangan, bukan jalur: daemon yang ber-Administrator tidak boleh
    /// menyalin berkas sembarang ke hosts atas permintaan klien).
    PulihkanHosts { nama: String },
    /// Buang blok Phoron dari berkas hosts; baris lain tidak disentuh.
    BersihkanHosts,
    /// Tulis nama situs SEMUA profil ke hosts. Hasil: daftar nama.
    DaftarkanHosts,
    /// Buat (ulang) sertifikat HTTPS untuk akhiran profil aktif di etc\ssl.
    /// Config tidak ikut ditulis: UI mengirim `Terapkan` sesudahnya.
    BuatSertifikat,
    /// Pasang CA Phoron ke Trusted Root Windows.
    PercayaiSertifikat,
    /// Daemon ikut berhenti: semua layanan dimatikan dengan rapi, lalu Kabar::Pamit.
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
    /// Aksi butuh hak Administrator yang tidak dimiliki daemon. UI menawarkan
    /// `MulaiUlangAdmin`, lalu orang mengulang aksinya.
    PerluAdmin { pesan: Pesan },
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct HasilHalo {
    pub versi_daemon: String,
    pub protokol: u32,
    /// Folder akar Phoron yang dipegang daemon ini. Nama pipe sama untuk semua
    /// salinan Phoron, jadi UI dari folder lain bisa tersambung ke sini - lalu
    /// menampilkan berkas foldernya sendiri sambil mengendalikan layanan milik
    /// folder ini. UI menolak bila akarnya berbeda. Kosong pada daemon 1.42.0.
    #[serde(default)]
    pub akar: String,
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

/// Satu baris log Aktivitas; `waktu` dalam format "o" UTC.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct BarisLogJarak {
    pub waktu: String,
    pub pesan: Pesan,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct StatusNode {
    pub path: String,
    pub berjalan: bool,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub url: Option<String>,
    /// Port yang bentrok pada jalan terakhir (untuk tawaran "Jalankan di port N").
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub port_bentrok: Option<u16>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Keadaan {
    pub status: StatusLayanan,
    /// file_name profil aktif; kosong bila belum ada profil.
    pub profil_aktif: String,
    pub node: Vec<StatusNode>,
    /// Config web/db di cakram lebih baru daripada yang dipakai layanan yang
    /// sedang hidup: tampilkan tanda "perlu dinyalakan ulang".
    pub perlu_restart_web: bool,
    pub perlu_restart_db: bool,
    /// Peringatan penerapan config terakhir (Engine.LastBuild).
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub peringatan_terapkan: Vec<Pesan>,
    /// Keluhan saat memuat (berkas cacat, profil dilewati).
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub masalah_muat: Vec<Pesan>,
    /// Hak Administrator daemon (hosts dan sertifikat butuh ini).
    pub admin: bool,
}

/// Kabar daemon ke semua klien, tanpa diminta.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(tag = "kabar", content = "data", rename_all = "snake_case")]
pub enum Kabar {
    Status(StatusLayanan),
    Log(BarisLogJarak),
    /// Proyek Node mulai/berhenti.
    Node(StatusNode),
    /// Satu baris keluaran proyek Node. `baris` selalu terisi (teks apa
    /// adanya; untuk baris buatan Phoron berisi teks Indonesia). `pesan` terisi
    /// hanya untuk baris buatan Phoron sendiri ("-- dihentikan --", galat
    /// keluar, peringatan HSTS): UI menerjemahkannya dengan Lang.T.
    NodeKeluaran {
        path: String,
        baris: String,
        #[serde(default, skip_serializing_if = "Option::is_none")]
        pesan: Option<Pesan>,
    },
    /// Setelan, profil, atau paket berubah (dari klien lain, menu baki, atau
    /// sesudah MuatUlang/GantiProfil): UI membaca ulang berkasnya.
    PerluMuatUlang,
    /// Daemon akan berhenti; UI ikut keluar.
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

        let k = baris(&Kabar::Log(BarisLogJarak {
            waktu: "2026-11-27T07:50:29.0000000Z".into(),
            pesan: Pesan::dengan("Menghentikan {0}", &["httpd"]),
        }));
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
