//! Fitur dari ServiceManager.cs - menyalakan dan mematikan layanan.
//!
//! Apache/Nginx, php-cgi (FastCGI profil dan kolam PHP per situs), dan MySQL
//! dijalankan sebagai proses anak daemon - BUKAN Windows Service. Alasannya
//! sama dengan Laragon: service butuh hak admin untuk dipasang, tetap ada
//! sesudah aplikasi ditutup, dan tidak bisa punya dua versi yang gampang
//! ditukar.
//!
//! Perubahan keadaan dan baris log dikirim sebagai `Peristiwa` lewat penerima
//! (callback atau channel); daemon meneruskannya sebagai `Kabar::Status` dan
//! `Kabar::Log`. Semua fungsi start/stop MEMBLOKIR: daemon memanggilnya dari
//! utas kerja, bukan dari utas pipe.
//!
//! Daftar periksa (pelajaran dari versi C#):
//!
//! Kunci dan peristiwa
//! - Kunci data TIDAK pernah dipegang saat I/O, saat menunggu, atau saat
//!   memanggil penerima: dulu kunci yang dipegang sambil mengangkat peristiwa,
//!   sementara utas lain menunggu kunci yang sama di StopAll, adalah
//!   kebuntuan sempurna. Kabar status diurutkan oleh kunci `siar` tersendiri
//!   yang tidak dipakai di tempat lain.
//! - Uji-lalu-lepas rujukan proses dalam SATU langkah (`lepas_*_jika`): dulu
//!   pengawas kematian proses dan Stop sama-sama melihat rujukan yang sama,
//!   dan penghentian yang DISENGAJA ikut dilaporkan "berhenti sendiri" lalu
//!   lampunya jadi merah sesudah Stop menyetelnya Berhenti. Uji:
//!   `nyata_apache_php`, `nyata_mysql_init_dan_rapi`.
//!
//! Start dan stop yang berpapasan (nomor giliran)
//! - Start yang masih berjalan DIPAKAI ULANG oleh start berikutnya: "Nyalakan
//!   semua" dua kali selagi InnoDB memulihkan diri dulu melahirkan dua mysqld,
//!   dan yang memegang 3306 tidak bisa dihentikan lagi. Uji:
//!   `nyata_apache_php` (dua start serentak, satu httpd).
//! - Setiap stop menaikkan nomor giliran PALING AWAL; start memeriksanya
//!   sesudah setiap langkah yang menunggu dan membereskan proses yang sempat
//!   ia lahirkan. Dulu "Matikan semua" di tengah start web membiarkan Apache
//!   hidup di port 80. Uji: `nyata_stop_di_tengah_start`.
//! - Stop di tengah jeda pemeriksaan tidak boleh melaporkan "Apache berhenti
//!   seketika" (lengkap dengan saran VC++) untuk httpd yang baru saja dibunuh
//!   Stop. Uji: `nyata_stop_di_tengah_start`.
//! - Lampu hijau diperiksa SEKALI LAGI sesudah disetel (`selesaikan`): stop
//!   dari utas lain bisa menyelinap tepat sebelumnya.
//! - Galat di tengah start (exe dikarantina antivirus, folder data tak bisa
//!   dibuat) berakhir di Gagal, tidak pernah tersangkut di Menyalakan. Uji:
//!   `exe_hilang_berakhir_gagal`.
//!
//! Web server
//! - Port HTTP (dan HTTPS bila config membukanya) diperiksa dulu dan
//!   pemegangnya disebut: Apache yang gagal mengikat SATU port menolak start
//!   seluruhnya. Uji: `port_terpakai_disebut_pemegangnya`.
//! - Config diuji (`httpd -t`, `nginx -t`) sebelum start: httpd yang salah
//!   config mati seketika tanpa satu baris keterangan. Gagal + petunjuk runtime
//!   VC++ (`bin::runtime_kurang_semua`). Uji: `nyata_config_ditolak`.
//! - httpd/nginx yang mati dalam jeda pertama dilaporkan dengan ekor
//!   apache-error.log dan petunjuk VC++. Dulu nginx tidak dijaga sama sekali.
//! - Kolam PHP per situs dan php-cgi profil dinyalakan SEBELUM web server.
//!   Kolam yang gagal tidak menggagalkan web server; yang kena disebut.
//! - php-cgi: PHP_FCGI_MAX_REQUESTS=0 (bawaan 500 membuat 503 di tengah
//!   kerja), PHP_FCGI_CHILDREN=4 untuk profil dan 2 untuk kolam (satu
//!   responder melayani permintaan satu per satu). Uji: `env_php_cgi`.
//! - php-cgi yang mati sendiri dinyalakan ulang di port yang sama, paling
//!   banyak 3 kali per menit per port. Uji: `batas_bangkit_per_menit`,
//!   `nyata_nginx_fastcgi_bangkit`.
//! - Proses web yang mati sendiri sesudah "jalan" -> Gagal, php-cgi ikut
//!   dimatikan. Uji: `nyata_apache_php`.
//! - PATH anak diberi folder PHP di depan dan PHPRC ke folder php.ini yang
//!   BENAR-BENAR ditulis (`config::folder_php_ini`); kolam ber-oci8 mendapat
//!   folder Instant Client yang sepadan di depan PATH (`oracle::folder_untuk`).
//!   Uji: `env_php_cgi`.
//!
//! MySQL
//! - Folder data kosong disiapkan dulu (mysqld --initialize-insecure, atau
//!   mysql_install_db untuk MariaDB). Init dianggap berhasil hanya bila tidak
//!   terputus dan (kode 0 atau tabel sistem benar-benar ada): dulu init yang
//!   terputus meninggalkan folder mysql\ dan setiap start sesudahnya gagal.
//!   Sisa init gagal di folder yang tadinya kosong DIPINDAH ke
//!   `<folder>.gagal-<waktu>`, tidak dihapus. Uji: `init_berhasil_hanya_bila_tuntas`,
//!   `init_gagal_dipindah`, `nyata_mysql_init_dan_rapi`, `nyata_mariadb_xampp`.
//! - Basis data lama di folder bernama sama milik MySQL lain disebut terang.
//! - mysqld dijalankan dengan --console; salinan keluarannya ditulis Phoron ke
//!   logs\mysql-error.log (--console membuat mysqld mengabaikan log-error).
//! - Siap = port didengar, ditunggu sampai `batas_siap_db` (baku 300 detik,
//!   pesan sabar sesudah 20 detik): dulu mysqld dibunuh di tengah pemulihan
//!   InnoDB dan tidak pernah nyala.
//! - Stop = RAPI: event `MySQLShutdown<PID>` dulu (tanpa sandi), mysqladmin
//!   sebagai cadangan, 12 detik, baru paksa. Rujukan dilepas SEBELUM meminta
//!   berhenti - dulu tiap penghentian rapi dilaporkan "MySQL berhenti sendiri
//!   (kode 0)". Uji: `nyata_mysql_init_dan_rapi` ("Shutdown complete").
//! - `stop_all` membunuh paksa hanya web server dan php-cgi; MySQL tetap
//!   dimatikan dengan rapi. Kabar status tetap dikirim.
//! - Folder data dicatat milik paket ini begitu mysqld jalan di atasnya.
//!
//! Proses
//! - Semua anak diikat ke Job Object daemon (`process_job::spawn_terikat`),
//!   terikat SEBELUM jalan: daemon mati -> httpd/mysqld ikut mati.
//! - Mematikan = mematikan POHON proses (httpd dan php-cgi punya anak).
//! - Keluaran layanan diteruskan seluruhnya hanya bila "log rinci" menyala;
//!   bila tidak, hanya baris penting (error, fatal, warning, cannot, failed,
//!   denied) - kegagalan start tidak boleh ikut hilang. Uji: `baris_penting`.
//! - Sisa proses jenis milik Phoron (httpd, mysqld, nginx, php-cgi) di port
//!   profil bisa dihentikan; mysqld diminta berhenti rapi dulu. Uji:
//!   `sisa_tidak_dicari_saat_layanan_jalan`.
//!
//! Beda yang disengaja dengan C# (bug di versi C#, diperbaiki di sini):
//! - httpd/nginx yang mati dalam jeda pertama dulu dilaporkan DUA kali
//!   ("berhenti sendiri" dari pengawas lalu "berhenti seketika" dari start).
//!   Kini pengawas diam selama start masih memeriksa prosesnya (`dalam_start`).
//! - php-cgi/kolam yang mati dalam jeda 600 ms dulu bisa hilang tanpa pesan
//!   (pengawas melepasnya diam-diam karena status belum Jalan), dan stop di
//!   tengah jeda itu dilaporkan "php-cgi berhenti seketika". Kini start yang
//!   melapor, dan hanya bila tidak dibatalkan.
//! - httpd yang gagal dilahirkan dulu meninggalkan php-cgi dan kolamnya hidup.
//! - Nginx yang mati sendiri dulu menyuruh membuka apache-error.log.
//! - mysqld yang lahir lalu mati sebelum rujukannya dipasang dulu membuat
//!   status tersangkut di Menyalakan; kini start sendiri yang melapor.
//! - mysqld yang diminta stop tepat saat dinyatakan jalan dulu dibunuh paksa;
//!   kini dimatikan rapi.

