//! Fitur "orkestrasi" dari Engine.cs - perekat setelan, hasil pindai paket,
//! profil, penulis config, layanan, dan proyek Node.
//!
//! Daemon (`phorond`) hanya berbicara dengan [`Engine`]: semua perintah
//! protokol v2 dipetakan satu-satu oleh [`tangani`], supaya urutan "tulis
//! config dulu, baru nyalakan" tidak pernah terlewat di jalur mana pun (UI,
//! menu baki, autostart). Semua fungsi yang menyalakan/mematikan MEMBLOKIR:
//! daemon memanggilnya dari utas kerja, bukan utas pipe.
//!
//! Keluaran ke luar lewat satu titik: penerima `Kabar` yang diberikan saat
//! membuat Engine (callback, atau channel lewat [`Engine::dengan_saluran`]).
//! Peristiwa layanan dan Node diterjemahkan ke `Kabar::Status`, `Kabar::Log`,
//! `Kabar::Node`, `Kabar::NodeKeluaran`; `Kabar::PerluMuatUlang` dikirim
//! sesudah muat ulang dan ganti profil.
//!
//! Daftar periksa (dari komentar "kenapa" di Engine.cs; tiap butir punya uji):
//!
//! Pemuatan
//! - phoron.ini ditulis sejak jalan pertama, bukan menunggu pengguna menyimpan:
//!   berkas itu penanda akar instalasi. Uji: `jalan_pertama_menulis_phoron_ini`.
//! - Keluhan memuat (phoron.ini cacat, profil tak terbaca) tidak diam: ikut di
//!   `Keadaan::masalah_muat` dan di peringatan Terapkan. Keluhan profil lama
//!   dibuang tiap muat ulang, tidak menumpuk. Uji: `masalah_muat_tidak_menumpuk`.
//! - Tanpa profil sama sekali tapi ada PHP: dibuatkan satu profil per versi PHP
//!   (`profile_store::seed`), dengan daftar ekstensi terisi. Uji:
//!   `nyata_jalan_pertama_membuat_profil`.
//! - Tanpa profil aktif tersimpan, yang dipilih profil dengan PHP TERBARU, bukan
//!   yang pertama menurut abjad (yang kebetulan selalu versi paling tua). Uji:
//!   `profil_bawaan_php_terbaru`.
//! - Tanpa Virtual Host, catatan pemindai situs (semuanya tentang nama host)
//!   dibuang: nama itu tidak dipakai siapa pun. Uji: `catatan_situs_hanya_dengan_vhost`.
//!
//! Paket
//! - Nilai profil bisa nama folder, kunci kembar ("mysql@C-xampp"), atau jalur
//!   lengkap (paket kembar). Jalur yang tidak ada di komputer ini jatuh ke nama
//!   folder terakhirnya - profil dibawa dari komputer lain. Uji: `find_*`.
//! - Kosong = "(tidak dipakai)", pilihan sengaja: TIDAK diganti versi tertinggi.
//!   Dulu profil tanpa MySQL menyalakan MariaDB 10.1 milik XAMPP di 3306 tanpa
//!   satu pesan pun. Uji: `pakai_kosong_tetap_kosong`.
//! - Id yang disebut profil tapi tidak ada: diganti (Apache lewat kecocokan
//!   arsitektur/toolset dengan PHP profil - Apache VS16 dengan PHP VC11 mati
//!   tanpa pesan; lainnya versi tertinggi) DAN dilaporkan (`penyesuaian`).
//!   Berkas profil TIDAK diubah. Uji: `pakai_mengganti_dan_melapor`.
//! - Nama folder kembar disebutkan (yang terpakai bisa berubah kalau urutan
//!   folder bin diubah), tapi dicatat ke log hanya SEKALI per sesi: dulu dua
//!   baris tiap start, setiap hari, sampai orang berhenti membaca peringatan.
//!   Uji: `paket_kembar_disebut_sekali`.
//!
//! Terapkan
//! - Halaman sambutan hanya ditulis ke www yang benar-benar KOSONG; folder kerja
//!   orang lain tidak pernah disentuh. Uji: `sambutan_hanya_di_folder_kosong`.
//! - Sertifikat dibuat sekali bila belum ada (HTTPS mati total tanpanya); CA
//!   yang diganti disebut, karena kepercayaannya hilang.
//! - Kolam PHP per situs: satu per VERSI, ekstensinya dari profil yang memakai
//!   versi itu sebagai PHP utama (oci8_11g PHP 5.6 tidak ada di PHP 8.3),
//!   selain itu daftar yang disarankan; versi yang sama dengan PHP profil
//!   (dibandingkan lewat JALUR) dilewati. Uji: `kolam_per_versi`.
//! - Peringatan Terapkan memuat juga masalah muat, catatan situs, sertifikat,
//!   paket kembar, dan penyesuaian - satu folder yang salah ketik tidak boleh
//!   hanya berwujud situs yang hilang.
//! - Ekstensi yang diambil alih dari php.ini dasar disimpan ke profil supaya
//!   terlihat dan tidak berubah lagi bila berkas dasarnya berubah.
//! - hosts: tanpa Virtual Host blok Phoron DIKOSONGKAN (nama tertinggal tetap
//!   dijawab vhost bawaan dengan proyek yang salah). Tanpa hak: pesan
//!   Administrator; galat lain "Gagal menyunting hosts: " + pesannya. Uji:
//!   `galat_hosts_dibungkus`.
//! - Situs dipindai ulang SEKALI sesudah vhost dan hosts ditulis (dulu tiga
//!   pemindaian per Terapkan, dua beruntun tanpa apa pun berubah).
//!
//! Layanan
//! - Start SELALU membangun ulang config: "bangun kalau belum pernah" berarti
//!   profil yang disunting tidak pernah sampai ke server. Uji: e2e.
//! - Profil tanpa web server/MySQL adalah pilihan, bukan kegagalan: dicatat,
//!   lampunya tidak merah. Uji: `tanpa_web_bukan_gagal`.
//! - Ganti profil berurutan (satu dalam satu waktu): dua yang berpapasan saling
//!   membaca keadaan setengah jadi dan stop milik yang kedua membatalkan start
//!   yang pertama. Uji: `ganti_profil_berpapasan`.
//! - Ganti profil juga diurutkan terhadap start dan Terapkan dari utas lain
//!   (`kunci_profil`): profil aktif tidak pernah berganti di tengah start.
//!   Uji: `ganti_profil_menunggu_start`.
//! - "Hidup" termasuk yang MASIH menyala: MySQL yang sedang memulihkan InnoDB
//!   atau masih init dulu dibiarkan lalu menyala hijau dengan profil lama.
//!   MySQL dimatikan dengan rapi. Uji: `nyata_ujung_ke_ujung`,
//!   `nyata_ganti_profil_saat_mysql_init`.
//! - Ganti profil yang gagal menulis config tetap mencoba menyalakan ulang
//!   layanan yang tadi hidup: layanan tidak tertinggal mati.
//! - Daemon berhenti (`tutup`): gerbang ditutup, start yang berjalan
//!   dibatalkan, ganti profil dan start yang berbenah ditunggu (paling lama
//!   `BATAS_TUTUP`), baru semuanya dimatikan - MySQL dengan rapi. Uji:
//!   `tutup_menolak_start_baru`, `nyata_tutup_di_tengah_init`.
//! - "Perlu restart" = layanan jalan tapi versi config di cakram sudah lain dari
//!   saat ia menyala. Start untuk layanan yang SUDAH jalan tidak memadamkan
//!   penandanya (server masih memakai config lama). Uji:
//!   `perlu_restart_menurut_versi`.
//!
//! Setelan
//! - phoron.ini dibaca dari cakram hanya saat Engine dibuat; daemon satu-
//!   satunya penulisnya, jadi setelan di memori sumber kebenaran. Muat ulang
//!   tidak membacanya lagi. Semua baca-ubah-tulis setelan (ganti profil,
//!   situs terakhir, simpan/ubah setelan) di bawah kunci tulis data. Uji:
//!   `muat_ulang_tidak_membaca_phoron_ini`.
//! - Simpan/ubah setelan tidak menyentuh profil aktif dan situs terakhir;
//!   `ubah_setelan` yang menyebut profil aktif ditolak. Perubahan log rinci
//!   langsung sampai ke layanan. Uji: `setelan_tidak_mengganti_profil_aktif`.
//!
//! Node dan log
//! - "Proyek X siap di URL" dicatat Engine (dulu oleh halaman Node, jadi hilang
//!   kalau halamannya tertutup).
//! - Peringatan HSTS diperiksa di utas latar (menunggu jaringan sampai beberapa
//!   detik; utas pembaca keluaran tidak boleh tertahan) dan disampaikan di DUA
//!   tempat: log Aktivitas (tersimpan, dibaca berhari-hari kemudian) dan
//!   keluaran proyek (yang sedang ditatap). Uji: `hsts_disisipkan_ke_keluaran`.
//! - Port tawaran/port bebas melewati port web dan MySQL profil. Uji:
//!   `port_bebas_melewati_port_profil`.
//! - Riwayat log dipegang Engine (halaman yang dibuat ulang kehilangannya),
//!   maksimal 200 baris, dicatat SEBELUM pendengar dipanggil, dan ditulis ke
//!   logs\phoron.log lewat `log_file` (berputar). Uji: `riwayat_dibatasi_dan_berurutan`.
//! - Situs terakhir: dipindah ke depan, tanpa kembar (tidak peka besar-kecil),
//!   maksimal 5; Engine yang menyimpan phoron.ini (daemon satu-satunya
//!   penulisnya). Uji: `situs_terakhir`.
//!
//! Beda yang disengaja dengan C#:
//! - Peringatan HSTS (tiga kalimat) dikirim sebagai tiga baris log dan tiga
//!   baris keluaran, masing-masing `Pesan` yang bisa diterjemahkan.
//! - phoron.ini yang gagal disimpan saat ganti profil tidak membatalkan ganti
//!   profil (layanan tidak tertinggal mati); keluhannya ikut di peringatan.
//! - Ekstensi yang diambil alih dari php.ini dasar disimpan di atas isi
//!   berkas profil TERKINI, bukan salinan memori (profiles\*.ini milik UI).

