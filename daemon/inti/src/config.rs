//! Fitur: menulis seluruh konfigurasi sebuah profil ke folder etc\ - httpd.conf,
//! mod_php.conf, ssl.conf, vhost (000-default.conf, sites-enabled\auto.*.conf),
//! php.ini per versi (termasuk kolam PHP per situs), my.ini, nginx.conf +
//! fastcgi_params, dan beranda Phoron.
//!
//! Prinsip: JANGAN menyunting berkas di dalam folder bin. Bin bisa milik
//! Laragon atau dipakai bersama; menulis ke sana berarti dua pengelola saling
//! menimpa. Satu-satunya pengecualian adalah sakelar "Tulis php.ini ke dalam
//! folder PHP" yang dipilih pengguna sendiri.
//!
//! Daftar periksa (dari kasus nyata di versi C#):
//!
//! Web server
//! - Berkas yang ditulis dipilah menurut web server yang DIPILIH profil, bukan
//!   menurut paket yang kebetulan ada: profil Nginx yang versinya hilang tidak
//!   boleh diam-diam menghasilkan config Apache, dan keluhannya menyebut Nginx.
//! - Profil tanpa web server (hanya MySQL) sah: tidak ada yang ditulis, tidak
//!   ada keluhan.
//!
//! Apache (httpd.conf)
//! - Dasar: conf\original\httpd.conf lebih dulu (conf\httpd.conf mungkin sudah
//!   diacak pengelola lain).
//! - Build lama menaruh jalur harfiah di ServerRoot, bukan ${SRVROOT}: keduanya
//!   ditimpa.
//! - Include/IncludeOptional berjalur mutlak (milik Laragon dsb.) dibuang.
//! - DocumentRoot bawaan dikomentari: Apache memeriksa foldernya saat mengurai,
//!   dan XAMPP menaruh htdocs di LUAR folder apache-nya.
//! - Listen hanya sekali, dengan port profil.
//! - LoadModule hanya untuk .so yang ada: modul hilang = Apache gagal start total.
//! - Log galat selalu menyala; log akses hanya bila "Catat log rinci" menyala.
//! - CustomLog/TransferLog/ErrorLog bawaan vendor dikomentari: relatif ke
//!   ServerRoot, jadi Apache menulis ke folder paket di bin apa pun setelan
//!   log rinci. Log hanya ditulis ke folder logs Phoron. Uji:
//!   `log_vendor_tidak_tersalin`.
//! - Beranda lewat Alias /phoron, bukan berkas di folder proyek.
//! - Pengalihan akar ke beranda ditaruh DI DALAM VirtualHost bawaan: RewriteRule
//!   di konteks server tidak diwarisi VirtualHost dan gagal senyap.
//! - 000-default.conf wajib ada dan terurut paling awal: Apache memakai
//!   VirtualHost PERTAMA sebagai jawaban baku, tanpanya localhost dilayani situs
//!   pertama menurut abjad.
//! - RewriteRule beranda mengikat akar PERSIS ("^/?$"); DirectoryIndex akan
//!   membajak semua subfolder.
//!
//! PHP lewat Apache
//! - FastCGI dipakai bila build PHP tidak membawa php*apache2_4.dll - SATU
//!   syarat di satu tempat (`pakai_fastcgi`). Dulu modul proxy dimuat menurut
//!   ThreadSafe sedangkan jalurnya menurut DLL, dan PHP NTS di folder "php-8.3"
//!   membuat httpd menolak start: "Invalid command 'ProxyFCGISetEnvIf'".
//! - SetHandler berakhiran "/" (tanpanya "fcgi://127.0.0.1:9123C:/..." -> 400)
//!   dan SCRIPT_FILENAME dikupas dari jalur yang sudah dipetakan Apache (rakitan
//!   DOCUMENT_ROOT + REQUEST_URI salah untuk setiap Alias, termasuk /phoron).
//! - Titik di alamat IP pada ProxyFCGISetEnvIf harus sampai sebagai "\.".
//! - ProxyFCGISetEnvIf baru ada sejak Apache 2.4.26: direktif tak dikenal
//!   membuat httpd menolak start. Versi yang tak terbaca dianggap cukup baru.
//!
//! PHP per situs (kolam FastCGI)
//! - Satu kolam per VERSI, bukan per situs; port 9124 ke atas.
//! - Tiap kolam memakai `Hasil` SENDIRI untuk php.ini: ekstensi yang diambil
//!   alih untuk kolam tidak boleh masuk ke profil aktif (daftar 8.3 masuk ke
//!   profil 5.6).
//! - CGIPassAuth On: tanpa itu header Authorization (token Bearer) tidak
//!   sampai ke FastCGI.
//! - Kolam ditulis SEBELUM web server: modul proxy dan blok nginx bergantung
//!   pada kolam mana yang jadi.
//! - nginx: location situs kolam dikutip ("my project" tanpa kutip membuat
//!   nginx -t menolak SELURUH berkas).
//!
//! SSL dan vhost
//! - Tanpa sertifikat+kunci, "Listen 443" tidak ditulis (port terbuka tapi tiap
//!   permintaan HTTPS gagal).
//! - Vhost memakai syarat `ssl::exists` (crt, key, CA): dulu cukup .crt, dan .key
//!   yang hilang membuat vhost menunjuk berkas yang tidak ada.
//! - Vhost otomatis milik folder yang sudah dihapus ikut dihapus, kalau tidak
//!   Apache menolak start karena DocumentRoot tidak ada.
//!
//! php.ini
//! - Ditulis ke etc\php\<kunci paket> (kunci, bukan Id: dua PHP XAMPP bernama
//!   "php" dulu berbagi satu php.ini). `folder_php_ini` satu-satunya tempat
//!   keputusan ini; PHPRC php-cgi dan terminal harus menunjuk ke sini.
//! - Dasar: php.ini.sebelum-phoron, lalu php.ini yang SELAMA INI dipakai, baru
//!   bawaan vendor. Mulai dari bawaan vendor membuat short_open_tag diam-diam
//!   Off (CodeIgniter rusak). php.ini keluaran Phoron sendiri dilewati supaya
//!   tidak menumpuk.
//! - Menulis ke folder PHP: php.ini asli dicadangkan SEKALI sebagai
//!   php.ini.sebelum-phoron; folder di luar bin Phoron diperingatkan.
//! - Semua baris extension bawaan dimatikan; yang berlaku hanya daftar profil.
//! - Kunci yang muncul berkali-kali: hanya yang pertama disetel, sisanya
//!   dimatikan.
//! - Setelan opcache sebelum penimpaan profil (profil tetap berhak menimpa).
//!   validate_timestamps=1 dan revalidate_freq=0 adalah SYARAT: tanpanya berkas
//!   yang baru disimpan belum berlaku saat halaman dimuat ulang.
//! - opcache dimuat terpisah dari daftar profil, supaya profil lama ikut cepat.
//! - Profil tanpa daftar ekstensi mengambil alih dari php.ini dasar (atau daftar
//!   baku bila dasar tidak mengaktifkan apa pun) - tanpa itu aplikasi mati
//!   dengan "Call to undefined function mb_strlen()".
//! - Ekstensi tanpa DLL tidak ditulis (tiap permintaan diawali "Unable to load
//!   dynamic library"), tapi TIDAK dihapus dari profil.
//! - oci8/pdo_oci tanpa Instant Client yang sepadan tidak ditulis (lihat
//!   `oracle`).
//! - xdebug dan opcache lewat zend_extension ("Xdebug MUST be loaded as a Zend
//!   extension").
//! - PHP < 7.2 butuh nama DLL lengkap di baris extension.
//! - Daftar DLL: php_x.dllaaa (sengaja dimatikan dengan ganti nama) tidak ikut,
//!   walau pola "*.dll" Windows mencocokkan nama pendek 8.3-nya.
//!
//! MySQL
//! - Folder data per paket, TIDAK PERNAH dipakai bersama (dua MariaDB XAMPP
//!   bernama "mysql" dulu menulis ke satu folder data). Penanda .phoron-pemilik.
//! - Paket unik yang pindah tempat tetap memakai folder lamanya (dulu seluruh
//!   basis data tampak hilang).
//! - Ejaan jalur lain ("/", besar-kecil, garis miring di ujung, nama pendek
//!   8.3) untuk folder yang sama bukan paket lain.
//! - sql_mode dilonggarkan untuk proyek lama bertanggal '0000-00-00'.
//!
//! Nginx
//! - fastcgi_params dengan jalur penuh; bila paket tidak membawanya, Phoron
//!   menulis versi minimal ke etc\nginx (dulu: "CreateFile() ... failed (2)").
//! - server_names_hash_bucket_size dihitung dari nama terpanjang (baku 32
//!   menolak "bandarmasih-mobile-pm-service.test").
//! - "Catat log rinci" dan "beranda di akar" dihormati sama seperti Apache;
//!   beranda di akar hanya untuk server bawaan.
//!
//! Penulisan
//! - Hanya bila isinya berubah (`tulis::write_if_changed`), akhir baris CRLF.