use crate::config;
use crate::model::{BinPackage, Profile, ServiceKind, ServiceState};
use crate::protokol::{Pesan, StatusLayanan};
use crate::{log_file, port, process_job, shell};
use std::collections::HashMap;
use std::os::windows::process::CommandExt;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::sync::atomic::{AtomicBool, AtomicI32, AtomicU64, Ordering};
use std::sync::{mpsc, Arc, Condvar, Mutex, MutexGuard};
use std::time::{Duration, Instant};

mod db;
mod web;

#[cfg(test)]
mod uji;

pub use db::{init_berhasil, minta_mysql_berhenti, mysql_error_log, tabel_sistem_ada};

const CREATE_NO_WINDOW: u32 = 0x0800_0000;

/// Nama proses yang memang dijalankan Phoron - untuk mengenali sisa.
pub const PROSES_KITA: &[&str] = &["httpd", "mysqld", "nginx", "php-cgi"];

/// Kabar dari pengelola layanan.
#[derive(Debug, Clone, PartialEq)]
pub enum Peristiwa {
    /// Keadaan kedua layanan sesudah berubah (diteruskan sebagai Kabar::Status).
    Status(StatusLayanan),
    /// Satu baris log Aktivitas (diteruskan sebagai Kabar::Log).
    Log(Pesan),
}