use crate::config::{self, Hasil, Kolam, Opsi};
use crate::model::{BinKind, BinPackage, Profile, ServiceState, Site, Versi};
use crate::node_apps::{self, NodeApp};
use crate::node_runner::{self, BarisNode, NodeRunner};
use crate::port::{self, Usage};
use crate::protokol::{BarisLogJarak, Galat, IsiJawaban, Kabar, Keadaan, Perintah, Pesan, StatusNode};
use crate::services::{self, Layanan};
use crate::settings::{self, Settings};
use crate::teks::sama_abaikan_besar;
use crate::waktu::Saat;
use crate::{bin, hosts, hsts, log_file, paths, profile_store, situs, ssl, tulis};
use std::collections::{HashSet, VecDeque};
use std::path::Path;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{mpsc, Arc, Mutex, MutexGuard, RwLock, RwLockReadGuard, RwLockWriteGuard};
use std::time::{Duration, Instant};

/// Sebanyak ini baris riwayat log disimpan untuk layar.
pub const RIWAYAT_MAKS: usize = 200;

/// Paling lama sekian `tutup` menunggu ganti profil dan start yang sedang
/// berjalan selesai atau selesai berbenah.
pub const BATAS_TUTUP: Duration = Duration::from_secs(60);

/// Penanda "layanan belum pernah menyala di sesi ini".
const BELUM_NYALA: u64 = u64::MAX;

type PenerimaKabar = Arc<dyn Fn(Kabar) + Send + Sync>;

fn kunci<T>(m: &Mutex<T>) -> MutexGuard<'_, T> {
    m.lock().unwrap_or_else(|e| e.into_inner())
}

// ------------------------------------------------------------ siaran

/// Bagian yang dipegang penerima peristiwa layanan dan Node. Terpisah dari
/// `Inti` supaya closure di `Layanan`/`NodeRunner` tidak memegang Engine
/// (rujukan melingkar).
struct Siar {
    penerima: PenerimaKabar,
    riwayat: Mutex<VecDeque<BarisLogJarak>>,
    /// Untuk menyisipkan peringatan HSTS dan membaca URL proyek. Dikosongkan
    /// saat Engine dilepas (memutus lingkaran runner -> closure -> Siar).
    node: Mutex<Option<NodeRunner>>,
}

impl Siar {
    fn kirim(&self, k: Kabar) {
        (self.penerima)(k);
    }

    fn say(&self, p: Pesan) {
        let baris = BarisLogJarak { waktu: Saat::sekarang().ke_o(), pesan: p };
        // Dicatat SEBELUM pendengar dipanggil: penggambar layar yang membaca
        // riwayat sudah melihat baris ini.
        {
            let mut r = kunci(&self.riwayat);
            r.push_back(baris.clone());
            while r.len() > RIWAYAT_MAKS {
                r.pop_front();
            }
        }
        let teks = config::teks(&baris.pesan);
        self.kirim(Kabar::Log(baris));
        log_file::tambah(&paths::logs().join("phoron.log"), &format!("{}  {}", cap_lokal_log(), teks));
    }

    fn node(&self) -> Option<NodeRunner> {
        kunci(&self.node).clone()
    }

    fn dari_layanan(&self, p: services::Peristiwa) {
        match p {
            services::Peristiwa::Status(s) => self.kirim(Kabar::Status(s)),
            services::Peristiwa::Log(p) => self.say(p),
        }
    }

    fn dari_node(siar: &Arc<Siar>, p: node_runner::Peristiwa) {
        match p {
            node_runner::Peristiwa::Keluaran { folder, baris } => {
                let pesan = match &baris {
                    BarisNode::Pesan(p) => Some(p.clone()),
                    BarisNode::Mentah(_) => None,
                };
                siar.kirim(Kabar::NodeKeluaran { path: folder, baris: baris.teks(), pesan });
            }
            node_runner::Peristiwa::Status { folder, berjalan } => {
                let (url, bentrok) = match siar.node() {
                    Some(n) => (n.url_of(&folder), n.port_bentrok(&folder)),
                    None => (None, 0),
                };
                let port_bentrok = u16::try_from(bentrok).ok().filter(|p| *p > 0);
                siar.kirim(Kabar::Node(StatusNode { path: folder, berjalan, url, port_bentrok }));
            }
            node_runner::Peristiwa::Url { folder, url } => {
                periksa_hsts(siar, &folder, &url);
                siar.say(Pesan::dengan("Proyek {0} siap di {1}", &[&nama_folder(&folder), &url]));
                siar.kirim(Kabar::Node(StatusNode { path: folder, berjalan: true, url: Some(url), port_bentrok: None }));
            }
        }
    }
}

/// Begitu proyek Node mengumumkan alamatnya, periksa apakah ia mengirim HSTS
/// untuk localhost. Di utas latar: pemeriksaannya menunggu jaringan sampai
/// beberapa detik, dan peristiwa ini datang dari utas pembaca keluaran.
fn periksa_hsts(siar: &Arc<Siar>, folder: &str, url: &str) {
    if !hsts::berlaku(url) {
        return;
    }
    let (siar, folder, url) = (siar.clone(), folder.to_string(), url.to_string());
    let _ = std::thread::Builder::new().name("phoron-hsts".into()).spawn(move || {
        let header = hsts::baca_header(&url);
        sampaikan_hsts(&siar, &folder, &url, header.as_deref());
    });
}

/// Dua tempat sekaligus, disengaja: log Aktivitas tersimpan ke phoron.log dan
/// bisa dibaca berhari-hari kemudian - saat gejalanya baru muncul; keluaran
/// proyek adalah yang sedang ditatap pengguna detik itu.
fn sampaikan_hsts(siar: &Siar, folder: &str, url: &str, header: Option<&str>) {
    let Some(keluhan) = hsts::keluhan(&nama_folder(folder), url, header) else { return };
    let node = siar.node();
    for p in keluhan {
        siar.say(p.clone());
        if let Some(n) = &node {
            n.sisipkan(folder, BarisNode::Pesan(p));
        }
    }
}

/// `Path.GetFileName(folder.TrimEnd('\\'))`.
fn nama_folder(folder: &str) -> String {
    let t = folder.trim_end_matches('\\');
    t.rsplit(['\\', '/']).next().unwrap_or(t).to_string()
}

/// `DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")` - waktu lokal untuk phoron.log.
fn cap_lokal_log() -> String {
    use windows_sys::Win32::Foundation::SYSTEMTIME;
    use windows_sys::Win32::System::SystemInformation::GetLocalTime;
    // SAFETY: GetLocalTime hanya mengisi struct yang kita berikan.
    let mut t: SYSTEMTIME = unsafe { std::mem::zeroed() };
    unsafe { GetLocalTime(&mut t) };
    format!(
        "{:04}-{:02}-{:02} {:02}:{:02}:{:02}",
        t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute, t.wSecond
    )
}

// ------------------------------------------------------------ paket

/// `SamaJalur`: tanpa garis miring terbalik di ujung, tidak peka besar-kecil.
fn sama_jalur(a: &str, b: &str) -> bool {
    sama_abaikan_besar(a.trim_end_matches('\\'), b.trim_end_matches('\\'))
}

/// Paket yang ditunjuk sebuah nilai profil: nama folder, kunci kembar, atau
/// jalur lengkap. Jalur yang tidak ada di komputer ini jatuh ke nama folder
/// terakhirnya, sehingga profil yang dibawa dari komputer lain tetap cocok.
pub fn find<'a>(paket: &'a [BinPackage], kind: BinKind, id: &str) -> Option<&'a BinPackage> {
    if id.is_empty() {
        return None;
    }
    let mut id = id;
    if id.contains(['\\', '/']) {
        if let Some(p) = paket.iter().find(|p| p.kind == kind && sama_jalur(&p.path, id)) {
            return Some(p);
        }
        // Path.GetFileName juga memotong di ':' (huruf drive).
        let t = id.trim_end_matches(['\\', '/']);
        id = t.rsplit(['\\', '/', ':']).next().unwrap_or("");
        if id.is_empty() {
            return None;
        }
    }
    // Kunci dulu: kembaran kedua ("mysql@C-xampp") hanya bisa ditunjuk lewat
    // kuncinya; yang pertama berkunci sama dengan nama foldernya.
    paket
        .iter()
        .find(|p| p.kind == kind && sama_abaikan_besar(p.kunci(), id))
        .or_else(|| paket.iter().find(|p| p.kind == kind && sama_abaikan_besar(&p.id, id)))
}

/// Versi tertinggi; saat seri yang PERTAMA dalam daftar menang
/// (`OrderByDescending(...).FirstOrDefault()`).
fn tertinggi<'a>(it: impl IntoIterator<Item = &'a BinPackage>) -> Option<&'a BinPackage> {
    let mut best: Option<&BinPackage> = None;
    for p in it {
        if best.is_none_or(|b| p.parsed() > b.parsed()) {
            best = Some(p);
        }
    }
    best
}