use crate::bin::{apache_module_name, gabung, tanda_asal};
use crate::model::{kamus_set, BinPackage, Profile, Site, Versi};
use crate::oracle;
use crate::paths::{self, fwd};
use crate::protokol::Pesan;
use crate::situs;
use crate::ssl;
use crate::teks::sama_abaikan_besar;
use crate::tulis::{beranda_folder, tandai_web_berubah, write_if_changed, BERANDA_ALIAS};
use std::fs;
use std::io;
use std::path::{Path, PathBuf};

pub const GENERATED_HEADER: &str = "# Berkas ini DIBUAT OTOMATIS oleh Phoron. Suntingan tangan akan hilang\n\
# saat profil di-switch. Ubah lewat profil atau folder etc\\apache2\\alias.";

/// Port php-cgi milik PHP profil; kolam per situs mulai dari port berikutnya.
pub const FASTCGI_PORT: i32 = 9123;

/// Setelan global yang memengaruhi isi config.
#[derive(Debug, Clone, PartialEq)]
pub struct Opsi {
    /// "Tulis php.ini ke dalam folder PHP".
    pub php_ini_ke_folder_php: bool,
    /// "Catat log rinci": log akses web server.
    pub log_akses: bool,
    /// Beranda Phoron di http://localhost/.
    pub beranda_di_akar: bool,
    pub opcache: bool,
    /// "Bisa diakses dari jaringan": Apache/Nginx mendengarkan di semua
    /// antarmuka. Mati = hanya 127.0.0.1 (tanpa dialog Windows Firewall).
    pub akses_jaringan: bool,
}

impl Default for Opsi {
    fn default() -> Self {
        Opsi { php_ini_ke_folder_php: false, log_akses: true, beranda_di_akar: true, opcache: true, akses_jaringan: false }
    }
}

/// Satu versi PHP tambahan yang melayani sebagian situs lewat FastCGI.
#[derive(Debug, Clone, PartialEq)]
pub struct Kolam {
    pub php: BinPackage,
    /// Ekstensi milik versi ITU, bukan milik profil.
    pub ekstensi: Vec<String>,
    pub timpa: Vec<(String, String)>,
    pub situs: Vec<Site>,
    /// Diisi `build`.
    pub port: i32,
    /// Diisi `build`: folder php.ini kolam, untuk PHPRC.
    pub folder_ini: String,
}

impl Kolam {
    pub fn baru(php: BinPackage, ekstensi: Vec<String>, timpa: Vec<(String, String)>, situs: Vec<Site>) -> Self {
        Kolam { php, ekstensi, timpa, situs, port: 0, folder_ini: String::new() }
    }
}

/// Hasil penerapan config.
#[derive(Debug, Clone, PartialEq)]
pub struct Hasil {
    pub httpd_conf: Option<PathBuf>,
    pub my_ini: Option<PathBuf>,
    pub php_ini_dir: Option<String>,
    pub nginx_conf: Option<PathBuf>,
    pub peringatan: Vec<Pesan>,
    pub php_fastcgi: bool,
    pub fastcgi_port: i32,
    /// Config ini benar-benar membuka port HTTPS.
    pub ssl_enabled: bool,
    /// Ekstensi yang diambil alih dari php.ini dasar karena profil belum
    /// punya daftar sendiri; pemanggil menyimpannya ke profil aktif.
    pub ekstensi_diambil: Option<Vec<String>>,
    /// Kolam yang benar-benar ditulis, dengan port dan folder php.ini-nya.
    pub kolam: Vec<Kolam>,
}

impl Default for Hasil {
    fn default() -> Self {
        Hasil {
            httpd_conf: None,
            my_ini: None,
            php_ini_dir: None,
            nginx_conf: None,
            peringatan: Vec::new(),
            php_fastcgi: false,
            fastcgi_port: FASTCGI_PORT,
            ssl_enabled: false,
            ekstensi_diambil: None,
            kolam: Vec::new(),
        }
    }
}

/// Teks Indonesia sebuah `Pesan` (`string.Format` sederhana: "{n}" diganti
/// argumen ke-n). Dipakai untuk pesan yang menyisipkan pesan lain.
pub fn teks(p: &Pesan) -> String {
    let mut s = String::new();
    let mut it = p.kunci.chars().peekable();
    while let Some(c) = it.next() {
        if c == '{' {
            let mut angka = String::new();
            while let Some(d) = it.peek().copied().filter(char::is_ascii_digit) {
                angka.push(d);
                it.next();
            }
            if !angka.is_empty() && it.peek() == Some(&'}') {
                it.next();
                match angka.parse::<usize>().ok().and_then(|i| p.args.get(i)) {
                    Some(a) => s.push_str(a),
                    None => {
                        s.push('{');
                        s.push_str(&angka);
                        s.push('}');
                    }
                }
                continue;
            }
            s.push('{');
            s.push_str(&angka);
            continue;
        }
        s.push(c);
    }
    s
}

// ------------------------------------------------------------------ penyusun

/// Penyusun teks berkas; akhir baris dinormalkan ke CRLF saat ditulis.
#[derive(Default)]
struct Teks(String);

impl Teks {
    fn baris(&mut self, s: &str) {
        self.0.push_str(s);
        self.0.push('\n');
    }
    fn kosong(&mut self) {
        self.0.push('\n');
    }
}

fn jalur(p: &Path) -> String {
    p.to_string_lossy().into_owned()
}

fn fwd_p(p: &Path) -> String {
    fwd(&jalur(p))
}

/// `File.ReadAllText`: UTF-8 (BOM dibuang), UTF-16 bila ber-BOM.
fn baca_teks(p: &Path) -> io::Result<String> {
    let b = fs::read(p)?;
    if b.starts_with(&[0xEF, 0xBB, 0xBF]) {
        return Ok(String::from_utf8_lossy(&b[3..]).into_owned());
    }
    let u16_dari = |le: bool| -> String {
        let w: Vec<u16> = b[2..]
            .as_chunks::<2>()
            .0
            .iter()
            .map(|c| if le { u16::from_le_bytes(*c) } else { u16::from_be_bytes(*c) })
            .collect();
        String::from_utf16_lossy(&w)
    };
    if b.starts_with(&[0xFF, 0xFE]) {
        return Ok(u16_dari(true));
    }
    if b.starts_with(&[0xFE, 0xFF]) {
        return Ok(u16_dari(false));
    }
    Ok(String::from_utf8_lossy(&b).into_owned())
}

/// `File.ReadAllLines`: dipecah pada CRLF, LF, atau CR; baris kosong terakhir
/// sesudah akhiran baris tidak dihitung.
fn baca_baris(p: &Path) -> io::Result<Vec<String>> {
    let t = baca_teks(p)?;
    let mut v = Vec::new();
    let mut cur = String::new();
    let mut it = t.chars().peekable();
    while let Some(c) = it.next() {
        match c {
            '\r' => {
                if it.peek() == Some(&'\n') {
                    it.next();
                }
                v.push(std::mem::take(&mut cur));
            }
            '\n' => v.push(std::mem::take(&mut cur)),
            _ => cur.push(c),
        }
    }
    if !cur.is_empty() {
        v.push(cur);
    }
    Ok(v)
}

/// `StartsWith(awalan, OrdinalIgnoreCase)` untuk awalan ASCII.
fn awal(t: &str, awalan: &str) -> bool {
    t.len() >= awalan.len() && t.is_char_boundary(awalan.len()) && t[..awalan.len()].eq_ignore_ascii_case(awalan)
}

fn lewati_spasi(t: &str) -> &str {
    t.trim_start_matches(char::is_whitespace)
}

/// `^Include(Optional)?\s+"?[A-Za-z]:` tanpa peka besar-kecil.
fn include_mutlak(t: &str) -> bool {
    if !awal(t, "include") {
        return false;
    }
    let mut r = &t[7..];
    if awal(r, "optional") {
        r = &r[8..];
    }
    let s = lewati_spasi(r);
    if s.len() == r.len() {
        return false;
    }
    let s = s.strip_prefix('"').unwrap_or(s);
    let mut c = s.chars();
    matches!((c.next(), c.next()), (Some(h), Some(':')) if h.is_ascii_alphabetic())
}

/// `^(CustomLog|TransferLog|ErrorLog)\s` tanpa peka besar-kecil: direktif log
/// bawaan vendor. `ErrorLogFormat` bukan direktif log dan tidak ikut.
fn log_vendor(t: &str) -> bool {
    ["CustomLog", "TransferLog", "ErrorLog"].iter().any(|d| {
        awal(t, d) && t[d.len()..].starts_with(|c: char| c.is_whitespace())
    })
}

#[cfg(test)]
thread_local! {
    /// Uji yang benar-benar menyalakan server: dengarkan hanya di 127.0.0.1
    /// supaya Windows Firewall tidak memunculkan dialog. Per utas karena uji
    /// berjalan paralel dan uji config lain memeriksa bentuk aslinya.
    static DENGAR_LOKAL: std::cell::Cell<bool> = const { std::cell::Cell::new(false) };
}

/// Uji: config yang ditulis utas ini mendengarkan hanya di 127.0.0.1.
#[cfg(test)]
pub(crate) fn uji_dengar_lokal(nyala: bool) {
    DENGAR_LOKAL.with(|d| d.set(nyala));
}

#[cfg(test)]
fn dengar_lokal() -> bool {
    DENGAR_LOKAL.with(|d| d.get())
}