type Penerima = Arc<dyn Fn(Peristiwa) + Send + Sync>;

/// Hasil langkah start: `Err` = galat tak terduga (I/O), dilaporkan sebagai
/// "... tidak bisa dinyalakan: <sebab>".
type Lanjut<T> = Result<T, String>;

/// Cara menyalakan ulang sebuah php-cgi (kolam atau milik profil).
#[derive(Debug, Clone)]
struct Resep {
    exe: String,
    args: String,
    kerja_di: String,
    env: HashMap<String, String>,
    tag: String,
    /// Untuk pesan: "php-cgi" atau "PHP 8.3.12 (api, bbs)".
    nama: String,
    port: u16,
    kolam: bool,
}

/// Satu proses anak. Pemilik `Child` adalah utas pengawasnya; yang lain hanya
/// melihat PID dan tanda keluarnya.
struct Proses {
    pid: u32,
    tag: String,
    resep: Option<Resep>,
    /// Benar selama start web masih memeriksa proses ini: kematiannya
    /// dilaporkan start (dengan ekor log), bukan oleh pengawas.
    dalam_start: AtomicBool,
    keluar: AtomicBool,
    kode: AtomicI32,
    tunggu: Mutex<()>,
    cv: Condvar,
}

impl Proses {
    fn keluar(&self) -> bool {
        self.keluar.load(Ordering::SeqCst)
    }

    fn kode(&self) -> i32 {
        self.kode.load(Ordering::SeqCst)
    }

    /// Tunggu proses berakhir paling lama `batas`; benar bila sudah berakhir.
    fn tunggu_keluar(&self, batas: Duration) -> bool {
        let mulai = Instant::now();
        let mut g = kunci(&self.tunggu);
        while !self.keluar() {
            let Some(sisa) = batas.checked_sub(mulai.elapsed()) else { return false };
            if sisa.is_zero() {
                return false;
            }
            g = self.cv.wait_timeout(g, sisa).map(|(g, _)| g).unwrap_or_else(|e| e.into_inner().0);
        }
        true
    }

    /// Matikan pohon prosesnya bila masih hidup.
    ///
    /// taskkill /T memotret pohonnya dulu, baru membunuh. httpd yang baru
    /// lahir bisa melahirkan anak tepat di antara keduanya (terlihat pada stop
    /// di tengah start): anak itu yatim dan tetap memegang port. Karena itu
    /// keturunan yang masih hidup dicari lagi sesudahnya - induk yang sudah
    /// mati tetap tercatat sebagai induk mereka.
    fn bunuh(&self) {
        if self.keluar() {
            return;
        }
        shell::kill_tree(self.pid);
        for _ in 0..3 {
            let sisa: Vec<u32> =
                process_job::keturunan(self.pid).into_iter().filter(|p| process_job::masih_hidup(*p)).collect();
            if sisa.is_empty() {
                break;
            }
            for p in sisa {
                shell::kill_tree(p);
            }
        }
    }

    fn tandai_keluar(&self, kode: i32) {
        self.kode.store(kode, Ordering::SeqCst);
        let _g = kunci(&self.tunggu);
        self.keluar.store(true, Ordering::SeqCst);
        self.cv.notify_all();
    }
}

/// Start yang sedang berjalan; start berikutnya menunggu hasilnya.
#[derive(Default)]
struct Mulai {
    hasil: Mutex<Option<bool>>,
    cv: Condvar,
}

impl Mulai {
    fn selesai(&self) -> bool {
        kunci(&self.hasil).is_some()
    }