/// Paket untuk sebuah entri profil, DENGAN penyesuaian bila yang tercatat tidak
/// ada di komputer ini. `php_profil` = nilai PHP profil aktif (untuk memilih
/// Apache yang sepadan).
///
/// Kosong berarti TIDAK DIPAKAI dan dikembalikan kosong - bukan diganti versi
/// tertinggi yang kebetulan terpasang.
pub fn pakai(paket: &[BinPackage], kind: BinKind, id: &str, php_profil: &str) -> Option<BinPackage> {
    if id.is_empty() {
        return None;
    }
    if let Some(p) = find(paket, kind, id) {
        return Some(p.clone());
    }
    let tersedia: Vec<BinPackage> = paket.iter().filter(|p| p.kind == kind).cloned().collect();
    if tersedia.is_empty() {
        return None;
    }
    if kind == BinKind::Apache {
        // Lewat kecocokan toolset, bukan sekadar versi tertinggi: Apache VS16
        // dengan PHP VC11 mati seketika tanpa pesan.
        let php = if php_profil.is_empty() { None } else { pakai(paket, BinKind::Php, php_profil, php_profil) };
        return profile_store::pick_apache(php.as_ref(), &tersedia).cloned();
    }
    tertinggi(&tersedia).cloned()
}

/// Paket yang dipakai profil untuk tiap peran.
#[derive(Debug, Clone, Default)]
struct PaketProfil {
    php: Option<BinPackage>,
    apache: Option<BinPackage>,
    nginx: Option<BinPackage>,
    mysql: Option<BinPackage>,
}

impl PaketProfil {
    fn dari(paket: &[BinPackage], p: &Profile) -> Self {
        let f = |k, id: &str| pakai(paket, k, id, &p.php_id);
        PaketProfil {
            php: f(BinKind::Php, &p.php_id),
            apache: f(BinKind::Apache, &p.apache_id),
            nginx: f(BinKind::Nginx, &p.nginx_id),
            mysql: f(BinKind::MySql, &p.mysql_id),
        }
    }

    fn web(&self, p: &Profile) -> Option<&BinPackage> {
        if p.web_server == "nginx" { self.nginx.as_ref() } else { self.apache.as_ref() }
    }
}

/// Penyesuaian yang sedang berlaku, untuk dilaporkan: diam-diam memakai versi
/// lain lebih buruk daripada gagal terang-terangan.
pub fn penyesuaian(paket: &[BinPackage], p: &Profile) -> Vec<Pesan> {
    let mut pesan = Vec::new();
    let mut periksa = |kind: BinKind, id: &str, sebutan: &str| {
        if id.is_empty() || find(paket, kind, id).is_some() {
            return;
        }
        match pakai(paket, kind, id, &p.php_id) {
            None => pesan.push(Pesan::dengan(
                &format!(
                    "{sebutan} \"{{0}}\" yang dicatat profil tidak ada di komputer ini, dan tidak ada gantinya. \
                     Pasang versinya di halaman Versi, atau pilih yang lain di Profil."
                ),
                &[id],
            )),
            Some(g) => pesan.push(Pesan::dengan(
                &format!(
                    "{sebutan} \"{{0}}\" tidak ada di komputer ini; Phoron memakai \"{{1}}\". \
                     Simpan di halaman Profil kalau ingin penggantian ini tetap."
                ),
                &[id, &g.id],
            )),
        }
    };
    periksa(BinKind::Php, &p.php_id, "Versi PHP");
    if p.web_server == "nginx" {
        periksa(BinKind::Nginx, &p.nginx_id, "Versi Nginx");
    } else {
        periksa(BinKind::Apache, &p.apache_id, "Versi Apache");
    }
    periksa(BinKind::MySql, &p.mysql_id, "Versi MySQL");

    // Pilihan PHP per situs TIDAK dihapus dari profil: dibawa pulang ke
    // komputer asalnya, situs itu harus kembali ke versinya sendiri.
    for (folder, php) in &p.php_per_situs {
        if find(paket, BinKind::Php, php).is_none() {
            pesan.push(Pesan::dengan(
                "Situs {0} memakai PHP \"{1}\", yang tidak ada di komputer ini; sementara ia dilayani PHP profil.",
                &[&nama_folder(folder), php],
            ));
        }
    }
    pesan
}

/// Nama folder yang sama di lebih dari satu folder bin: yang terpakai adalah
/// yang pertama ditemukan, diam-diam, dan bisa berubah kalau urutan folder bin
/// diubah. Jalur lengkap tidak ambigu.
pub fn paket_kembar(paket: &[BinPackage], p: &Profile) -> Vec<Pesan> {
    let mut pesan = Vec::new();
    for (kind, id) in [
        (BinKind::Php, &p.php_id),
        (BinKind::Apache, &p.apache_id),
        (BinKind::Nginx, &p.nginx_id),
        (BinKind::MySql, &p.mysql_id),
    ] {
        if id.is_empty() || id.contains(['\\', '/']) {
            continue;
        }
        let cocok: Vec<&BinPackage> =
            paket.iter().filter(|x| x.kind == kind && sama_abaikan_besar(&x.id, id)).collect();
        if cocok.len() < 2 {
            continue;
        }
        let asal: Vec<&str> = cocok.iter().map(|c| c.source_root.as_str()).collect();
        pesan.push(Pesan::dengan(
            "Nama folder \"{0}\" ada di lebih dari satu folder bin ({1}). Yang dipakai adalah {2}; \
             pilih ulang di halaman Profil untuk mengunci salinan yang Anda maksud.",
            &[id, &asal.join(", "), &cocok[0].path],
        ));
    }
    pesan
}

/// Kolam php-cgi untuk situs yang memakai versi PHP sendiri - satu per VERSI.
pub fn susun_kolam(
    paket: &[BinPackage],
    profiles: &[Profile],
    aktif: &Profile,
    sites: &[Site],
    php_profil: Option<&BinPackage>,
) -> Vec<Kolam> {
    let mut hasil: Vec<Kolam> = Vec::new();
    for (folder, id) in &aktif.php_per_situs {
        // Folder yang sudah dihapus atau di luar folder proyek: tidak dilayani.
        let Some(situs) = sites.iter().find(|s| sama_jalur(&s.path, folder)) else { continue };
        let Some(php) = find(paket, BinKind::Php, id) else { continue }; // dilaporkan penyesuaian
        // Sama dengan PHP profil (lewat JALUR - dua paket bisa bernama sama).
        if php_profil.is_some_and(|pp| sama_jalur(&php.path, &pp.path)) {
            continue;
        }
        if let Some(k) = hasil.iter_mut().find(|k| sama_jalur(&k.php.path, &php.path)) {
            k.situs.push(situs.clone());
            continue;
        }
        let sumber = profiles.iter().find(|p| {
            !p.php_extensions.is_empty()
                && find(paket, BinKind::Php, &p.php_id).is_some_and(|x| sama_jalur(&x.path, &php.path))
        });
        let (ekstensi, timpa) = match sumber {
            Some(s) => (s.php_extensions.clone(), s.php_ini_overrides.clone()),
            None => (config::ekstensi_disarankan(Some(php)), Vec::new()),
        };
        hasil.push(Kolam::baru(php.clone(), ekstensi, timpa, vec![situs.clone()]));
    }
    hasil
}

/// Profil bawaan bila profil aktif tidak tersimpan: PHP paling baru.
pub fn profil_bawaan<'a>(profiles: &'a [Profile], paket: &[BinPackage]) -> Option<&'a Profile> {
    let versi = |p: &Profile| find(paket, BinKind::Php, &p.php_id).map(|x| x.parsed()).unwrap_or(Versi::NOL);
    let mut best: Option<&Profile> = None;
    for p in profiles {
        if best.is_none_or(|b| versi(p) > versi(b)) {
            best = Some(p);
        }
    }
    best
}

/// Layanan jalan dengan config yang sudah berubah di cakram sejak ia menyala.
pub fn perlu_restart(state: ServiceState, versi_nyala: u64, versi_kini: u64) -> bool {
    state == ServiceState::Jalan && versi_nyala != BELUM_NYALA && versi_nyala != versi_kini
}

/// Galat hosts seperti di Engine.SyncHosts: tanpa hak -> pesan Administrator;
/// galat lain dibungkus "Gagal menyunting hosts: ".
pub fn pesan_galat_hosts(e: &hosts::GalatHosts) -> Pesan {
    match e {
        hosts::GalatHosts::TidakBerhak | hosts::GalatHosts::Tulis(_) => e.pesan(),
        lain => {
            let p = lain.pesan();
            Pesan { kunci: format!("Gagal menyunting hosts: {}", p.kunci), args: p.args }
        }
    }
}

/// `Pesan` dengan awalan teks; argumennya ikut.
fn awali(awalan: &str, p: &Pesan) -> Pesan {
    Pesan { kunci: format!("{awalan}{}", p.kunci), args: p.args.clone() }
}

// ------------------------------------------------------------ sambutan