#[cfg(not(test))]
fn dengar_lokal() -> bool {
    false
}

/// Alamat Listen/listen untuk sebuah port: semua antarmuka hanya bila
/// "Bisa diakses dari jaringan" menyala.
fn dengar(jaringan: bool, port: i32) -> String {
    if jaringan && !dengar_lokal() { port.to_string() } else { format!("127.0.0.1:{port}") }
}

/// `^;?\s*(zend_)?extension\s*=` tanpa peka besar-kecil.
fn baris_ekstensi(t: &str) -> bool {
    let r = lewati_spasi(t.strip_prefix(';').unwrap_or(t));
    let r = if awal(r, "zend_") { &r[5..] } else { r };
    awal(r, "extension") && lewati_spasi(&r[9..]).starts_with('=')
}

/// `^;?\s*([A-Za-z0-9_.]+)\s*=`: kunci php.ini di awal baris.
fn kunci_ini(t: &str) -> Option<&str> {
    let r = lewati_spasi(t.strip_prefix(';').unwrap_or(t));
    let n = r.find(|c: char| !(c.is_ascii_alphanumeric() || c == '_' || c == '.')).unwrap_or(r.len());
    if n == 0 || !lewati_spasi(&r[n..]).starts_with('=') {
        return None;
    }
    Some(&r[..n])
}

fn berisi(v: &[String], x: &str) -> bool {
    v.iter().any(|a| sama_abaikan_besar(a, x))
}

fn cari<'a>(v: &'a [(String, String)], k: &str) -> Option<&'a str> {
    v.iter().find(|(a, _)| sama_abaikan_besar(a, k)).map(|(_, b)| b.as_str())
}

/// Huruf besar per aksara seperti `OrdinalIgnoreCase` (untuk mengurutkan).
fn kunci_urut(s: &str) -> Vec<char> {
    s.chars()
        .map(|c| {
            let mut u = c.to_uppercase();
            match (u.next(), u.next()) {
                (Some(h), None) => h,
                _ => c,
            }
        })
        .collect()
}

// ------------------------------------------------------------------- build

/// Bangun seluruh config untuk profil. `kolam` = versi PHP per situs yang
/// diminta; yang benar-benar jadi ada di `Hasil::kolam`.
#[allow(clippy::too_many_arguments)]
pub fn build(
    profile: &Profile,
    php: Option<&BinPackage>,
    apache: Option<&BinPackage>,
    mysql: Option<&BinPackage>,
    nginx: Option<&BinPackage>,
    sites: &[Site],
    opsi: &Opsi,
    kolam: Vec<Kolam>,
) -> io::Result<Hasil> {
    let mut r = Hasil::default();
    if let Some(php) = php {
        r.php_ini_dir = Some(write_php_ini(profile, php, &mut r, opsi.php_ini_ke_folder_php, opsi.opcache)?);
    }
    if !kolam.is_empty() && profile.pakai_web() {
        siapkan_kolam(profile, kolam, apache, &mut r, opsi.opcache)?;
    }
    if !profile.pakai_web() {
        // Sengaja tanpa web server: tidak ada yang ditulis atau dikeluhkan.
    } else if profile.web_server == "nginx" {
        match nginx {
            Some(n) => r.nginx_conf = Some(write_nginx(profile, n, php, sites, &mut r, opsi)?),
            None => r.peringatan.push(Pesan::baru(
                "Versi Nginx yang dicatat profil tidak ada di komputer ini, \
                 dan tidak ada Nginx lain sebagai gantinya.",
            )),
        }
    } else {
        match apache {
            Some(a) => r.httpd_conf = write_apache(profile, a, php, sites, &mut r, opsi)?,
            None => r.peringatan.push(Pesan::baru(
                "Versi Apache yang dicatat profil tidak ada di komputer ini, \
                 dan tidak ada Apache lain sebagai gantinya.",
            )),
        }
    }
    if let Some(m) = mysql {
        r.my_ini = Some(write_my_ini(profile, m)?);
    }
    let versi = |s: &Site| -> String {
        for k in &r.kolam {
            if k.situs.iter().any(|x| sama_abaikan_besar(&x.path, &s.path)) {
                return k.php.version.clone();
            }
        }
        php.map(|p| p.version.clone()).unwrap_or_default()
    };
    crate::beranda::tulis(Some(profile), sites, php, apache.or(nginx), mysql, Some(&versi))?;
    Ok(r)
}

// ------------------------------------------------------------------ Apache

fn write_apache(
    profile: &Profile,
    apache: &BinPackage,
    php: Option<&BinPackage>,
    sites: &[Site],
    r: &mut Hasil,
    opsi: &Opsi,
) -> io::Result<Option<PathBuf>> {
    let Some(dasar) = pristine_conf(apache)? else {
        r.peringatan.push(Pesan::dengan("httpd.conf bawaan tidak ditemukan di {0}.", &[&apache.id]));
        return Ok(None);
    };

    let mut sb = Teks::default();
    let srvroot = fwd(&apache.path);
    let mut listen = false;
    for line in &dasar {
        let t = line.trim_start();
        if awal(t, "Define SRVROOT") {
            sb.baris(&format!("Define SRVROOT \"{srvroot}\""));
            continue;
        }
        if awal(t, "ServerRoot") {
            sb.baris(&format!("ServerRoot \"{srvroot}\""));
            continue;
        }
        if awal(t, "Listen ") {
            if !listen {
                sb.baris(&format!("Listen {}", dengar(opsi.akses_jaringan, profile.http_port)));
                listen = true;
            }
            continue;
        }
        if include_mutlak(t) {
            continue;
        }
        if awal(t, "DocumentRoot") {
            sb.baris(&format!("# {line}   # dinonaktifkan Phoron - lihat blok di bawah"));
            continue;
        }
        // Log bawaan vendor ("logs/access.log", relatif ke ServerRoot) menulis
        // ke folder paket di bin - apa pun setelan log rinci. Log Phoron
        // ditulis di blok penutup, ke folder logs Phoron.
        if log_vendor(t) {
            sb.baris(&format!("# {line}   # dinonaktifkan Phoron - log ditulis ke folder logs Phoron"));
            continue;
        }
        sb.baris(line);
    }
    if !listen {
        sb.baris(&format!("Listen {}", dengar(opsi.akses_jaringan, profile.http_port)));
    }

    sb.kosong();
    sb.baris("### ================= Phoron =================");
    sb.baris("# Blok ini ditambahkan di akhir dengan sengaja: direktif Apache yang");
    sb.baris("# muncul belakangan menimpa yang di atasnya, jadi bawaan vendor tidak");
    sb.baris("# perlu diobrak-abrik sama sekali.");
    for m in apache_modules(php, !r.kolam.is_empty()) {
        let so = gabung(&gabung(&apache.path, "modules"), &format!("mod_{m}.so"));
        if Path::new(&so).is_file() {
            sb.baris(&format!("LoadModule {m}_module modules/mod_{m}.so"));
        }
    }

    let doc_root = fwd(&situs::document_root(Some(profile)));
    let tmp = paths::tmp();
    let logs = paths::logs();
    sb.baris(&format!("DefaultRuntimeDir \"{}/\"", fwd_p(&tmp)));
    sb.baris(&format!("PidFile \"{}\"", fwd_p(&tmp.join("httpd.pid"))));
    sb.baris(&format!("ErrorLog \"{}\"", fwd_p(&logs.join("apache-error.log"))));
    if opsi.log_akses {
        sb.baris(&format!("CustomLog \"{}\" common", fwd_p(&logs.join("apache-access.log"))));
    } else {
        sb.baris("# Log akses dimatikan (Pengaturan > Catat log rinci).");
    }
    sb.baris(&format!("ServerName localhost:{}", profile.http_port));
    sb.baris("ServerSignature Off");
    sb.baris(&format!("DocumentRoot \"{doc_root}\""));
    sb.baris(&format!("<Directory \"{doc_root}\">"));
    sb.baris("    Options Indexes FollowSymLinks ExecCGI");
    sb.baris("    AllowOverride All");
    sb.baris("    Require all granted");
    sb.baris("</Directory>");
    sb.baris("<IfModule dir_module>");
    sb.baris("    DirectoryIndex index.php index.html index.htm");
    sb.baris("</IfModule>");
    sb.baris("AddDefaultCharset UTF-8");
    let beranda = fwd_p(&beranda_folder());
    sb.baris(&format!("Alias {BERANDA_ALIAS} \"{beranda}\""));
    sb.baris(&format!("<Directory \"{beranda}\">"));
    sb.baris("    Options FollowSymLinks ExecCGI");
    sb.baris("    AllowOverride None");
    sb.baris("    Require all granted");
    sb.baris("    DirectoryIndex index.php");
    sb.baris("</Directory>");

    let etc_apache = paths::etc_apache();
    sb.baris(&format!("Include \"{}\"", fwd_p(&etc_apache.join("mod_php.conf"))));
    sb.baris(&format!("Include \"{}\"", fwd_p(&etc_apache.join("ssl.conf"))));
    sb.baris(&format!("IncludeOptional \"{}/*.conf\"", fwd_p(&etc_apache.join("alias"))));
    sb.baris(&format!("IncludeOptional \"{}/*.conf\"", fwd_p(&paths::sites_enabled())));

    let conf = etc_apache.join("httpd.conf");
    fs::create_dir_all(etc_apache.join("alias"))?;
    write_if_changed(&conf, &sb.0)?;

    write_mod_php(apache, php, r)?;
    write_ssl_conf(profile, r, opsi.akses_jaringan)?;
    write_vhosts(profile, sites, opsi.beranda_di_akar)?;
    Ok(Some(conf))
}