    fn tunggu(&self) -> bool {
        let mut g = kunci(&self.hasil);
        loop {
            if let Some(h) = *g {
                return h;
            }
            g = self.cv.wait(g).unwrap_or_else(|e| e.into_inner());
        }
    }

    fn isi(&self, h: bool) {
        *kunci(&self.hasil) = Some(h);
        self.cv.notify_all();
    }
}

struct Data {
    web: Option<Arc<Proses>>,
    db: Option<Arc<Proses>>,
    fcgi: Option<Arc<Proses>>,
    kolam: Vec<Arc<Proses>>,
    /// Kapan saja php-cgi di tiap port dinyalakan ulang dalam semenit terakhir.
    bangkit: HashMap<u16, Vec<Instant>>,
    mulai_web: Option<Arc<Mulai>>,
    mulai_db: Option<Arc<Mulai>>,
    /// Paket dan port mysqld yang sedang dijalankan: `stop_all` tidak menerima
    /// profil, padahal cadangan penghentian rapinya (mysqladmin) butuh keduanya.
    db_paket: Option<BinPackage>,
    db_port: u16,
}

struct Keadaan {
    web: ServiceState,
    db: ServiceState,
    galat_web: Option<Pesan>,
    galat_db: Option<Pesan>,
}

struct Inti {
    data: Mutex<Data>,
    keadaan: Mutex<Keadaan>,
    /// Hanya mengurutkan kabar status; tidak dipakai untuk hal lain.
    siar: Mutex<()>,
    penerima: Penerima,
    giliran_web: AtomicU64,
    giliran_db: AtomicU64,
    log_rinci: AtomicBool,
    batas_siap_db_ms: AtomicU64,
    /// Job khusus (uji); `None` = job daemon.
    job: Option<Arc<process_job::Job>>,
}

/// Pengelola layanan. Murah di-clone; semua salinan berbagi keadaan yang sama.
#[derive(Clone)]
pub struct Layanan {
    inti: Arc<Inti>,
}

fn kunci<T>(m: &Mutex<T>) -> MutexGuard<'_, T> {
    m.lock().unwrap_or_else(|e| e.into_inner())
}

fn sama(a: &Option<Arc<Proses>>, p: &Arc<Proses>) -> bool {
    a.as_ref().is_some_and(|x| Arc::ptr_eq(x, p))
}

/// Port profil (i32) sebagai u16; nilai di luar rentang jadi 0.
fn port16(p: i32) -> u16 {
    u16::try_from(p).unwrap_or(0)
}

/// `Pesan` lain disambung dengan ekor teks: kuncinya tetap templat utuh yang
/// bisa diterjemahkan, argumennya ikut.
fn sambung(p: Pesan, ekor: &str) -> Pesan {
    Pesan { kunci: format!("{}{}", p.kunci, ekor), args: p.args }
}

/// Baris keluaran yang tetap diteruskan walau log rinci mati: tanpa penyaring
/// ini, mematikan log berarti kegagalan start juga ikut hilang.
pub fn penting(baris: &str) -> bool {
    let l = baris.to_lowercase();
    ["error", "fatal", "warning", "cannot", "failed", "denied"].iter().any(|k| l.contains(k))
}

/// Lingkungan anak: folder PHP di depan PATH supaya exec() dari skrip memakai
/// versi ini, dan PHPRC ke folder php.ini yang benar-benar ditulis.
pub fn env_for(php: Option<&BinPackage>, folder_php_ini: Option<&str>) -> HashMap<String, String> {
    let mut env = HashMap::new();
    if let Some(php) = php {
        let path = std::env::var("PATH").unwrap_or_default();
        env.insert("PATH".to_string(), format!("{};{}", php.path, path));
        let rc = folder_php_ini.map(str::to_string).unwrap_or_else(|| config::folder_php_ini(php, false));
        env.insert("PHPRC".to_string(), rc);
    }
    env
}

/// Boleh menyalakan ulang php-cgi lagi? Mencatat saat ini bila boleh.
fn boleh_bangkit(kapan: &mut Vec<Instant>, kini: Instant) -> bool {
    kapan.retain(|t| kini.saturating_duration_since(*t) <= Duration::from_secs(60));
    let boleh = kapan.len() < BANGKIT_MAKS_PER_MENIT;
    if boleh {
        kapan.push(kini);
    }
    boleh
}

const BANGKIT_MAKS_PER_MENIT: usize = 3;

impl Layanan {
    pub fn baru(penerima: impl Fn(Peristiwa) + Send + Sync + 'static) -> Layanan {
        Layanan::dengan_job(penerima, None)
    }

    /// Layanan yang peristiwanya dikirim ke channel.
    pub fn dengan_saluran() -> (Layanan, mpsc::Receiver<Peristiwa>) {
        let (tx, rx) = mpsc::channel();
        (Layanan::baru(move |p| drop(tx.send(p))), rx)
    }