/// Halaman sambutan untuk folder www yang masih kosong. Sintaks PHP-nya sah
/// sampai PHP 5.6 (isset(), bukan "??").
pub const HALAMAN_SAMBUTAN: &str = "<?php\n\
$folders = array_filter(scandir(__DIR__), function ($f) {\n\
\x20   return $f[0] !== '.' && is_dir(__DIR__ . DIRECTORY_SEPARATOR . $f);\n\
});\n\
?>\n\
<!doctype html>\n\
<html lang=\"id\">\n\
<head>\n\
<meta charset=\"utf-8\">\n\
<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n\
<title>Phoron</title>\n\
<style>\n\
\x20 :root { color-scheme: light dark; }\n\
\x20 body { font: 15px/1.6 system-ui, 'Segoe UI', sans-serif; margin: 0;\n\
\x20        display: grid; place-items: center; min-height: 100vh;\n\
\x20        background: #f6f6f8; color: #1a1a1c; }\n\
\x20 @media (prefers-color-scheme: dark) { body { background: #17171a; color: #ececf0; } }\n\
\x20 .kotak { width: min(620px, 90vw); padding: 32px; border-radius: 14px;\n\
\x20          background: #fff; box-shadow: 0 2px 24px rgba(0,0,0,.08); }\n\
\x20 @media (prefers-color-scheme: dark) { .kotak { background: #222226; box-shadow: none; } }\n\
\x20 h1 { margin: 0 0 4px; font-size: 28px; }\n\
\x20 .sub { opacity: .65; margin: 0 0 22px; }\n\
\x20 dl { display: grid; grid-template-columns: max-content 1fr; gap: 6px 18px; margin: 0 0 22px; }\n\
\x20 dt { opacity: .6; } dd { margin: 0; font-variant-numeric: tabular-nums; }\n\
\x20 ul { margin: 0; padding-left: 20px; }\n\
\x20 a { color: #2563eb; }\n\
</style>\n\
</head>\n\
<body>\n\
<div class=\"kotak\">\n\
\x20 <h1>Phoron</h1>\n\
\x20 <p class=\"sub\">Server lokal berjalan.</p>\n\
\x20 <dl>\n\
\x20   <dt>PHP</dt><dd><?= PHP_VERSION ?> (<?= php_sapi_name() ?>)</dd>\n\
\x20   <dt>Server</dt><dd><?= isset($_SERVER['SERVER_SOFTWARE']) ? $_SERVER['SERVER_SOFTWARE'] : '-' ?></dd>\n\
\x20   <dt>Document root</dt><dd><?= htmlspecialchars(__DIR__) ?></dd>\n\
\x20 </dl>\n\
\x20 <?php if ($folders): ?>\n\
\x20   <p><strong>Proyek di folder ini</strong></p>\n\
\x20   <ul>\n\
\x20     <?php foreach ($folders as $f): ?>\n\
\x20       <li><a href=\"/<?= rawurlencode($f) ?>/\"><?= htmlspecialchars($f) ?></a></li>\n\
\x20     <?php endforeach; ?>\n\
\x20   </ul>\n\
\x20 <?php else: ?>\n\
\x20   <p>Belum ada proyek. Buat satu lewat halaman <em>Situs</em> di Phoron,\n\
\x20      atau salin folder proyek ke sini.</p>\n\
\x20 <?php endif; ?>\n\
</div>\n\
</body>\n\
</html>\n";

/// Tulis halaman sambutan hanya bila folder www benar-benar kosong. Kalau
/// sudah ada isinya - termasuk bila pengguna menghapus halaman ini - jangan
/// pernah menaruh berkas di folder kerja orang lain.
fn pastikan_halaman_sambutan(p: &Profile) {
    let root = situs::document_root(Some(p));
    let Ok(mut isi) = std::fs::read_dir(&root) else { return };
    if isi.next().is_some() {
        return;
    }
    let _ = std::fs::write(Path::new(&root).join("index.php"), HALAMAN_SAMBUTAN);
}

/// Situs profil beserta catatannya. Tanpa Virtual Host, catatan pemindai (yang
/// seluruhnya tentang nama host) dibuang.
fn pindai_situs(p: Option<&Profile>, auto_vhost: bool) -> (Vec<Site>, Vec<Pesan>) {
    let Some(p) = p else { return (Vec::new(), Vec::new()) };
    let mut w = Vec::new();
    let s = situs::scan(Some(p), &hosts::all_names(), &mut w);
    if !auto_vhost {
        w.clear();
    }
    (s, w)
}

// ------------------------------------------------------------ Engine

struct Data {
    settings: Settings,
    packages: Vec<BinPackage>,
    profiles: Vec<Profile>,
    active: Option<Profile>,
    sites: Vec<Site>,
    site_warnings: Vec<Pesan>,
    node_apps: Vec<NodeApp>,
    /// Peringatan Terapkan terakhir (Engine.LastBuild.Warnings).
    peringatan_terapkan: Vec<Pesan>,
    masalah_setelan: Vec<Pesan>,
    masalah_profil: Vec<Pesan>,
}

impl Data {
    fn masalah_muat(&self) -> Vec<Pesan> {
        self.masalah_setelan.iter().chain(self.masalah_profil.iter()).cloned().collect()
    }
}

struct Inti {
    data: RwLock<Data>,
    siar: Arc<Siar>,
    layanan: Layanan,
    node: NodeRunner,
    /// Menertibkan Terapkan, Muat Ulang, dan pergantian profil aktif:
    /// Terapkan dan Muat Ulang memotret data, bekerja di luar kunci data, lalu
    /// menulis kembali.
    kunci_terapkan: Mutex<()>,
    /// Ganti profil (tulis) terhadap start dan Terapkan (baca): profil aktif
    /// tidak pernah berganti di tengah start, dan satu ganti profil dalam
    /// satu waktu. Stop TIDAK memakainya - stop harus bisa memotong start.
    /// Urutan kunci: kunci_profil -> kunci_terapkan -> data.
    kunci_profil: RwLock<()>,
    /// Gerbang: sesudah `tutup` start baru dan ganti profil ditolak.
    tertutup: AtomicBool,
    versi_web_nyala: AtomicU64,
    versi_db_nyala: AtomicU64,
    /// Peringatan paket kembar yang sudah dicatat di sesi ini.
    kembar_tercatat: Mutex<HashSet<String>>,
}

impl Drop for Inti {
    fn drop(&mut self) {
        *kunci(&self.siar.node) = None;
    }
}

/// Pusat orkestrasi. Murah di-clone; semua salinan berbagi keadaan yang sama.
#[derive(Clone)]
pub struct Engine {
    inti: Arc<Inti>,
}

fn muat_setelan() -> (Settings, Vec<Pesan>) {
    let h = Settings::muat();
    let masalah = h.keluhan.iter().map(|k| Pesan::dengan("phoron.ini: {0}", &[k])).collect();
    let mut s = h.setelan;
    // Ditulis sejak jalan pertama: berkas ini juga penanda akar instalasi, dan
    // tanpanya exe yang dipindah ke subfolder menebak akar yang salah.
    if !paths::settings_file().exists() {
        let _ = s.save();
    }
    (s, masalah)
}

/// Pindahkan medan publik setelan dari UI ke setelan yang sedang dipegang;
/// keadaan internal (sandi tersandi saat muat, asal berkas) dipertahankan.
///
/// `active_profile` dan `situs_terakhir` TIDAK ikut: keduanya milik
/// `ganti_profil` dan `catat_situs_dibuka`. Salinan UI yang basi dulu
/// menimpa perubahan dari baki - dan profil aktif yang berbeda membuat muat
/// ulang berpindah profil tanpa mematikan atau menerapkan ulang layanan.
fn salin_setelan(ke: &mut Settings, dari: Settings) {
    ke.bin_roots = dari.bin_roots;
    ke.auto_start_services = dari.auto_start_services;
    ke.minimize_to_tray = dari.minimize_to_tray;
    ke.auto_vhost = dari.auto_vhost;
    ke.opcache = dari.opcache;
    ke.manage_hosts = dari.manage_hosts;
    ke.php_ini_ke_folder_php = dari.php_ini_ke_folder_php;
    ke.log_rinci = dari.log_rinci;
    ke.beranda_di_akar = dari.beranda_di_akar;
    ke.tema = dari.tema;
    ke.jendela_lebar = dari.jendela_lebar;
    ke.jendela_tinggi = dari.jendela_tinggi;
    ke.bahasa = dari.bahasa;
    ke.cek_pembaruan = dari.cek_pembaruan;
    ke.cek_terakhir = dari.cek_terakhir;
    ke.terminal = dari.terminal;
    ke.editor = dari.editor;
    ke.db_pengguna = dari.db_pengguna;
    ke.heidi_sql = dari.heidi_sql;
    ke.db_sandi = dari.db_sandi;
}

fn galat_io(kunci_pesan: &str, e: &std::io::Error) -> Pesan {
    Pesan::dengan(kunci_pesan, &[&e.to_string()])
}

impl Engine {
    /// Muat setelan, pindai paket, muat profil (membuatkannya pada jalan
    /// pertama), dan pindai situs. Semua kabar dikirim ke `penerima`.
    pub fn baru(penerima: impl Fn(Kabar) + Send + Sync + 'static) -> Engine {
        let siar = Arc::new(Siar {
            penerima: Arc::new(penerima),
            riwayat: Mutex::new(VecDeque::new()),
            node: Mutex::new(None),
        });
        let (settings, masalah_setelan) = muat_setelan();
        let s1 = siar.clone();
        let layanan = Layanan::baru(move |p| s1.dari_layanan(p));
        layanan.set_log_rinci(settings.log_rinci);
        let s2 = siar.clone();
        let node = NodeRunner::baru(move |p| Siar::dari_node(&s2, p));
        *kunci(&siar.node) = Some(node.clone());
        let e = Engine {
            inti: Arc::new(Inti {
                data: RwLock::new(Data {
                    settings,
                    packages: Vec::new(),
                    profiles: Vec::new(),
                    active: None,
                    sites: Vec::new(),
                    site_warnings: Vec::new(),
                    node_apps: Vec::new(),
                    peringatan_terapkan: Vec::new(),
                    masalah_setelan,
                    masalah_profil: Vec::new(),
                }),
                siar,
                layanan,
                node,
                kunci_terapkan: Mutex::new(()),
                kunci_profil: RwLock::new(()),
                tertutup: AtomicBool::new(false),
                versi_web_nyala: AtomicU64::new(BELUM_NYALA),
                versi_db_nyala: AtomicU64::new(BELUM_NYALA),
                kembar_tercatat: Mutex::new(HashSet::new()),
            }),
        };
        e.muat_ulang();
        e
    }