/// Salinan pristine (conf\original) lebih disukai.
fn pristine_conf(apache: &BinPackage) -> io::Result<Option<Vec<String>>> {
    for rel in [r"conf\original\httpd.conf", r"conf\httpd.conf"] {
        let p = gabung(&apache.path, rel);
        if Path::new(&p).is_file() {
            return baca_baris(Path::new(&p)).map(Some);
        }
    }
    Ok(None)
}

/// ProxyFCGISetEnvIf ada sejak Apache 2.4.26; versi tak terbaca dianggap baru.
fn proxy_fcgi_setenvif_ada(apache: Option<&BinPackage>) -> bool {
    let Some(a) = apache else { return true };
    let v = a.parsed();
    v.0 == 0 || v >= Versi(2, 4, 26, -1)
}

/// Apache harus melayani PHP ini lewat FastCGI: build-nya tanpa modul Apache.
/// SATU-SATUNYA tempat keputusan ini diambil.
pub fn pakai_fastcgi(php: Option<&BinPackage>) -> bool {
    php.is_some_and(|p| p.apache_module_dll.is_empty())
}

/// Baris yang mengupas "proxy:fcgi://127.0.0.1:<port>/" dari SCRIPT_FILENAME;
/// satu baris melayani php-cgi profil dan semua kolam.
pub fn setel_nama_berkas_fcgi() -> &'static str {
    r#"ProxyFCGISetEnvIf "reqenv('SCRIPT_FILENAME') =~ m#^proxy:fcgi://127\.0\.0\.1:\d+/(.*)$#" SCRIPT_FILENAME "$1""#
}

fn siapkan_kolam(
    profile: &Profile,
    kolam: Vec<Kolam>,
    apache: Option<&BinPackage>,
    r: &mut Hasil,
    opcache: bool,
) -> io::Result<()> {
    if profile.web_server != "nginx" && !proxy_fcgi_setenvif_ada(apache) {
        r.peringatan.push(Pesan::dengan(
            "Versi PHP per situs butuh Apache 2.4.26 ke atas; Apache {0} belum mengenal \
             ProxyFCGISetEnvIf. Semua situs memakai PHP profil.",
            &[apache.map_or("ini", |a| a.version.as_str())],
        ));
        return Ok(());
    }
    let mut port = r.fastcgi_port + 1;
    for mut k in kolam {
        if k.situs.is_empty() {
            continue;
        }
        if !Path::new(&gabung(&k.php.path, "php-cgi.exe")).is_file() {
            let folder: Vec<&str> = k.situs.iter().map(|s| s.folder.as_str()).collect();
            r.peringatan.push(Pesan::dengan(
                "php-cgi.exe tidak ada di {0}, jadi {1} memakai PHP profil.",
                &[&k.php.id, &folder.join(", ")],
            ));
            continue;
        }
        let pk = Profile {
            name: format!("{} - PHP {} per situs", profile.name, k.php.version),
            php_id: k.php.nilai_simpan().to_string(),
            php_extensions: k.ekstensi.clone(),
            php_ini_overrides: k.timpa.clone(),
            ..Profile::default()
        };
        // Hasil SENDIRI: ekstensi yang diambil alih tidak boleh bocor ke profil.
        let mut rk = Hasil::default();
        k.folder_ini = write_php_ini(&pk, &k.php, &mut rk, false, opcache)?;
        for w in rk.peringatan {
            r.peringatan.push(Pesan::dengan("PHP {0} (per situs): {1}", &[&k.php.version, &teks(&w)]));
        }
        k.port = port;
        port += 1;
        r.kolam.push(k);
    }
    Ok(())
}

/// Port kolam yang melayani situs ini, atau 0 bila ia ikut PHP profil.
fn port_kolam(r: &Hasil, s: &Site) -> i32 {
    r.kolam
        .iter()
        .find(|k| k.situs.iter().any(|x| sama_abaikan_besar(&x.path, &s.path)))
        .map_or(0, |k| k.port)
}

fn tulis_kolam_apache(sb: &mut Teks, r: &Hasil, setel_sudah_ada: bool) {
    if r.kolam.is_empty() {
        return;
    }
    sb.kosong();
    sb.baris("# --- Versi PHP per situs (FastCGI), berdampingan dengan PHP profil ---");
    for k in &r.kolam {
        for s in &k.situs {
            sb.baris(&format!("# {} -> PHP {}", s.folder, k.php.version));
            sb.baris(&format!("<Directory \"{}\">", fwd(&s.path)));
            sb.baris("    CGIPassAuth On");
            sb.baris("    <FilesMatch \\.php$>");
            sb.baris(&format!("        SetHandler \"proxy:fcgi://127.0.0.1:{}/\"", k.port));
            sb.baris("    </FilesMatch>");
            sb.baris("</Directory>");
        }
    }
    if !setel_sudah_ada {
        sb.baris(setel_nama_berkas_fcgi());
    }
}

fn apache_modules(php: Option<&BinPackage>, ada_kolam: bool) -> Vec<&'static str> {
    let mut m = vec!["rewrite", "deflate", "expires", "headers", "ssl", "socache_shmcb", "vhost_alias"];
    if pakai_fastcgi(php) || ada_kolam {
        m.push("proxy");
        m.push("proxy_fcgi");
    }
    m
}

fn write_mod_php(apache: &BinPackage, php: Option<&BinPackage>, r: &mut Hasil) -> io::Result<()> {
    let path = paths::etc_apache().join("mod_php.conf");
    let mut sb = Teks::default();
    sb.baris(GENERATED_HEADER);
    let Some(php) = php else {
        sb.baris("# Profil ini tidak memakai PHP.");
        tulis_kolam_apache(&mut sb, r, false);
        write_if_changed(&path, &sb.0)?;
        return Ok(());
    };

    let mut setel_sudah_ada = false;
    if !pakai_fastcgi(Some(php)) {
        sb.baris(&format!("LoadModule {} \"{}\"", apache_module_name(Some(php)), fwd(&php.apache_module_dll)));
        let dir = r.php_ini_dir.clone().unwrap_or_else(|| php.path.clone());
        sb.baris(&format!("PHPIniDir \"{}\"", fwd(&dir)));
        sb.baris("<IfModule mime_module>");
        sb.baris("    AddType application/x-httpd-php .php");
        sb.baris("    AddType application/x-httpd-php-source .phps");
        sb.baris("</IfModule>");
    } else {
        r.php_fastcgi = true;
        for b in KETERANGAN_FASTCGI {
            sb.baris(b);
        }
        sb.baris("<FilesMatch \\.php$>");
        sb.baris(&format!("    SetHandler \"proxy:fcgi://127.0.0.1:{}/\"", r.fastcgi_port));
        sb.baris("</FilesMatch>");
        if proxy_fcgi_setenvif_ada(Some(apache)) {
            sb.baris(setel_nama_berkas_fcgi());
            setel_sudah_ada = true;
        } else {
            r.peringatan.push(Pesan::dengan(
                "Apache {0} belum mengenal ProxyFCGISetEnvIf (ada sejak 2.4.26), jadi PHP \
                 non-thread-safe tidak bisa dilayani lewat FastCGI di Windows. \
                 Pilih build PHP thread-safe, atau Apache yang lebih baru.",
                &[&apache.version],
            ));
        }
        if !Path::new(&gabung(&php.path, "php-cgi.exe")).is_file() {
            r.peringatan.push(Pesan::dengan(
                "php-cgi.exe tidak ada di {0}; PHP tidak akan jalan lewat Apache.",
                &[&php.id],
            ));
        }
    }
    tulis_kolam_apache(&mut sb, r, setel_sudah_ada);
    write_if_changed(&path, &sb.0)?;
    Ok(())
}

/// Penjelasan di mod_php.conf untuk orang yang membukanya.
const KETERANGAN_FASTCGI: &[&str] = &[
    "# Build PHP ini NTS (tanpa modul Apache) - dilayani lewat FastCGI.",
    "#",
    "# DUA HAL DI BAWAH KHAS WINDOWS, DAN TANPA KEDUANYA TIDAK SATU PUN",
    "# BERKAS .php BISA DIBUKA.",
    "#",
    "# Garis miring di akhir alamat. Apache menyambung jalur berkas langsung",
    "# ke belakang alamat itu. Di Linux jalurnya diawali \"/\" sehingga yang",
    "# terbentuk sah; di Windows ia diawali \"C:\", jadi jadinya",
    "# \"fcgi://127.0.0.1:9123C:/...\" dan Apache menjawab 400 dengan",
    "# keterangan \"URI cannot be parsed\" yang tidak menyebut PHP sama sekali.",
    "#",
    "# SCRIPT_FILENAME yang disetel sendiri. Dengan garis miring tadi, Apache",
    "# mengirim SELURUH alamat proxy sebagai nama berkas yang harus dijalankan",
    "# PHP - \"proxy:fcgi://127.0.0.1:9123/C:/...\" - dan php-cgi menjawab",
    "# \"No input file specified\" untuk setiap permintaan.",
    "#",
    "# Awalan proxy-nya DIKUPAS dari jalur yang sudah dipetakan Apache, bukan",
    "# dirakit ulang dari DOCUMENT_ROOT + REQUEST_URI. Rakitan itu salah untuk",
    "# setiap Alias - termasuk /phoron, yang dilayani dari etc/dashboard dan",
    "# bukan dari folder proyek - dan pernah membuat beranda Phoron sendiri",
    "# menjawab \"No input file specified\".",
];