    fn dengan_job(penerima: impl Fn(Peristiwa) + Send + Sync + 'static, job: Option<Arc<process_job::Job>>) -> Layanan {
        Layanan {
            inti: Arc::new(Inti {
                data: Mutex::new(Data {
                    web: None,
                    db: None,
                    fcgi: None,
                    kolam: Vec::new(),
                    bangkit: HashMap::new(),
                    mulai_web: None,
                    mulai_db: None,
                    db_paket: None,
                    db_port: 0,
                }),
                keadaan: Mutex::new(Keadaan {
                    web: ServiceState::Berhenti,
                    db: ServiceState::Berhenti,
                    galat_web: None,
                    galat_db: None,
                }),
                siar: Mutex::new(()),
                penerima: Arc::new(penerima),
                giliran_web: AtomicU64::new(0),
                giliran_db: AtomicU64::new(0),
                log_rinci: AtomicBool::new(false),
                batas_siap_db_ms: AtomicU64::new(300_000),
                job,
            }),
        }
    }

    /// "Catat log rinci": teruskan SELURUH keluaran layanan, bukan hanya baris
    /// pentingnya. Mati secara baku: mysqld mencetak ratusan baris tiap menyala.
    pub fn set_log_rinci(&self, rinci: bool) {
        self.inti.log_rinci.store(rinci, Ordering::SeqCst);
    }

    /// Berapa lama start MySQL menunggu port-nya didengar. Longgar dengan
    /// sengaja: yang ditunggu bisa jadi pemulihan InnoDB.
    pub fn set_batas_siap_db(&self, batas: Duration) {
        self.inti.batas_siap_db_ms.store(batas.as_millis().min(u64::MAX as u128) as u64, Ordering::SeqCst);
    }

    fn batas_siap_db(&self) -> Duration {
        Duration::from_millis(self.inti.batas_siap_db_ms.load(Ordering::SeqCst))
    }