    /// Engine yang kabarnya dikirim ke channel.
    pub fn dengan_saluran() -> (Engine, mpsc::Receiver<Kabar>) {
        let (tx, rx) = mpsc::channel();
        (Engine::baru(move |k| drop(tx.send(k))), rx)
    }

    fn baca(&self) -> RwLockReadGuard<'_, Data> {
        self.inti.data.read().unwrap_or_else(|e| e.into_inner())
    }

    fn tulis(&self) -> RwLockWriteGuard<'_, Data> {
        self.inti.data.write().unwrap_or_else(|e| e.into_inner())
    }

    /// Kunci profil untuk start dan Terapkan (bersama).
    fn profil_baca(&self) -> RwLockReadGuard<'_, ()> {
        self.inti.kunci_profil.read().unwrap_or_else(|e| e.into_inner())
    }

    /// Kunci profil untuk ganti profil (sendiri).
    fn profil_tulis(&self) -> RwLockWriteGuard<'_, ()> {
        self.inti.kunci_profil.write().unwrap_or_else(|e| e.into_inner())
    }

    /// Start baru dan ganti profil ditolak sesudah `tutup`.
    fn gerbang(&self) -> Result<(), Pesan> {
        if self.inti.tertutup.load(Ordering::SeqCst) {
            return Err(Pesan::baru("Phoron sedang ditutup."));
        }
        Ok(())
    }

    pub fn layanan(&self) -> &Layanan {
        &self.inti.layanan
    }

    pub fn node(&self) -> &NodeRunner {
        &self.inti.node
    }

    // -------------------------------------------------------- log

    /// Catat satu baris log Aktivitas (riwayat, kabar, dan logs\phoron.log).
    pub fn say(&self, p: Pesan) {
        self.inti.siar.say(p);
    }

    /// Salinan riwayat log, terlama lebih dulu.
    pub fn riwayat_log(&self) -> Vec<BarisLogJarak> {
        kunci(&self.inti.siar.riwayat).iter().cloned().collect()
    }

    // -------------------------------------------------------- data

    pub fn settings(&self) -> Settings {
        self.baca().settings.clone()
    }

    pub fn packages(&self) -> Vec<BinPackage> {
        self.baca().packages.clone()
    }

    pub fn profiles(&self) -> Vec<Profile> {
        self.baca().profiles.clone()
    }

    pub fn active(&self) -> Option<Profile> {
        self.baca().active.clone()
    }

    pub fn sites(&self) -> Vec<Site> {
        self.baca().sites.clone()
    }

    /// Catatan pemindaian situs terakhir (folder hilang, nama bentrok).
    pub fn site_warnings(&self) -> Vec<Pesan> {
        self.baca().site_warnings.clone()
    }

    pub fn node_apps(&self) -> Vec<NodeApp> {
        self.baca().node_apps.clone()
    }

    pub fn masalah_muat(&self) -> Vec<Pesan> {
        self.baca().masalah_muat()
    }

    pub fn peringatan_terapkan(&self) -> Vec<Pesan> {
        self.baca().peringatan_terapkan.clone()
    }

    /// Paket yang ditunjuk nilai profil, tanpa penyesuaian.
    pub fn find(&self, kind: BinKind, id: &str) -> Option<BinPackage> {
        find(&self.baca().packages, kind, id).cloned()
    }

    /// Paket untuk nilai profil aktif, dengan penyesuaian.
    pub fn pakai(&self, kind: BinKind, id: &str) -> Option<BinPackage> {
        let d = self.baca();
        let php = d.active.as_ref().map(|a| a.php_id.as_str()).unwrap_or("");
        pakai(&d.packages, kind, id, php)
    }

    fn paket_aktif(&self) -> (Option<Profile>, PaketProfil) {
        let d = self.baca();
        match &d.active {
            Some(a) => (Some(a.clone()), PaketProfil::dari(&d.packages, a)),
            None => (None, PaketProfil::default()),
        }
    }

    pub fn php(&self) -> Option<BinPackage> {
        self.paket_aktif().1.php
    }

    pub fn apache(&self) -> Option<BinPackage> {
        self.paket_aktif().1.apache
    }

    pub fn nginx(&self) -> Option<BinPackage> {
        self.paket_aktif().1.nginx
    }

    pub fn mysql(&self) -> Option<BinPackage> {
        self.paket_aktif().1.mysql
    }

    /// Web server profil aktif sesuai pilihan Apache/Nginx.
    pub fn web_package(&self) -> Option<BinPackage> {
        let (a, pk) = self.paket_aktif();
        a.and_then(|a| pk.web(&a).cloned())
    }

    /// Penyesuaian paket profil aktif yang sedang berlaku.
    pub fn penyesuaian(&self) -> Vec<Pesan> {
        let d = self.baca();
        d.active.as_ref().map(|a| penyesuaian(&d.packages, a)).unwrap_or_default()
    }

    /// PHP yang sungguh melayani situs ini: pilihannya sendiri bila ada dan
    /// terpasang, selain itu PHP profil.
    pub fn php_untuk(&self, situs_path: &str) -> Option<BinPackage> {
        let d = self.baca();
        let a = d.active.as_ref()?;
        for (folder, id) in &a.php_per_situs {
            if sama_jalur(folder, situs_path) {
                if let Some(p) = find(&d.packages, BinKind::Php, id) {
                    return Some(p.clone());
                }
            }
        }
        pakai(&d.packages, BinKind::Php, &a.php_id, &a.php_id)
    }

    // -------------------------------------------------------- muat

    /// Baca ulang profil dan apps.ini, lalu pindai ulang paket dan situs. Aman
    /// dipanggil berkali-kali.
    ///
    /// phoron.ini TIDAK dibaca ulang: daemon satu-satunya penulisnya, jadi
    /// setelan di memori adalah sumber kebenaran (dibaca dari cakram hanya
    /// saat Engine dibuat). Dulu muat ulang membacanya lagi di luar kunci
    /// data dan menimpa perubahan dari ganti profil atau situs terakhir yang
    /// datang di sela waktu itu - dan berkas yang sesaat terkunci membuat
    /// setelan kembali ke bawaan.
    pub fn muat_ulang(&self) {
        // Profil aktif hanya berganti di bawah kunci ini (ganti_profil), jadi
        // pilihan di bawah tidak bisa basi.
        let _g = kunci(&self.inti.kunci_terapkan);
        let settings = self.settings();
        self.inti.layanan.set_log_rinci(settings.log_rinci);
        let packages = bin::scan_all(&settings.bin_roots);
        let apps = node_apps::load_all();
        let mut keluhan = Vec::new();
        let mut profiles = profile_store::load_all(Some(&mut keluhan));
        if profiles.is_empty() && packages.iter().any(|p| p.kind == BinKind::Php) {
            self.say(Pesan::baru("Belum ada profil - membuatkan satu profil per versi PHP yang ditemukan."));
            profiles = profile_store::seed(&packages);
        }
        let active = profiles
            .iter()
            .find(|p| p.file_name == settings.active_profile)
            .or_else(|| profil_bawaan(&profiles, &packages))
            .cloned();
        let (sites, sw) = pindai_situs(active.as_ref(), settings.auto_vhost);
        {
            let mut d = self.tulis();
            d.packages = packages;
            d.node_apps = apps;
            // Keluhan profil lama dibuang dulu: tidak menumpuk tiap muat ulang.
            d.masalah_profil = keluhan.iter().map(|k| Pesan::dengan("{0}", &[k])).collect();
            d.profiles = profiles;
            d.active = active;
            d.sites = sites;
            d.site_warnings = sw;
        }
        self.inti.siar.kirim(Kabar::PerluMuatUlang);
    }

    // -------------------------------------------------------- terapkan

    /// Tulis ulang config profil aktif tanpa menyentuh layanan. Hasil:
    /// peringatan (juga disimpan untuk `Keadaan::peringatan_terapkan`).
    pub fn terapkan(&self) -> Result<Vec<Pesan>, Pesan> {
        let _p = self.profil_baca();
        Ok(match self.terapkan_inti()? {
            Some((_, _, w)) => w,
            None => vec![Pesan::baru("Belum ada profil.")],
        })
    }

    /// Terapkan; hasilnya profil yang diterapkan, hasil build, dan peringatan.
    /// `None` bila belum ada profil. Pemanggil memegang `kunci_profil`.
    fn terapkan_inti(&self) -> Result<Option<(Profile, Hasil, Vec<Pesan>)>, Pesan> {
        let _g = kunci(&self.inti.kunci_terapkan);
        let (settings, packages, profiles, active, masalah) = {
            let d = self.baca();
            (d.settings.clone(), d.packages.clone(), d.profiles.clone(), d.active.clone(), d.masalah_muat())
        };
        let Some(mut active) = active else { return Ok(None) };
        pastikan_halaman_sambutan(&active);
        let pk = PaketProfil::dari(&packages, &active);
        let awal = self.pastikan_sertifikat(&active, pk.apache.as_ref(), &profiles);
        let (sites, sw) = pindai_situs(Some(&active), settings.auto_vhost);
        self.inti.layanan.set_log_rinci(settings.log_rinci);
        let kolam = susun_kolam(&packages, &profiles, &active, &sites, pk.php.as_ref());
        let opsi = Opsi {
            php_ini_ke_folder_php: settings.php_ini_ke_folder_php,
            log_akses: settings.log_rinci,
            beranda_di_akar: settings.beranda_di_akar,
            opcache: settings.opcache,
        };
        let situs_vhost: &[Site] = if settings.auto_vhost { &sites } else { &[] };
        let mut hasil = config::build(
            &active,
            pk.php.as_ref(),
            pk.apache.as_ref(),
            pk.mysql.as_ref(),
            pk.nginx.as_ref(),
            situs_vhost,
            &opsi,
            kolam,
        )
        .map_err(|e| galat_io("Konfigurasi tidak bisa ditulis: {0}", &e))?;

        // Masalah folder proyek dan muat disampaikan bersama peringatan
        // config - kalau tidak, satu folder salah ketik hanya berwujud situs
        // yang hilang dari daftar tanpa sebab yang terlihat.
        let mut w = std::mem::take(&mut hasil.peringatan);
        w.extend(masalah);
        let keluhan = kunci(&settings::KELUHAN_TERAKHIR).clone();
        if !keluhan.is_empty() {
            w.push(Pesan::dengan("{0}", &[&keluhan]));
        }
        w.extend(sw);
        w.extend(awal);
        let kembar = paket_kembar(&packages, &active);
        w.extend(kembar.iter().cloned());
        w.extend(penyesuaian(&packages, &active));

        // Ekstensi yang diambil alih dari php.ini dasar disimpan ke profil:
        // terlihat dan bisa disunting, dan tidak berubah lagi kalau berkas
        // dasarnya kelak ikut berubah.
        let diambil = hasil.ekstensi_diambil.clone().filter(|e| !e.is_empty());
        if let Some(ext) = &diambil {
            active.php_extensions = ext.clone();
            // profiles\*.ini milik UI: yang diubah hanya daftar ekstensinya,
            // di atas isi berkas TERKINI. Menyimpan salinan memori menimpa
            // suntingan UI yang muat ulangnya belum diproses.
            let berkas = paths::profiles().join(format!("{}.ini", active.file_name));
            let mut simpan = profile_store::load(&berkas).unwrap_or_else(|_| active.clone());
            simpan.php_extensions = ext.clone();
            profile_store::save(&mut simpan).map_err(|e| galat_io("Profil tidak bisa disimpan: {0}", &e))?;
            self.say(Pesan::dengan(
                "Profil \"{0}\" mengambil alih {1} ekstensi dari php.ini yang sudah ada: {2}.",
                &[&active.name, &ext.len().to_string(), &ext.join(", ")],
            ));
        }
        if settings.manage_hosts {
            // Tanpa Virtual Host blok Phoron dikosongkan, bukan dibiarkan.
            let nama: Vec<&str> = if settings.auto_vhost {
                sites.iter().map(|s| s.host_name.as_str()).filter(|n| !n.trim().is_empty() && *n != "localhost").collect()
            } else {
                Vec::new()
            };
            if let Err(e) = hosts::sync(nama) {
                w.push(pesan_galat_hosts(&e));
            }
        }
        // Dipindai ulang SESUDAH vhost dan hosts ditulis - sekali, di sini.
        let (sites, sw) = pindai_situs(Some(&active), settings.auto_vhost);
        hasil.peringatan = w.clone();
        {
            let mut d = self.tulis();
            // Ganti profil yang menyelinap: hasil profil lama tidak menimpa.
            if d.active.as_ref().is_some_and(|a| a.file_name == active.file_name) {
                if diambil.is_some() {
                    if let Some(p) = d.profiles.iter_mut().find(|p| p.file_name == active.file_name) {
                        p.php_extensions = active.php_extensions.clone();
                    }
                    d.active = Some(active.clone());
                }
                d.sites = sites;
                d.site_warnings = sw;
                d.peringatan_terapkan = w.clone();
            }
        }
        self.catat_peringatan(&w, &kembar);
        self.say(Pesan::dengan("Konfigurasi profil \"{0}\" ditulis ulang.", &[&active.name]));
        Ok(Some((active, hasil, w)))
    }

    /// Catat peringatan ke log. Paket kembar adalah keadaan komputer, bukan
    /// kejadian: cukup sekali per sesi (daftar peringatan tetap memuatnya).
    fn catat_peringatan(&self, w: &[Pesan], kembar: &[Pesan]) {
        for p in w {
            if kembar.contains(p) && !kunci(&self.inti.kembar_tercatat).insert(format!("{p:?}")) {
                continue;
            }
            self.say(awali("Peringatan: ", p));
        }
    }

    /// Buat sertifikat sekali bila belum ada. Tanpanya HTTPS mati total dan
    /// browser hanya bilang "tidak dapat tersambung".
    fn pastikan_sertifikat(&self, p: &Profile, apache: Option<&BinPackage>, profiles: &[Profile]) -> Vec<Pesan> {
        let lain: Vec<&str> = profiles.iter().map(|x| x.site_suffix.as_str()).collect();
        let akhiran = situs::suffix(Some(p));
        match ssl::pastikan(apache, &p.web_server, &p.site_suffix, &lain) {
            ssl::HasilPastikan::TidakPerlu => Vec::new(),
            ssl::HasilPastikan::Gagal(e) => vec![awali("HTTPS tidak aktif - ", &e)],
            ssl::HasilPastikan::CaDiganti => vec![Pesan::dengan(
                "CA HTTPS Phoron dibuat ulang supaya hanya sah untuk akhiran situs Phoron (*.{0}) dan localhost. \
                 Percayai sertifikatnya lagi lewat Beranda.",
                &[&akhiran],
            )],
            ssl::HasilPastikan::Dibuat => {
                self.say(Pesan::dengan(
                    "Sertifikat HTTPS dibuat untuk *.{0} (pasang ke Trusted Root lewat Beranda agar browser tidak memperingatkan).",
                    &[&akhiran],
                ));
                Vec::new()
            }
        }
    }

    // -------------------------------------------------------- profil

    /// Jadikan profil aktif: layanan yang hidup (termasuk yang MASIH menyala)
    /// dimatikan dulu, config ditulis ulang, lalu dinyalakan lagi bila
    /// `restart`. Hanya satu ganti profil dalam satu waktu.
    ///
    /// Diurutkan terhadap start dan Terapkan dari utas lain (`kunci_profil`):
    /// dulu "Nyalakan web" dari baki yang sudah lolos Terapkan profil LAMA
    /// menyalakan httpd lama sesudah ganti, dan restart milik ganti melihatnya
    /// Jalan lalu membiarkannya.
    pub fn ganti_profil(&self, file_name: &str, restart: bool) -> Result<Vec<Pesan>, Pesan> {
        self.gerbang()?;
        if !self.baca().profiles.iter().any(|p| p.file_name == file_name) {
            return Err(Pesan::dengan("Profil {0} tidak ditemukan.", &[file_name]));
        }
        // Dimatikan SEBELUM menunggu kunci: start yang sedang berjalan
        // (MySQL yang memulihkan InnoDB bisa sampai lima menit) dibatalkan,
        // bukan ditunggu sampai selesai.
        let mut sudah_bilang = false;
        let (mut web_hidup, mut db_hidup) = self.matikan_untuk_ganti(&mut sudah_bilang);
        let _p = self.profil_tulis();
        self.gerbang()?;
        // Start yang menyelinap sebelum kunci didapat: dimatikan juga.
        let (w2, d2) = self.matikan_untuk_ganti(&mut sudah_bilang);
        web_hidup |= w2;
        db_hidup |= d2;

        let simpan = {
            let _t = kunci(&self.inti.kunci_terapkan);
            let mut d = self.tulis();
            let Some(baru) = d.profiles.iter().find(|p| p.file_name == file_name).cloned() else {
                return Err(Pesan::dengan("Profil {0} tidak ditemukan.", &[file_name]));
            };
            d.active = Some(baru);
            d.settings.active_profile = file_name.to_string();
            d.settings.save()
        };
        // Layanan tidak boleh tertinggal mati: config yang gagal ditulis
        // tidak menghentikan upaya menyalakan ulang (start menulis config-nya
        // sendiri dan melaporkan kegagalannya), dan MySQL yang gagal tidak
        // menghalangi web server.
        let mut galat: Option<Pesan> = None;
        let mut w = match self.terapkan_inti() {
            Ok(Some((_, _, w))) => w,
            Ok(None) => Vec::new(),
            Err(e) => {
                galat = Some(e);
                Vec::new()
            }
        };
        if let Err(e) = simpan {
            w.push(galat_io("Setelan tidak bisa disimpan: {0}", &e));
        }
        if restart {
            if db_hidup {
                if let Err(e) = self.nyalakan_db_inti() {
                    galat.get_or_insert(e);
                }
            }
            if web_hidup {
                if let Err(e) = self.nyalakan_web_inti() {
                    galat.get_or_insert(e);
                }
            }
        }
        self.inti.siar.kirim(Kabar::PerluMuatUlang);
        match galat {
            Some(e) => Err(e),
            None => Ok(w),
        }
    }

    /// Matikan layanan yang hidup (termasuk yang MASIH menyala) untuk ganti
    /// profil. Hasil: (web hidup, MySQL hidup) sebelum dimatikan.
    fn matikan_untuk_ganti(&self, sudah_bilang: &mut bool) -> (bool, bool) {
        let l = &self.inti.layanan;
        let web = l.web_state().hidup();
        let db = l.db_state().hidup();
        if (web || db) && !*sudah_bilang {
            *sudah_bilang = true;
            self.say(Pesan::baru("Mematikan layanan sebelum ganti profil..."));
        }
        if db {
            self.matikan_db();
        }
        if web {
            l.stop_web();
        }
        (web, db)
    }

    // -------------------------------------------------------- layanan

    /// Nyalakan web server profil aktif. Config SELALU dibangun ulang dulu.
    pub fn nyalakan_web(&self) -> Result<bool, Pesan> {
        self.gerbang()?;
        let _p = self.profil_baca();
        self.nyalakan_web_inti()
    }

    /// `nyalakan_web` tanpa kunci profil - pemanggil memegangnya.
    fn nyalakan_web_inti(&self) -> Result<bool, Pesan> {
        self.gerbang()?;
        let Some(a) = self.active() else { return Ok(false) };
        // Tidak memakai web server adalah pilihan, bukan kegagalan: tidak ada
        // titik merah untuk profil yang sengaja hanya menjalankan MySQL.
        if !a.pakai_web() {
            self.say(Pesan::dengan(
                "Profil \"{0}\" tidak memakai web server, jadi tidak ada yang dinyalakan.",
                &[&a.name],
            ));
            return Ok(false);
        }
        // Sudah jalan: start langsung berhasil tanpa restart, jadi server
        // masih memakai config lama - penanda "perlu restart" tidak boleh
        // padam karenanya.
        let sudah_jalan = self.inti.layanan.web_state() == ServiceState::Jalan;
        let Some((a, hasil, _)) = self.terapkan_inti()? else { return Ok(false) };
        let pk = PaketProfil::dari(&self.baca().packages, &a);
        let versi = tulis::versi_konfig_web();
        let ok = self.inti.layanan.start_web(&a, pk.web(&a), pk.php.as_ref(), &hasil);
        if ok && !sudah_jalan {
            self.inti.versi_web_nyala.store(versi, Ordering::SeqCst);
        }
        Ok(ok)
    }

    pub fn matikan_web(&self) {
        self.inti.layanan.stop_web();
    }

    /// Nyalakan MySQL profil aktif. Config SELALU dibangun ulang dulu.
    pub fn nyalakan_db(&self) -> Result<bool, Pesan> {
        self.gerbang()?;
        let _p = self.profil_baca();
        self.nyalakan_db_inti()
    }

    /// `nyalakan_db` tanpa kunci profil - pemanggil memegangnya.
    fn nyalakan_db_inti(&self) -> Result<bool, Pesan> {
        self.gerbang()?;
        let Some(a) = self.active() else { return Ok(false) };
        if !a.pakai_mysql() {
            self.say(Pesan::dengan("Profil \"{0}\" tidak memakai MySQL, jadi tidak ada yang dinyalakan.", &[&a.name]));
            return Ok(false);
        }
        let sudah_jalan = self.inti.layanan.db_state() == ServiceState::Jalan;
        let Some((a, _, _)) = self.terapkan_inti()? else { return Ok(false) };
        let pk = PaketProfil::dari(&self.baca().packages, &a);
        let versi = tulis::versi_konfig_db();
        let ok = self.inti.layanan.start_db(&a, pk.mysql.as_ref());
        if ok && !sudah_jalan {
            self.inti.versi_db_nyala.store(versi, Ordering::SeqCst);
        }
        Ok(ok)
    }

    /// Matikan MySQL dengan rapi.
    pub fn matikan_db(&self) {
        let (a, pk) = self.paket_aktif();
        self.inti.layanan.stop_db(a.as_ref(), pk.mysql.as_ref());
    }

    pub fn nyalakan_semua(&self) -> Result<(), Pesan> {
        self.gerbang()?;
        // Satu kunci untuk keduanya: kunci baca yang diambil dua kali di
        // utas yang sama bisa buntu bila ada ganti profil yang menunggu.
        let _p = self.profil_baca();
        self.nyalakan_db_inti()?;
        self.nyalakan_web_inti()?;
        Ok(())
    }

    pub fn matikan_semua(&self) {
        self.matikan_web();
        self.matikan_db();
    }

    /// Daemon berhenti. Gerbang ditutup dulu (start dan ganti profil baru
    /// ditolak), start yang sedang berjalan dibatalkan, ganti profil dan start
    /// yang masih berbenah ditunggu sampai `BATAS_TUTUP`, baru semuanya
    /// dimatikan: web server dan php-cgi dibunuh, MySQL dimatikan RAPI, semua
    /// proyek Node dihentikan.
    ///
    /// Dulu tutup tidak menunggu siapa pun: mysqld yang lahir sesudah stop
    /// dibunuh paksa oleh Job saat daemon keluar, init MySQL tertinggal
    /// yatim, dan ganti profil yang berpapasan menyalakan layanan lagi.
    pub fn tutup(&self) {
        self.inti.tertutup.store(true, Ordering::SeqCst);
        let l = &self.inti.layanan;
        l.batalkan_start();
        let mulai = Instant::now();
        // Ganti profil dan start memegang kunci profil selama bekerja.
        loop {
            match self.inti.kunci_profil.try_write() {
                Ok(_) | Err(std::sync::TryLockError::Poisoned(_)) => break,
                Err(std::sync::TryLockError::WouldBlock) => {}
            }
            if mulai.elapsed() >= BATAS_TUTUP {
                break;
            }
            std::thread::sleep(Duration::from_millis(50));
        }
        l.tunggu_start(BATAS_TUTUP.saturating_sub(mulai.elapsed()));
        l.stop_all();
        self.inti.node.stop_all();
    }

    /// Web server jalan, tapi config-nya sudah berubah di cakram sejak menyala.
    pub fn perlu_restart_web(&self) -> bool {
        perlu_restart(
            self.inti.layanan.web_state(),
            self.inti.versi_web_nyala.load(Ordering::SeqCst),
            tulis::versi_konfig_web(),
        )
    }

    pub fn perlu_restart_db(&self) -> bool {
        perlu_restart(
            self.inti.layanan.db_state(),
            self.inti.versi_db_nyala.load(Ordering::SeqCst),
            tulis::versi_konfig_db(),
        )
    }

    /// Port profil yang dipegang sisa proses jenis milik Phoron.
    pub fn sisa_proses(&self) -> Vec<Usage> {
        match self.active() {
            Some(a) => self.inti.layanan.sisa_proses(&a, ssl::exists()),
            None => Vec::new(),
        }
    }

    /// Hentikan sisa proses itu; hasilnya pesan untuk yang tetap bertahan.
    pub fn hentikan_sisa(&self) -> Vec<Pesan> {
        let (a, pk) = self.paket_aktif();
        match a {
            Some(a) => self.inti.layanan.hentikan_sisa(&a, pk.mysql.as_ref(), ssl::exists()),
            None => Vec::new(),
        }
    }

    // -------------------------------------------------------- node

    fn cari_app(&self, path: &str) -> Option<NodeApp> {
        let cocok = |a: &NodeApp| sama_jalur(&a.path, path);
        if let Some(a) = self.baca().node_apps.iter().find(|a| cocok(a)) {
            return Some(a.clone());
        }
        // apps.ini baru ditulis UI tapi belum ada muat_ulang: baca langsung.
        let apps = node_apps::load_all();
        let a = apps.iter().find(|a| cocok(a)).cloned();
        self.tulis().node_apps = apps;
        a
    }

    /// Jalankan proyek Node terdaftar dengan Node pilihannya. Hasil: URL bila
    /// sudah diketahui (biasanya belum - menyusul lewat `Kabar::Node`).
    pub fn jalankan_node(&self, path: &str, port_sekali: u16) -> Result<Option<String>, Pesan> {
        let Some(app) = self.cari_app(path) else { return Err(Pesan::baru("Proyek belum dipilih.")) };
        let node = if app.node_id.is_empty() { None } else { self.find(BinKind::Node, &app.node_id) };
        self.inti.node.start(&app, node.as_ref(), port_sekali)?;
        let p = Pesan::dengan(
            "Menjalankan {0} ({1} {2}).",
            &[&app.display_name(), &app.manager, &app.script],
        );
        // C#: Lang.T(...) + " · port N" - ekornya ikut di kunci, argumennya di belakang.
        let mut p = p;
        if port_sekali > 0 {
            p.kunci.push_str(" · port {3}");
            p.args.push(port_sekali.to_string());
        }
        self.say(p);
        Ok(self.inti.node.url_of(&app.path))
    }

    pub fn hentikan_node(&self, path: &str) {
        self.inti.node.stop(path);
    }

    pub fn hentikan_semua_node(&self) {
        self.inti.node.stop_all();
    }

    /// Riwayat keluaran proyek sebagai teks.
    pub fn riwayat_node(&self, path: &str) -> Vec<String> {
        self.inti.node.riwayat(path).iter().map(BarisNode::teks).collect()
    }

    fn port_profil(&self) -> Vec<u16> {
        self.active()
            .map(|a| {
                [a.http_port, a.https_port, a.mysql_port].iter().filter_map(|p| u16::try_from(*p).ok()).collect()
            })
            .unwrap_or_default()
    }

    /// Port bebas pertama mulai dari `mulai` (0 = 3001), dalam 50 port,
    /// melewati port web dan MySQL profil.
    pub fn cari_port_bebas(&self, mulai: u16) -> Option<u16> {
        let mulai = if mulai == 0 { 3001 } else { mulai };
        let lewati = self.port_profil();
        (mulai..mulai.saturating_add(50)).find(|p| !lewati.contains(p) && port::is_free(*p))
    }

    /// Port tawaran untuk proyek yang gagal karena portnya terpakai.
    pub fn port_tawaran(&self, path: &str) -> Option<u16> {
        self.inti.node.port_tawaran(path, &self.port_profil())
    }

    // -------------------------------------------------------- situs dan setelan

    /// Taruh situs ini paling depan di daftar terakhir dibuka.
    pub fn catat_situs_dibuka(&self, path: &str) {
        if path.is_empty() {
            return;
        }
        let mut d = self.tulis();
        let s = &mut d.settings;
        s.situs_terakhir.retain(|x| !sama_abaikan_besar(x, path));
        s.situs_terakhir.insert(0, path.to_string());
        s.situs_terakhir.truncate(settings::BATAS_SITUS_TERAKHIR);
        let _ = s.save(); // daftar ini kenyamanan, bukan data
    }

    /// Situs terakhir dibuka yang masih ada di profil ini, terbaru dulu.
    pub fn situs_terakhir(&self, maks: usize) -> Vec<Site> {
        let d = self.baca();
        d.settings
            .situs_terakhir
            .iter()
            .filter_map(|p| d.sites.iter().find(|s| sama_abaikan_besar(&s.path, p)).cloned())
            .take(maks)
            .collect()
    }

    /// Setelan dari UI: ganti, simpan ke phoron.ini, lalu muat ulang. Profil
    /// aktif dan situs terakhir dari UI diabaikan (lihat `salin_setelan`).
    pub fn simpan_setelan(&self, baru: Settings) -> Result<(), Pesan> {
        let rinci = {
            let mut d = self.tulis();
            salin_setelan(&mut d.settings, baru);
            d.settings.save().map_err(|e| galat_io("Setelan tidak bisa disimpan: {0}", &e))?;
            d.settings.log_rinci
        };
        self.inti.layanan.set_log_rinci(rinci);
        self.muat_ulang();
        Ok(())
    }

    /// Ubah HANYA medan setelan yang disebut, di atas setelan terkini.
    ///
    /// Inilah jalan UI menyimpan setelan. Mengirim salinan utuh berarti salinan
    /// UI yang basi menimpa perubahan dari menu baki atau klien lain - mis.
    /// menyimpan tema mengembalikan profil aktif ke profil lama. Baca-ubah-tulis
    /// terjadi di bawah satu kunci tulis, jadi dua ubahan yang berpapasan tidak
    /// saling menghapus. Medan yang tidak dikenal ditolak, bukan diabaikan:
    /// salah eja nama kunci di UI harus ketahuan, bukan hilang diam-diam.
    ///
    /// `active_profile` ditolak (ganti profil mematikan, menerapkan ulang,
    /// dan menyalakan lagi layanan - setelan tidak), begitu pula
    /// `situs_terakhir` (dicatat lewat `catat_situs_dibuka`). `cek_terakhir`
    /// boleh: UI yang mengecek pembaruan.
    pub fn ubah_setelan(&self, perubahan: serde_json::Map<String, serde_json::Value>) -> Result<(), Pesan> {
        if perubahan.contains_key("active_profile") {
            return Err(Pesan::baru("Profil aktif diganti lewat Ganti profil, bukan lewat setelan."));
        }
        if perubahan.contains_key("situs_terakhir") {
            return Err(Pesan::baru("Situs terakhir dicatat lewat Catat situs dibuka, bukan lewat setelan."));
        }
        let rinci = {
            let mut d = self.tulis();
            let mut v = serde_json::to_value(&d.settings)
                .map_err(|e| Pesan::dengan("Setelan tidak sah: {0}", &[&e.to_string()]))?;
            let Some(obj) = v.as_object_mut() else {
                return Err(Pesan::dengan("Setelan tidak sah: {0}", &["bukan objek"]));
            };
            for (k, nilai) in perubahan {
                if !obj.contains_key(&k) {
                    return Err(Pesan::dengan("Setelan {0} tidak dikenal.", &[&k]));
                }
                obj.insert(k, nilai);
            }
            let baru: Settings = serde_json::from_value(v)
                .map_err(|e| Pesan::dengan("Setelan tidak sah: {0}", &[&e.to_string()]))?;
            salin_setelan(&mut d.settings, baru);
            d.settings.save().map_err(|e| galat_io("Setelan tidak bisa disimpan: {0}", &e))?;
            d.settings.log_rinci
        };
        self.inti.layanan.set_log_rinci(rinci);
        self.muat_ulang();
        Ok(())
    }

    /// Keadaan lengkap untuk layar.
    pub fn keadaan(&self) -> Keadaan {
        let (profil_aktif, peringatan_terapkan, masalah_muat) = {
            let d = self.baca();
            (
                d.active.as_ref().map(|a| a.file_name.clone()).unwrap_or_default(),
                d.peringatan_terapkan.clone(),
                d.masalah_muat(),
            )
        };
        Keadaan {
            status: self.inti.layanan.status(),
            profil_aktif,
            node: self.inti.node.status_semua(),
            perlu_restart_web: self.perlu_restart_web(),
            perlu_restart_db: self.perlu_restart_db(),
            peringatan_terapkan,
            masalah_muat,
            admin: tulis::is_admin(),
        }
    }

}