fn write_ssl_conf(profile: &Profile, r: &mut Hasil, jaringan: bool) -> io::Result<()> {
    let crt = ssl::crt_path();
    let key = ssl::key_path();
    let mut sb = Teks::default();
    sb.baris(GENERATED_HEADER);
    sb.baris("<IfModule ssl_module>");
    if crt.is_file() && key.is_file() {
        r.ssl_enabled = true;
        sb.baris(&format!("    Listen {}", dengar(jaringan, profile.https_port)));
        sb.baris("    SSLCipherSuite HIGH:MEDIUM:!MD5:!RC4:!3DES");
        sb.baris("    SSLProtocol all -SSLv3");
        sb.baris(&format!("    SSLSessionCache \"shmcb:{}(512000)\"", fwd_p(&paths::tmp().join("ssl_scache"))));
        sb.baris(&format!("    SSLCertificateFile \"{}\"", fwd_p(&crt)));
        sb.baris(&format!("    SSLCertificateKeyFile \"{}\"", fwd_p(&key)));
    } else {
        sb.baris("    # Sertifikat belum dibuat; HTTPS dimatikan.");
    }
    sb.baris("</IfModule>");
    write_if_changed(&paths::etc_apache().join("ssl.conf"), &sb.0)?;
    Ok(())
}

fn doc_root_situs(s: &Site) -> &str {
    if s.doc_root.is_empty() { &s.path } else { &s.doc_root }
}

fn write_vhosts(profile: &Profile, sites: &[Site], beranda_di_akar: bool) -> io::Result<()> {
    let dir = paths::sites_enabled();
    fs::create_dir_all(&dir)?;
    let ssl = ssl::exists();
    write_default_vhost(profile, ssl, beranda_di_akar)?;

    let mut wanted: Vec<String> = Vec::new();
    for s in sites {
        let file = situs::vhost_path(s);
        wanted.push(file.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default());
        let mut sb = Teks::default();
        sb.baris(GENERATED_HEADER);
        sb.baris(&format!("define ROOT \"{}\"", fwd(doc_root_situs(s))));
        sb.baris(&format!("define SITE \"{}\"", s.host_name));
        sb.kosong();
        sb.baris(&vhost_block(profile.http_port, false));
        if ssl {
            sb.baris(&vhost_block(profile.https_port, true));
        }
        write_if_changed(&file, &sb.0)?;
    }

    // Vhost milik folder yang sudah dihapus ikut hilang.
    if let Ok(rd) = fs::read_dir(&dir) {
        for e in rd.flatten() {
            let nama = e.file_name().to_string_lossy().into_owned();
            let auto = nama.len() >= 10 && awal(&nama, "auto.") && {
                let n = nama.len();
                nama.is_char_boundary(n - 5) && nama[n - 5..].eq_ignore_ascii_case(".conf")
            };
            if auto && e.file_type().is_ok_and(|t| t.is_file()) && !berisi(&wanted, &nama) && fs::remove_file(e.path()).is_ok() {
                tandai_web_berubah();
            }
        }
    }
    Ok(())
}

fn ssl_baris(sb: &mut Teks) {
    sb.baris("    SSLEngine on");
    sb.baris(&format!("    SSLCertificateFile \"{}\"", fwd_p(&ssl::crt_path())));
    sb.baris(&format!("    SSLCertificateKeyFile \"{}\"", fwd_p(&ssl::key_path())));
}

fn write_default_vhost(profile: &Profile, ssl: bool, beranda_di_akar: bool) -> io::Result<()> {
    let root = fwd(&situs::document_root(Some(profile)));
    let mut sb = Teks::default();
    sb.baris(GENERATED_HEADER);
    sb.baris(&format!("define ROOT \"{root}\""));
    sb.kosong();
    sb.baris(&default_block(profile.http_port, false, beranda_di_akar));
    if ssl {
        sb.baris(&default_block(profile.https_port, true, beranda_di_akar));
    }
    // Awalan "000-" menjamin urutannya sebelum semua "auto.*.conf".
    write_if_changed(&paths::sites_enabled().join("000-default.conf"), &sb.0)?;
    Ok(())
}

fn default_block(port: i32, ssl: bool, beranda_di_akar: bool) -> String {
    let mut sb = Teks::default();
    sb.baris(&format!("<VirtualHost _default_:{port}>"));
    sb.baris("    DocumentRoot \"${ROOT}\"");
    sb.baris("    ServerName localhost");
    if beranda_di_akar {
        sb.baris("    <IfModule rewrite_module>");
        sb.baris("        RewriteEngine On");
        sb.baris(&format!("        RewriteRule \"^/?$\" \"{BERANDA_ALIAS}/index.php\" [PT,L]"));
        sb.baris("    </IfModule>");
    }
    sb.baris("    <Directory \"${ROOT}\">");
    sb.baris("        Options Indexes FollowSymLinks ExecCGI");
    sb.baris("        AllowOverride All");
    sb.baris("        Require all granted");
    sb.baris("    </Directory>");
    if ssl {
        ssl_baris(&mut sb);
    }
    sb.baris("</VirtualHost>");
    sb.0
}

fn vhost_block(port: i32, ssl: bool) -> String {
    let mut sb = Teks::default();
    sb.baris(&format!("<VirtualHost *:{port}>"));
    sb.baris("    DocumentRoot \"${ROOT}\"");
    sb.baris("    ServerName ${SITE}");
    sb.baris("    ServerAlias *.${SITE}");
    sb.baris("    <Directory \"${ROOT}\">");
    sb.baris("        Options Indexes FollowSymLinks ExecCGI");
    sb.baris("        AllowOverride All");
    sb.baris("        Require all granted");
    sb.baris("    </Directory>");
    if ssl {
        ssl_baris(&mut sb);
    }
    sb.baris("</VirtualHost>");
    sb.0
}

// --------------------------------------------------------------------- PHP

/// Folder php.ini profil - SATU-SATUNYA tempat keputusan ini diambil.
pub fn folder_php_ini(php: &BinPackage, ke_folder_php: bool) -> String {
    if ke_folder_php {
        php.path.clone()
    } else {
        jalur(&paths::etc().join("php").join(php.kunci()))
    }
}

/// Setelan opcache Phoron. DUA BARIS TERAKHIR ADALAH SYARATNYA: tanpanya berkas
/// yang baru disimpan bisa belum berlaku saat halaman dimuat ulang.
pub const SETELAN_OPCACHE: &[(&str, &str)] = &[
    ("opcache.enable", "1"),
    // CLI dibiarkan mati: umurnya sependek satu perintah.
    ("opcache.enable_cli", "0"),
    ("opcache.memory_consumption", "128"),
    ("opcache.interned_strings_buffer", "8"),
    ("opcache.max_accelerated_files", "10000"),
    ("opcache.validate_timestamps", "1"),
    ("opcache.revalidate_freq", "0"),
];

/// Daftar baku untuk PHP yang php.ini-nya belum mengaktifkan apa pun. exif
/// sesudah mbstring; gd2 dan gd sama-sama disebut, yang tanpa DLL tersaring.
pub const BAKU_DISARANKAN: &[&str] = &[
    "curl", "fileinfo", "openssl", "mbstring", "exif", "intl", "gd2", "gd", "mysqli", "pdo_mysql", "pdo_sqlite",
    "sqlite3", "zip",
];

/// Ekstensi yang HARUS dimuat lewat zend_extension.
const EKSTENSI_ZEND: &[&str] = &["opcache", "xdebug"];

const PENANDA_BUATAN_PHORON: &str = "dibuat otomatis oleh Phoron";