    fn data(&self) -> MutexGuard<'_, Data> {
        kunci(&self.inti.data)
    }

    // ------------------------------------------------------------ keadaan

    pub fn status(&self) -> StatusLayanan {
        let k = kunci(&self.inti.keadaan);
        StatusLayanan { web: k.web, db: k.db, galat_web: k.galat_web.clone(), galat_db: k.galat_db.clone() }
    }

    pub fn web_state(&self) -> ServiceState {
        kunci(&self.inti.keadaan).web
    }

    pub fn db_state(&self) -> ServiceState {
        kunci(&self.inti.keadaan).db
    }

    /// PID web server yang sedang hidup; 0 bila tidak ada.
    pub fn web_pid(&self) -> u32 {
        self.data().web.as_ref().filter(|p| !p.keluar()).map(|p| p.pid).unwrap_or(0)
    }

    pub fn db_pid(&self) -> u32 {
        self.data().db.as_ref().filter(|p| !p.keluar()).map(|p| p.pid).unwrap_or(0)
    }

    /// PID php-cgi profil dan kolam yang sedang dipegang (untuk keterangan).
    pub fn php_cgi_pid(&self) -> Vec<u32> {
        let d = self.data();
        d.fcgi.iter().chain(d.kolam.iter()).filter(|p| !p.keluar()).map(|p| p.pid).collect()
    }

    /// Ubah keadaan; galat hanya disimpan untuk Gagal. Kabar dikirim di luar
    /// kunci data.
    fn ubah(&self, jenis: ServiceKind, state: ServiceState, galat: Option<Pesan>) {
        let _urut = kunci(&self.inti.siar);
        let snap = {
            let mut k = kunci(&self.inti.keadaan);
            let g = if state == ServiceState::Gagal { galat } else { None };
            match jenis {
                ServiceKind::Web => {
                    k.web = state;
                    k.galat_web = g;
                }
                ServiceKind::Db => {
                    k.db = state;
                    k.galat_db = g;
                }
            }
            StatusLayanan { web: k.web, db: k.db, galat_web: k.galat_web.clone(), galat_db: k.galat_db.clone() }
        };
        (self.inti.penerima)(Peristiwa::Status(snap));
    }

    fn say(&self, p: Pesan) {
        (self.inti.penerima)(Peristiwa::Log(p));
    }

    /// Catat pesannya lalu nyatakan Gagal dengan pesan itu. Selalu `false`.
    fn gagal(&self, jenis: ServiceKind, p: Pesan) -> bool {
        self.say(p.clone());
        self.ubah(jenis, ServiceState::Gagal, Some(p));
        false
    }

    // ------------------------------------------------------------ giliran

    fn giliran_ref(&self, jenis: ServiceKind) -> &AtomicU64 {
        match jenis {
            ServiceKind::Web => &self.inti.giliran_web,
            ServiceKind::Db => &self.inti.giliran_db,
        }
    }

    fn giliran(&self, jenis: ServiceKind) -> u64 {
        self.giliran_ref(jenis).load(Ordering::SeqCst)
    }

    fn batal(&self, jenis: ServiceKind, g: u64) -> bool {
        self.giliran(jenis) != g
    }

    fn naikkan_giliran(&self, jenis: ServiceKind) {
        self.giliran_ref(jenis).fetch_add(1, Ordering::SeqCst);
    }

    /// Jalankan badan start, atau ikut menunggu start yang masih berjalan.
    /// MENJAMIN keadaannya tidak tertinggal di Menyalakan bila badan start
    /// berakhir dengan galat tak terduga.
    fn antre(&self, jenis: ServiceKind, kerja: impl FnOnce(u64) -> Lanjut<bool>) -> bool {
        let (mulai, g) = {
            let mut d = self.data();
            let slot = match jenis {
                ServiceKind::Web => &mut d.mulai_web,
                ServiceKind::Db => &mut d.mulai_db,
            };
            if let Some(m) = slot.as_ref().filter(|m| !m.selesai()) {
                let m = m.clone();
                drop(d);
                return m.tunggu();
            }
            let m = Arc::new(Mulai::default());
            *slot = Some(m.clone());
            (m, self.giliran(jenis))
        };
        // Di luar kunci: badan start langsung mengirim kabar.
        let hasil = match kerja(g) {
            Ok(h) => h,
            Err(sebab) => {
                let kunci_pesan = match jenis {
                    ServiceKind::Web => "Web server tidak bisa dinyalakan: {0}",
                    ServiceKind::Db => "MySQL tidak bisa dinyalakan: {0}",
                };
                let p = Pesan::dengan(kunci_pesan, &[&sebab]);
                if self.batal(jenis, g) {
                    self.say(p);
                } else {
                    self.gagal(jenis, p);
                }
                false
            }
        };
        mulai.isi(hasil);
        hasil
    }

    /// Langkah terakhir start: nyatakan Jalan, lalu periksa SEKALI LAGI - stop
    /// dari utas lain bisa menyelinap tepat sebelumnya.
    fn selesaikan(&self, jenis: ServiceKind, g: u64, p: &Arc<Proses>, mysql: Option<&BinPackage>, port: u16) -> bool {
        self.ubah(jenis, ServiceState::Jalan, None);
        if !self.batal(jenis, g) {
            return true;
        }
        match jenis {
            ServiceKind::Web => self.buang_web_yatim(p),
            ServiceKind::Db => {
                if self.lepas_db_jika(p) {
                    self.matikan_mysql_rapi(p, mysql, port);
                }
            }
        }
        self.ubah(jenis, ServiceState::Berhenti, None);
        false
    }

    // ------------------------------------------------------------ rujukan

    fn ambil_lepas_web(&self) -> Option<Arc<Proses>> {
        self.data().web.take()
    }

    fn ambil_lepas_db(&self) -> Option<Arc<Proses>> {
        self.data().db.take()
    }

    fn lepas_web_jika(&self, p: &Arc<Proses>) -> bool {
        let mut d = self.data();
        if sama(&d.web, p) {
            d.web = None;
            true
        } else {
            false
        }
    }

    fn lepas_db_jika(&self, p: &Arc<Proses>) -> bool {
        let mut d = self.data();
        if sama(&d.db, p) {
            d.db = None;
            true
        } else {
            false
        }
    }

    fn lepas_fcgi_jika(&self, p: &Arc<Proses>) -> bool {
        let mut d = self.data();
        if sama(&d.fcgi, p) {
            d.fcgi = None;
            true
        } else {
            false
        }
    }

    fn lepas_kolam_jika(&self, p: &Arc<Proses>) -> bool {
        let mut d = self.data();
        let n = d.kolam.len();
        d.kolam.retain(|x| !Arc::ptr_eq(x, p));
        d.kolam.len() != n
    }

    // ------------------------------------------------------------ proses

    /// Jalankan satu layanan, terikat ke job sebelum sempat jalan. Keluarannya
    /// dibaca per baris; `salin_ke` = berkas log yang menerima seluruhnya.
    #[allow(clippy::too_many_arguments)]
    fn spawn(
        &self,
        exe: &str,
        args: &str,
        kerja_di: &str,
        env: Option<&HashMap<String, String>>,
        tag: &str,
        salin_ke: Option<PathBuf>,
        resep: Option<Resep>,
        dalam_start: bool,
    ) -> Result<Arc<Proses>, Pesan> {
        let mut cmd = Command::new(exe);
        if !args.is_empty() {
            cmd.raw_arg(args);
        }
        if Path::new(kerja_di).is_dir() {
            cmd.current_dir(kerja_di);
        }
        cmd.stdin(Stdio::null()).stdout(Stdio::piped()).stderr(Stdio::piped());
        if let Some(env) = env {
            for (k, v) in env {
                cmd.env(k, v);
            }
        }
        let hasil = match &self.inti.job {
            Some(j) => j.spawn(&mut cmd, CREATE_NO_WINDOW),
            None => process_job::spawn_terikat(&mut cmd, CREATE_NO_WINDOW),
        };
        let mut anak = match hasil {
            Ok(a) => a,
            Err(e) => {
                let nama = Path::new(exe).file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
                return Err(Pesan::dengan("Tidak bisa menjalankan {0}: {1}", &[&nama, &e.to_string()]));
            }
        };
        let p = Arc::new(Proses {
            pid: anak.id(),
            tag: tag.to_string(),
            resep,
            dalam_start: AtomicBool::new(dalam_start),
            keluar: AtomicBool::new(false),
            kode: AtomicI32::new(0),
            tunggu: Mutex::new(()),
            cv: Condvar::new(),
        });

        let (tx, rx) = mpsc::channel::<()>();
        if let Some(s) = anak.stdout.take() {
            self.baca(s, tag, salin_ke.clone(), tx.clone());
        }
        if let Some(s) = anak.stderr.take() {
            self.baca(s, tag, salin_ke, tx.clone());
        }
        drop(tx);

        // Proses bisa mati sendiri setelah dilaporkan "jalan": httpd yang
        // kehabisan port, mysqld yang gagal memulihkan InnoDB, php-cgi yang
        // ditutup paksa. Tanpa pengawas ini lampu tetap hijau.
        let diri = self.clone();
        let pw = p.clone();
        std::thread::spawn(move || {
            let kode = anak.wait().ok().and_then(|s| s.code()).unwrap_or(-1);
            pw.tandai_keluar(kode);
            // Kuras keluaran dulu supaya sebab kematiannya tercatat sebelum
            // laporannya, tapi jangan menunggu pipa yang dipegang cucu.
            let _ = rx.recv_timeout(Duration::from_secs(2));
            diri.proses_mati(&pw);
        });
        Ok(p)
    }

    fn baca(&self, r: impl std::io::Read + Send + 'static, tag: &str, salin_ke: Option<PathBuf>, selesai: mpsc::Sender<()>) {
        let diri = self.clone();
        let tag = tag.to_string();
        std::thread::spawn(move || {
            crate::node_runner::per_baris(r, |baris| {
                if baris.trim().is_empty() {
                    return;
                }
                if let Some(f) = &salin_ke {
                    log_file::tambah(f, &baris);
                }
                if diri.inti.log_rinci.load(Ordering::SeqCst) || penting(&baris) {
                    diri.say(Pesan::dengan("[{0}] {1}", &[&tag, &baris]));
                }
            });
            drop(selesai);
        });
    }

    /// Dipanggil pengawas saat sebuah proses berakhir. Hanya berarti bila
    /// proses itu MASIH dipegang: penghentian yang disengaja melepas
    /// rujukannya lebih dulu.
    fn proses_mati(&self, p: &Arc<Proses>) {
        enum Siapa {
            Web,
            Db,
            Php,
        }
        let siapa = {
            let mut d = self.data();
            if sama(&d.web, p) {
                if p.dalam_start.load(Ordering::SeqCst) {
                    // Start yang melapor, dengan ekor log dan petunjuknya.
                    return;
                }
                d.web = None;
                Siapa::Web
            } else if sama(&d.db, p) {
                d.db = None;
                Siapa::Db
            } else if let Some(i) = d.kolam.iter().position(|x| Arc::ptr_eq(x, p)) {
                d.kolam.remove(i);
                Siapa::Php
            } else if sama(&d.fcgi, p) {
                d.fcgi = None;
                Siapa::Php
            } else {
                return;
            }
        };
        let kode = p.kode().to_string();
        match siapa {
            Siapa::Web => {
                self.stop_fastcgi();
                let log = if p.tag == "nginx" { "nginx-error.log" } else { "apache-error.log" };
                let kunci_pesan = format!("{{0}} berhenti sendiri (kode {{1}}). Lihat logs\\{log}.");
                self.gagal(ServiceKind::Web, Pesan::dengan(&kunci_pesan, &[&p.tag, &kode]));
            }
            Siapa::Db => {
                self.gagal(
                    ServiceKind::Db,
                    Pesan::dengan("MySQL berhenti sendiri (kode {0}). Lihat logs\\mysql-error.log.", &[&kode]),
                );
            }
            Siapa::Php => {
                // Web server sedang menyala atau mati: yang melapor start/stop.
                if self.web_state() == ServiceState::Jalan {
                    if let Some(r) = &p.resep {
                        self.bangkitkan(r.clone(), p.kode());
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------ semua

    /// Bunuh semua yang masih dipegang - dipanggil saat daemon berhenti. Web
    /// server dan php-cgi dibunuh paksa; MySQL TIDAK (dulu taskkill /F setiap
    /// Phoron ditutup: pemulihan InnoDB tiap start, MyISAM bisa rusak).
    pub fn stop_all(&self) {
        // Start yang masih berjalan ikut dibatalkan.
        self.naikkan_giliran(ServiceKind::Web);
        self.naikkan_giliran(ServiceKind::Db);
        let (w, f, d, kolam, paket, port) = {
            let mut g = self.data();
            g.bangkit.clear();
            (g.web.take(), g.fcgi.take(), g.db.take(), std::mem::take(&mut g.kolam), g.db_paket.clone(), g.db_port)
        };
        for p in w.iter().chain(f.iter()).chain(kolam.iter()) {
            p.bunuh();
        }
        if let Some(d) = d {
            if self.matikan_mysql_rapi(&d, paket.as_ref(), port) {
                self.say(Pesan::baru("MySQL dimatikan dengan rapi."));
            } else {
                self.say(Pesan::baru("MySQL tidak mau berhenti dengan rapi - dimatikan paksa."));
            }
        }
        // Lewat kabar, bukan medan langsung: lampu tidak boleh tetap hijau.
        self.ubah(ServiceKind::Web, ServiceState::Berhenti, None);
        self.ubah(ServiceKind::Db, ServiceState::Berhenti, None);
    }

    // ------------------------------------------------------------ sisa

    /// Port profil yang dipegang proses jenis milik Phoron padahal Phoron ini
    /// tidak merasa menjalankannya - biasanya sisa salinan sebelumnya yang
    /// ditutup paksa. Kosong selama salah satu layanan kita sedang jalan.
    pub fn sisa_proses(&self, profile: &Profile, https: bool) -> Vec<port::Usage> {
        if self.web_state() == ServiceState::Jalan || self.db_state() == ServiceState::Jalan {
            return Vec::new();
        }
        port::conflicts(profile, https, true, true)
            .into_iter()
            .filter(|u| PROSES_KITA.contains(&u.process_name.to_lowercase().as_str()))
            .collect()
    }

    /// Hentikan sisa proses itu; mysqld diminta berhenti rapi dulu. Hasil:
    /// pesan untuk yang tetap tidak bisa dihentikan.
    pub fn hentikan_sisa(&self, profile: &Profile, mysql: Option<&BinPackage>, https: bool) -> Vec<Pesan> {
        let mut gagal = Vec::new();
        for u in self.sisa_proses(profile, https) {
            let pid = u.pid.to_string();
            let port_s = u.port.to_string();
            if u.process_name.eq_ignore_ascii_case("mysqld") {
                self.say(Pesan::dengan("Meminta mysqld (PID {0}) berhenti dengan rapi...", &[&pid]));
                if minta_mysql_berhenti(u.pid) {
                    for _ in 0..30 {
                        if port::is_free(u.port) {
                            break;
                        }
                        std::thread::sleep(Duration::from_millis(400));
                    }
                } else if let Some(m) = mysql {
                    let admin = Path::new(&m.path).join("bin").join("mysqladmin.exe");
                    if admin.is_file() {
                        let _ = shell::run(
                            &admin.to_string_lossy(),
                            &format!("--protocol=tcp --port={} -u root shutdown", u.port),
                            Some(Path::new(&m.path)),
                            20_000,
                            None,
                            None,
                        );
                        std::thread::sleep(Duration::from_millis(1500));
                    }
                }
            }
            if !port::is_free(u.port) {
                self.say(Pesan::dengan(
                    "Menghentikan {0} (PID {1}) yang memegang port {2}.",
                    &[&u.process_name, &pid, &port_s],
                ));
                shell::kill_tree(u.pid);
                std::thread::sleep(Duration::from_millis(700));
            }
            // Port yang MASIH terpakai sesudah dibunuh hampir selalu berarti
            // proses itu dijalankan Phoron ber-hak Administrator.
            if !port::is_free(u.port) {
                let kunci_pesan = if crate::tulis::is_admin() {
                    "{0} (PID {1}) di port {2} tidak bisa dihentikan."
                } else {
                    "{0} (PID {1}) di port {2} tidak bisa dihentikan - proses itu kemungkinan dijalankan \
                     Phoron ber-hak Administrator. Jalankan ulang Phoron sebagai Administrator, lalu coba lagi."
                };
                gagal.push(Pesan::dengan(kunci_pesan, &[&u.process_name, &pid, &port_s]));
            }
        }
        gagal
    }
}