// ------------------------------------------------------------ protokol

fn json<T: serde::Serialize>(v: T) -> serde_json::Value {
    serde_json::to_value(v).unwrap_or(serde_json::Value::Null)
}

fn ok(hasil: serde_json::Value, peringatan: Vec<Pesan>) -> IsiJawaban {
    IsiJawaban::Ok { hasil, peringatan }
}

fn kosong() -> IsiJawaban {
    ok(serde_json::Value::Null, Vec::new())
}

fn gagal(pesan: Pesan) -> IsiJawaban {
    IsiJawaban::Galat { galat: Galat::Gagal { pesan } }
}

fn selesai(r: Result<(), Pesan>) -> IsiJawaban {
    match r {
        Ok(()) => kosong(),
        Err(p) => gagal(p),
    }
}

/// Jawab satu perintah protokol. `Halo` dan `Keluar` urusan daemon (jabat
/// tangan dan siklus hidup); di sini dijawab `BelumAda`. MEMBLOKIR untuk
/// perintah layanan - panggil dari utas kerja.
pub fn tangani(e: &Engine, perintah: Perintah) -> IsiJawaban {
    match perintah {
        Perintah::Halo { .. } => IsiJawaban::Galat { galat: Galat::BelumAda { perintah: "halo".into() } },
        Perintah::Keluar => IsiJawaban::Galat { galat: Galat::BelumAda { perintah: "keluar".into() } },
        Perintah::Keadaan => ok(json(e.keadaan()), Vec::new()),
        Perintah::MuatUlang => {
            e.muat_ulang();
            kosong()
        }
        Perintah::SimpanSetelan { setelan } => selesai(e.simpan_setelan(setelan)),
        Perintah::UbahSetelan { perubahan } => selesai(e.ubah_setelan(perubahan)),
        // Ditangani phorond sendiri (menjalankan ulang dirinya lewat UAC).
        Perintah::MulaiUlangAdmin => {
            IsiJawaban::Galat { galat: Galat::BelumAda { perintah: "mulai_ulang_admin".into() } }
        }
        Perintah::Terapkan => match e.terapkan() {
            Ok(w) => ok(serde_json::Value::Null, w),
            Err(p) => gagal(p),
        },
        Perintah::GantiProfil { file_name, restart } => match e.ganti_profil(&file_name, restart) {
            Ok(w) => ok(serde_json::Value::Null, w),
            Err(p) => gagal(p),
        },
        Perintah::NyalakanSemua => selesai(e.nyalakan_semua()),
        Perintah::MatikanSemua => {
            e.matikan_semua();
            kosong()
        }
        Perintah::NyalakanWeb => selesai(e.nyalakan_web().map(drop)),
        Perintah::MatikanWeb => {
            e.matikan_web();
            kosong()
        }
        Perintah::NyalakanDb => selesai(e.nyalakan_db().map(drop)),
        Perintah::MatikanDb => {
            e.matikan_db();
            kosong()
        }
        Perintah::JalankanNode { path, port_sekali } => match e.jalankan_node(&path, port_sekali) {
            Ok(url) => ok(json(url), Vec::new()),
            Err(p) => gagal(p),
        },
        Perintah::HentikanNode { path } => {
            e.hentikan_node(&path);
            kosong()
        }
        Perintah::HentikanSemuaNode => {
            e.hentikan_semua_node();
            kosong()
        }
        Perintah::RiwayatNode { path } => ok(json(e.riwayat_node(&path)), Vec::new()),
        Perintah::RiwayatLog => ok(json(e.riwayat_log()), Vec::new()),
        Perintah::SisaProses => ok(json(e.sisa_proses()), Vec::new()),
        Perintah::HentikanSisa => ok(serde_json::Value::Null, e.hentikan_sisa()),
        Perintah::CatatSitusDibuka { path } => {
            e.catat_situs_dibuka(&path);
            kosong()
        }
        Perintah::Catat { pesan } => {
            e.say(pesan);
            kosong()
        }
    }
}

#[cfg(test)]
mod uji;