/// Tulis php.ini profil dan kembalikan foldernya (untuk PHPIniDir/PHPRC).
pub fn write_php_ini(
    profile: &Profile,
    php: &BinPackage,
    r: &mut Hasil,
    ke_folder_php: bool,
    opcache: bool,
) -> io::Result<String> {
    let dir = folder_php_ini(php, ke_folder_php);
    fs::create_dir_all(&dir)?;
    let target = PathBuf::from(gabung(&dir, "php.ini"));

    let mut cadangan: Option<String> = None;
    if ke_folder_php {
        let c = gabung(&php.path, "php.ini.sebelum-phoron");
        if target.is_file() && !Path::new(&c).is_file() {
            if let Err(e) = fs::copy(&target, &c) {
                r.peringatan.push(Pesan::dengan("Gagal mencadangkan php.ini asli: {0}", &[&e.to_string()]));
            }
        }
        if !di_bawah(&php.path, &paths::bin()) {
            r.peringatan.push(Pesan::dengan(
                "php.ini ditulis ke {0}, folder yang TIDAK milik Phoron. \
                 Pengelola lain (mis. Laragon) memakai berkas yang sama.",
                &[&php.path],
            ));
        }
        cadangan = Some(c);
    }

    let base = match php_ini_template(php, cadangan.as_deref())? {
        Some(t) => t,
        None => {
            r.peringatan.push(Pesan::dengan("Tidak ada php.ini contoh di {0}; dibuat dari nol.", &[&php.id]));
            String::new()
        }
    };

    let mut lines: Vec<String> = base.split('\n').map(|l| l.trim_end_matches('\r').to_string()).collect();
    let ext_dir = fwd(&gabung(&php.path, "ext"));
    let tmp = format!("\"{}\"", fwd_p(&paths::tmp()));

    let mut set: Vec<(String, String)> = vec![
        ("extension_dir".into(), format!("\"{ext_dir}\"")),
        ("error_log".into(), format!("\"{}\"", fwd_p(&paths::logs().join("php-error.log")))),
        ("upload_tmp_dir".into(), tmp.clone()),
        ("sys_temp_dir".into(), tmp.clone()),
        ("session.save_path".into(), tmp),
        ("date.timezone".into(), "Asia/Makassar".into()),
        ("display_errors".into(), "On".into()),
        ("log_errors".into(), "On".into()),
    ];
    let cacert = paths::etc_ssl().join("cacert.pem");
    if cacert.is_file() {
        let c = format!("\"{}\"", fwd_p(&cacert));
        kamus_set(&mut set, "curl.cainfo", &c);
        kamus_set(&mut set, "openssl.cafile", &c);
    }
    if opcache && ada_opcache(Some(php)) {
        for (k, v) in SETELAN_OPCACHE {
            kamus_set(&mut set, k, v);
        }
    }
    // SESUDAH opcache: profil tetap berhak menimpanya.
    for (k, v) in &profile.php_ini_overrides {
        kamus_set(&mut set, k, v);
    }

    let mut applied: Vec<String> = Vec::new();
    for line in lines.iter_mut() {
        let t = line.trim_start();
        if baris_ekstensi(t) {
            if !t.starts_with(';') {
                line.insert(0, ';');
            }
            continue;
        }
        let Some(key) = kunci_ini(t) else { continue };
        let Some(val) = cari(&set, key) else { continue };
        if berisi(&applied, key) {
            if !t.starts_with(';') {
                line.insert(0, ';');
            }
            continue;
        }
        let baru = format!("{key} = {val}");
        applied.push(key.to_string());
        *line = baru;
    }

    let mut sb = Teks::default();
    sb.baris(&format!("; php.ini untuk profil \"{}\" - dibuat otomatis oleh Phoron.", profile.name));
    sb.baris("; Sunting lewat Phoron; berkas ini ditulis ulang tiap kali profil dipakai.");
    sb.baris(&lines.join("\r\n"));
    sb.kosong();
    sb.baris("; --- disetel Phoron ---");
    for (k, v) in &set {
        if !berisi(&applied, k) {
            sb.baris(&format!("{k} = {v}"));
        }
    }

    // Profil tanpa daftar ekstensi mengambil alih dari php.ini dasar.
    let mut daftar: Vec<String> = profile.php_extensions.clone();
    if daftar.is_empty() {
        let tersedia = available_extensions(Some(php));
        let mut dari_dasar: Vec<String> = ekstensi_aktif(&base).into_iter().filter(|x| berisi(&tersedia, x)).collect();
        if dari_dasar.is_empty() {
            dari_dasar = BAKU_DISARANKAN.iter().filter(|x| berisi(&tersedia, x)).map(|x| x.to_string()).collect();
        }
        if !dari_dasar.is_empty() {
            daftar = dari_dasar.clone();
            r.ekstensi_diambil = Some(dari_dasar);
        }
    }

    let o = oracle::periksa(Some(php), None);
    if !o.layak {
        let kena: Vec<String> = daftar.iter().filter(|x| oracle::adalah_ekstensi_oracle(x)).cloned().collect();
        if !kena.is_empty() {
            daftar.retain(|x| !oracle::adalah_ekstensi_oracle(x));
            let alasan = o.pesan.as_ref().map(teks).unwrap_or_default();
            r.peringatan.push(Pesan::dengan("{0} tidak ditulis ke php.ini. {1}", &[&kena.join(", "), &alasan]));
        }
    }

    let ada_dll = available_extensions(Some(php));
    if !ada_dll.is_empty() {
        let lewat: Vec<String> = daftar.iter().filter(|x| !berisi(&ada_dll, x)).cloned().collect();
        if !lewat.is_empty() {
            daftar.retain(|x| berisi(&ada_dll, x));
            r.peringatan.push(Pesan::dengan(
                "Ekstensi ini tidak ada DLL-nya di {0}, jadi tidak ditulis ke php.ini: {1}. \
                 Daftar di profil dibiarkan utuh.",
                &[&php.id, &lewat.join(", ")],
            ));
        }
    }

    sb.kosong();
    sb.baris("; --- ekstensi profil ---");
    let mut sudah: Vec<String> = Vec::new();
    for ext in &daftar {
        if berisi(&sudah, ext) {
            continue;
        }
        sudah.push(ext.clone());
        let dll = gabung(&gabung(&php.path, "ext"), &format!("php_{ext}.dll"));
        if !Path::new(&dll).is_file() {
            r.peringatan.push(Pesan::dengan("Ekstensi {0} tidak ada di {1} - dilewati.", &[ext, &php.id]));
            continue;
        }
        let d = if EKSTENSI_ZEND.iter().any(|z| sama_abaikan_besar(z, ext)) { "zend_extension" } else { "extension" };
        sb.baris(&format!("{d} = {}", extension_value(php, ext)));
    }

    // opcache dimuat terpisah dari daftar profil.
    let terdaftar = daftar.iter().any(|x| x.eq_ignore_ascii_case("opcache"));
    if opcache && !terdaftar && ada_opcache(Some(php)) {
        sb.kosong();
        sb.baris("; --- opcache (setelan Phoron, bukan daftar ekstensi profil) ---");
        sb.baris(&format!("zend_extension = {}", extension_value(php, "opcache")));
    }

    write_if_changed(&target, &sb.0)?;
    Ok(dir)
}

/// PHP 7.2 ke atas menerima nama telanjang; sebelumnya nama DLL lengkap.
pub fn extension_value(php: &BinPackage, ext: &str) -> String {
    let v = php.parsed();
    if v.0 > 7 || (v.0 == 7 && v.1 >= 2) { ext.to_string() } else { format!("php_{ext}.dll") }
}

/// php.ini dasar: cadangan Phoron, php.ini yang selama ini dipakai, lalu
/// bawaan vendor. php.ini keluaran Phoron sendiri dilewati.
fn php_ini_template(php: &BinPackage, cadangan: Option<&str>) -> io::Result<Option<String>> {
    if let Some(c) = cadangan.filter(|c| Path::new(c).is_file()) {
        return baca_teks(Path::new(c)).map(Some);
    }
    let asli = gabung(&php.path, "php.ini.sebelum-phoron");
    if Path::new(&asli).is_file() {
        return baca_teks(Path::new(&asli)).map(Some);
    }
    for nama in ["php.ini", "php.ini-development", "php.ini-production"] {
        let p = gabung(&php.path, nama);
        if !Path::new(&p).is_file() {
            continue;
        }
        let t = baca_teks(Path::new(&p))?;
        if nama == "php.ini" {
            // 300 aksara UTF-16 pertama, seperti Substring di C#.
            let mut n = 0usize;
            let kepala: String = t
                .chars()
                .take_while(|c| {
                    n += c.len_utf16();
                    n <= 300
                })
                .collect();
            if kepala.contains(PENANDA_BUATAN_PHORON) {
                continue;
            }
        }
        return Ok(Some(t));
    }
    Ok(None)
}

/// Folder `path` berada di dalam `root`.
fn di_bawah(path: &str, root: &Path) -> bool {
    let (Ok(a), Ok(b)) = (std::path::absolute(path), std::path::absolute(root)) else { return false };
    let a = format!("{}\\", jalur(&a).trim_end_matches('\\'));
    let b = format!("{}\\", jalur(&b).trim_end_matches('\\'));
    a.len() >= b.len() && a.is_char_boundary(b.len()) && sama_abaikan_besar(&a[..b.len()], &b)
}

/// Ekstensi AKTIF di teks php.ini, tanpa awalan php_ dan akhiran .dll, urutan
/// dipertahankan (exif butuh mbstring).
pub fn ekstensi_aktif(ini: &str) -> Vec<String> {
    let mut hasil: Vec<String> = Vec::new();
    for baris in ini.split('\n') {
        let t = baris.trim_start();
        if t.starts_with(';') {
            continue;
        }
        let r = if awal(t, "zend_") { &t[5..] } else { t };
        if !awal(r, "extension") {
            continue;
        }
        let r = lewati_spasi(&r[9..]);
        let Some(r) = r.strip_prefix('=') else { continue };
        let r = lewati_spasi(r);
        let r = r.strip_prefix('"').unwrap_or(r);
        let akhir = r.find(['"', ';', '\r', '\n']).unwrap_or(r.len());
        let mut nama = r[..akhir].trim();
        if nama.len() >= 4 && nama.is_char_boundary(nama.len() - 4) && nama[nama.len() - 4..].eq_ignore_ascii_case(".dll") {
            nama = &nama[..nama.len() - 4];
        }
        if awal(nama, "php_") {
            nama = &nama[4..];
        }
        let nama = nama.to_lowercase();
        if !nama.is_empty() && !hasil.contains(&nama) {
            hasil.push(nama);
        }
    }
    hasil
}

/// Ekstensi aktif di php.ini dasar paket (tombol "Ambil dari php.ini asli").
pub fn ekstensi_dari_php_ini_dasar(php: Option<&BinPackage>) -> Vec<String> {
    let Some(php) = php else { return Vec::new() };
    php_ini_template(php, None).ok().flatten().map(|t| ekstensi_aktif(&t)).unwrap_or_default()
}

/// Build PHP ini membawa php_opcache.dll.
pub fn ada_opcache(php: Option<&BinPackage>) -> bool {
    php.is_some_and(|p| !p.path.is_empty() && Path::new(&gabung(&p.path, r"ext\php_opcache.dll")).is_file())
}

/// Ekstensi untuk profil baru: yang aktif di php.ini paket, atau daftar baku;
/// selalu disaring ke DLL yang ada.
pub fn ekstensi_disarankan(php: Option<&BinPackage>) -> Vec<String> {
    if php.is_none() {
        return Vec::new();
    }
    let dari_ini = ekstensi_dari_php_ini_dasar(php);
    let calon: Vec<String> =
        if dari_ini.is_empty() { BAKU_DISARANKAN.iter().map(|s| s.to_string()).collect() } else { dari_ini };
    let tersedia = available_extensions(php);
    calon.into_iter().filter(|x| berisi(&tersedia, x)).collect()
}

/// Ekstensi yang tersedia di build PHP (nama tanpa php_), terurut.
pub fn available_extensions(php: Option<&BinPackage>) -> Vec<String> {
    let Some(php) = php else { return Vec::new() };
    let Ok(rd) = fs::read_dir(gabung(&php.path, "ext")) else { return Vec::new() };
    let mut v: Vec<String> = Vec::new();
    for e in rd.flatten() {
        if !e.file_type().is_ok_and(|t| t.is_file()) {
            continue;
        }
        let nama = e.file_name().to_string_lossy().into_owned();
        // Akhiran harus PERSIS .dll: php_x.dllaaa sengaja dimatikan orang.
        let Some(titik) = nama.rfind('.') else { continue };
        if !nama[titik..].eq_ignore_ascii_case(".dll") || !awal(&nama, "php_") || titik < 4 {
            continue;
        }
        let x = nama[4..titik].to_lowercase();
        if !berisi(&v, &x) {
            v.push(x);
        }
    }
    v.sort_by_key(|x| kunci_urut(x));
    v
}

// ------------------------------------------------------------------- MySQL

pub fn write_my_ini(profile: &Profile, mysql: &BinPackage) -> io::Result<PathBuf> {
    let data_dir = mysql_data_dir(mysql);
    let path = paths::etc_mysql().join("my.ini");
    let mut sb = Teks::default();
    sb.baris(&format!("# Dibuat otomatis oleh Phoron untuk profil \"{}\".", profile.name));
    sb.baris("[client]");
    sb.baris(&format!("port={}", profile.mysql_port));
    sb.baris("default-character-set=utf8mb4");
    sb.kosong();
    sb.baris("[mysqld]");
    sb.baris(&format!("port={}", profile.mysql_port));
    // Selalu lokal, apa pun "Bisa diakses dari jaringan": untuk mencoba situs
    // dari HP cukup web server-nya, sedangkan MySQL pengembangan lazimnya
    // root tanpa sandi - terbuka ke seisi Wi-Fi bukan bawaan yang pantas.
    sb.baris("bind-address=127.0.0.1");
    sb.baris(&format!("basedir=\"{}\"", fwd(&mysql.path)));
    sb.baris(&format!("datadir=\"{}\"", fwd(&data_dir)));
    sb.baris(&format!("tmpdir=\"{}\"", fwd_p(&paths::tmp())));
    sb.baris(&format!("log-error=\"{}\"", fwd_p(&paths::logs().join("mysql-error.log"))));
    sb.baris("character-set-server=utf8mb4");
    sb.baris("collation-server=utf8mb4_general_ci");
    sb.baris("max_allowed_packet=64M");
    sb.baris("# sql_mode dilonggarkan: banyak proyek PHP lama menulis kolom");
    sb.baris("# tanggal '0000-00-00' yang ditolak mode ketat bawaan MySQL 5.7+.");
    sb.baris("sql_mode=\"NO_ENGINE_SUBSTITUTION\"");
    write_if_changed(&path, &sb.0)?;
    Ok(path)
}

const BERKAS_PEMILIK: &str = ".phoron-pemilik";

/// Folder data MySQL paket ini - TIDAK PERNAH dipakai bersama paket lain.
/// Urutan: folder yang tercatat miliknya; folder bertanda asal / folder lama
/// paket unik yang pindah; folder kuncinya selama belum dimiliki; terakhir
/// folder bertanda asal. Tidak ada data yang dipindah atau dihapus.
pub fn mysql_data_dir(mysql: &BinPackage) -> String {
    let data = paths::data();
    let polos = jalur(&data.join(&mysql.id));
    let bertanda = jalur(&data.join(format!("{}@{}", mysql.id, tanda_asal(&mysql.source_root))));
    for d in [&polos, &bertanda] {
        if sama_jalur(pemilik_data(d).as_deref(), &mysql.path) {
            return d.clone();
        }
    }
    if !mysql.kembar {
        if Path::new(&bertanda).join("mysql").is_dir() {
            return bertanda;
        }
        if pemilik_pindah(pemilik_data(&polos).as_deref()) {
            return polos;
        }
    }
    let kunci = jalur(&data.join(mysql.kunci()));
    if pemilik_data(&kunci).is_none() {
        return kunci;
    }
    bertanda
}

/// Jalur paket MySQL pemilik folder data ini, atau `None` bila belum tercatat.
pub fn pemilik_data(folder_data: &str) -> Option<String> {
    let f = Path::new(folder_data).join(BERKAS_PEMILIK);
    let isi = baca_teks(&f).ok()?;
    let isi = isi.trim();
    (!isi.is_empty()).then(|| isi.to_string())
}

/// Catat pemilik folder data, sekali saja saat mysqld sudah jalan di atasnya;
/// diperbarui hanya bila paket unik ini pindah tempat.
pub fn catat_pemilik_data(folder_data: &str, mysql: Option<&BinPackage>) {
    let Some(mysql) = mysql else { return };
    if !Path::new(folder_data).is_dir() {
        return;
    }
    let lama = pemilik_data(folder_data);
    let tulis = match &lama {
        None => true,
        Some(l) => !mysql.kembar && !sama_jalur(Some(l), &mysql.path) && pemilik_pindah(Some(l)),
    };
    if tulis {
        // Penanda penjaga, bukan syarat jalan: kegagalan diabaikan.
        let _ = fs::write(Path::new(folder_data).join(BERKAS_PEMILIK), mysql.path.as_bytes());
    }
}

fn pemilik_pindah(pemilik: Option<&str>) -> bool {
    pemilik.is_none_or(|p| !Path::new(p).is_dir())
}

/// Dua jalur menunjuk folder yang sama apa pun ejaannya.
fn sama_jalur(a: Option<&str>, b: &str) -> bool {
    match a {
        Some(a) if !a.is_empty() && !b.is_empty() => sama_abaikan_besar(&normalkan(a), &normalkan(b)),
        _ => false,
    }
}

fn normalkan(j: &str) -> String {
    let mut j = j.trim().replace('/', "\\");
    if let Ok(a) = std::path::absolute(&j) {
        j = jalur(&a);
    }
    let mut j = j.trim_end_matches('\\').to_string();
    if Path::new(&j).is_dir() {
        if let Some(p) = jalur_win(&j, true) {
            j = p.trim_end_matches('\\').to_string();
        }
    }
    j
}

/// GetLongPathNameW (`panjang`) atau GetShortPathNameW.
fn jalur_win(j: &str, panjang: bool) -> Option<String> {
    use windows_sys::Win32::Storage::FileSystem::{GetLongPathNameW, GetShortPathNameW};
    let w: Vec<u16> = j.encode_utf16().chain(std::iter::once(0)).collect();
    let mut buf = vec![0u16; 1024];
    let n = unsafe {
        if panjang {
            GetLongPathNameW(w.as_ptr(), buf.as_mut_ptr(), buf.len() as u32)
        } else {
            GetShortPathNameW(w.as_ptr(), buf.as_mut_ptr(), buf.len() as u32)
        }
    } as usize;
    (n > 0 && n < buf.len()).then(|| String::from_utf16_lossy(&buf[..n]))
}

/// Nama pendek 8.3 dari jalur yang ada.
pub fn nama_pendek(j: &str) -> Option<String> {
    jalur_win(j, false)
}

// ------------------------------------------------------------------- Nginx

/// fastcgi_params yang PASTI ada: milik paket, atau tulisan Phoron di etc\nginx.
fn pastikan_fastcgi_params(nginx: &BinPackage, dir_etc: &Path) -> io::Result<String> {
    let bawaan = gabung(&gabung(&nginx.path, "conf"), "fastcgi_params");
    if Path::new(&bawaan).is_file() {
        return Ok(bawaan);
    }
    let milik = dir_etc.join("fastcgi_params");
    write_if_changed(&milik, FASTCGI_PARAMS_BAKU)?;
    Ok(jalur(&milik))
}

const FASTCGI_PARAMS_BAKU: &str = "# Dibuat otomatis oleh Phoron karena paket nginx tidak membawanya.
fastcgi_param  QUERY_STRING       $query_string;
fastcgi_param  REQUEST_METHOD     $request_method;
fastcgi_param  CONTENT_TYPE       $content_type;
fastcgi_param  CONTENT_LENGTH     $content_length;
fastcgi_param  SCRIPT_NAME        $fastcgi_script_name;
fastcgi_param  REQUEST_URI        $request_uri;
fastcgi_param  DOCUMENT_URI       $document_uri;
fastcgi_param  DOCUMENT_ROOT      $document_root;
fastcgi_param  SERVER_PROTOCOL    $server_protocol;
fastcgi_param  REQUEST_SCHEME     $scheme;
fastcgi_param  GATEWAY_INTERFACE  CGI/1.1;
fastcgi_param  SERVER_SOFTWARE    nginx/$nginx_version;
fastcgi_param  REMOTE_ADDR        $remote_addr;
fastcgi_param  REMOTE_PORT        $remote_port;
fastcgi_param  SERVER_ADDR        $server_addr;
fastcgi_param  SERVER_PORT        $server_port;
fastcgi_param  SERVER_NAME        $server_name;
fastcgi_param  REDIRECT_STATUS    200;
";

/// Isi string berkutip ganda nginx.
pub fn kutip_nginx(t: &str) -> String {
    t.replace('\\', "\\\\").replace('"', "\\\"")
}

/// Ukuran ember hash nama server: pangkat dua dari 64 yang memuat nama
/// terpanjang + 8, paling besar 1024.
pub fn ember_hash<S: AsRef<str>>(nama: &[S]) -> i32 {
    let terpanjang = nama.iter().map(|n| n.as_ref().encode_utf16().count()).max().unwrap_or(0) as i32;
    let mut e = 64;
    while e < terpanjang + 8 && e < 1024 {
        e *= 2;
    }
    e
}

/// Kapasitas tabel hash; tumbuh mengikuti banyaknya situs.
pub fn maks_hash(jumlah: usize) -> i32 {
    let mut m = 512i64;
    while m < jumlah as i64 * 8 && m < 16384 {
        m *= 2;
    }
    m as i32
}

fn write_nginx(
    profile: &Profile,
    nginx: &BinPackage,
    php: Option<&BinPackage>,
    sites: &[Site],
    r: &mut Hasil,
    opsi: &Opsi,
) -> io::Result<PathBuf> {
    if !php.is_some_and(|p| Path::new(&gabung(&p.path, "php-cgi.exe")).is_file()) {
        r.peringatan.push(Pesan::baru("Nginx melayani PHP lewat FastCGI; php-cgi.exe tidak ditemukan."));
    }
    r.php_fastcgi = true;

    let dir = paths::etc_nginx();
    fs::create_dir_all(&dir)?;
    let akar = situs::document_root(Some(profile));
    let doc_root = fwd(&akar);
    let params = fwd(&pastikan_fastcgi_params(nginx, &dir)?);
    let tmp = paths::tmp();
    let logs = paths::logs();
    let mut sb = Teks::default();
    sb.baris("# Dibuat otomatis oleh Phoron.");
    sb.baris("worker_processes  1;");
    sb.baris(&format!("error_log \"{}\";", fwd_p(&logs.join("nginx-error.log"))));
    sb.baris(&format!("pid \"{}\";", fwd_p(&tmp.join("nginx.pid"))));
    sb.baris("events { worker_connections 1024; }");
    sb.baris("http {");
    let mut nama: Vec<&str> = vec!["localhost"];
    nama.extend(sites.iter().map(|s| s.host_name.as_str()));
    sb.baris(&format!("    server_names_hash_bucket_size {};", ember_hash(&nama)));
    sb.baris(&format!("    server_names_hash_max_size {};", maks_hash(nama.len())));
    sb.baris(&format!("    include \"{}\";", fwd(&gabung(&gabung(&nginx.path, "conf"), "mime.types"))));
    sb.baris("    default_type application/octet-stream;");
    sb.baris("    sendfile on;");
    sb.baris("    client_max_body_size 128m;");
    if opsi.log_akses {
        sb.baris(&format!("    access_log \"{}\";", fwd_p(&logs.join("nginx-access.log"))));
    } else {
        sb.baris("    access_log off;   # Pengaturan > Catat log rinci");
    }
    sb.baris(&format!("    client_body_temp_path \"{}\";", fwd_p(&tmp.join("nginx-body"))));
    sb.baris(&format!("    proxy_temp_path \"{}\";", fwd_p(&tmp.join("nginx-proxy"))));
    sb.baris(&format!("    fastcgi_temp_path \"{}\";", fwd_p(&tmp.join("nginx-fcgi"))));
    let lokasi = lokasi_kolam_nginx(r, &akar, &params);
    let listen = dengar(opsi.akses_jaringan, profile.http_port);
    sb.0.push_str(&nginx_server(
        &listen,
        "localhost",
        &doc_root,
        r.fastcgi_port,
        &params,
        opsi.beranda_di_akar,
        &lokasi,
    ));
    for s in sites {
        let p = port_kolam(r, s);
        let port = if p > 0 { p } else { r.fastcgi_port };
        sb.0.push_str(&nginx_server(&listen, &s.host_name, &fwd(doc_root_situs(s)), port, &params, false, ""));
    }
    sb.baris("}");
    let path = dir.join("nginx.conf");
    write_if_changed(&path, &sb.0)?;
    Ok(path)
}

/// Situs kolam di folder proyek UTAMA mendapat location sendiri untuk akses
/// lewat jalur (localhost/proyek/).
fn lokasi_kolam_nginx(r: &Hasil, akar_utama: &str, params: &str) -> String {
    let mut sb = Teks::default();
    for k in &r.kolam {
        for s in &k.situs {
            if !sama_abaikan_besar(s.root.trim_end_matches('\\'), akar_utama.trim_end_matches('\\')) {
                continue;
            }
            sb.baris(&format!("        location ^~ \"/{}/\" {{   # PHP {}", kutip_nginx(&s.folder), k.php.version));
            sb.baris("            location ~ \\.php$ {");
            sb.baris(&format!("                fastcgi_pass   127.0.0.1:{};", k.port));
            sb.baris("                fastcgi_param  SCRIPT_FILENAME $document_root$fastcgi_script_name;");
            sb.baris(&format!("                include        \"{params}\";"));
            sb.baris("            }");
            sb.baris("        }");
        }
    }
    sb.0
}

fn nginx_server(
    listen: &str,
    name: &str,
    root: &str,
    fcgi_port: i32,
    params: &str,
    beranda_di_akar: bool,
    lokasi: &str,
) -> String {
    let mut sb = Teks::default();
    sb.baris("    server {");
    sb.baris(&format!("        listen       {listen};"));
    sb.baris(&format!("        server_name  {name};"));
    sb.baris(&format!("        root         \"{root}\";"));
    sb.baris("        index        index.php index.html index.htm;");
    if beranda_di_akar {
        // "location = /" hanya cocok dengan akar PERSIS, setara "^/?$" Apache.
        sb.baris(&format!("        location = / {{ rewrite ^ {BERANDA_ALIAS}/index.php last; }}"));
    }
    // Prefiks ^~ didahulukan di atas "location ~ \.php$".
    sb.0.push_str(lokasi);
    sb.baris("        location / { try_files $uri $uri/ /index.php?$query_string; }");
    sb.baris(&format!("        location {BERANDA_ALIAS}/ {{"));
    sb.baris(&format!("            alias \"{}/\";", fwd_p(&beranda_folder())));
    sb.baris("            index index.php;");
    sb.baris("            location ~ \\.php$ {");
    sb.baris(&format!("                fastcgi_pass   127.0.0.1:{fcgi_port};"));
    sb.baris("                fastcgi_param  SCRIPT_FILENAME $request_filename;");
    sb.baris(&format!("                include        \"{params}\";"));
    sb.baris("            }");
    sb.baris("        }");
    sb.baris("        location ~ \\.php$ {");
    sb.baris(&format!("            fastcgi_pass   127.0.0.1:{fcgi_port};"));
    sb.baris("            fastcgi_index  index.php;");
    sb.baris("            fastcgi_param  SCRIPT_FILENAME $document_root$fastcgi_script_name;");
    sb.baris(&format!("            include        \"{params}\";"));
    sb.baris("        }");
    sb.baris("    }");
    sb.0
}

#[cfg(test)]
mod uji;
