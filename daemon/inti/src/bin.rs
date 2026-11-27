//! Fitur: memindai folder bin dan mengenali paket PHP/Apache/Nginx/MySQL/Node.
//!
//! Daftar periksa (dari kasus nyata di versi C#):
//! - Tata letak Laragon (`bin\php\php-8.3.12-...`), langsung di akar, XAMPP
//!   (`xampp\php` tanpa nomor versi), dan WAMP (`bin\php\php8.1.0`) semuanya
//!   tertangani: folder ditelusuri sampai kedalaman terbatas, dan yang
//!   menentukan adalah ADA TIDAKNYA exe, bukan pola nama.
//! - Folder besar yang mustahil berisi paket (htdocs, node_modules, vendor, ...)
//!   tidak dirayapi, supaya akar bin yang salah tidak membuat pemindaian lambat.
//! - Isi sebuah paket tidak ditelusuri ("bin" di dalam Apache bukan folder bin).
//! - Versi, toolset, dan arsitektur dibaca dari NAMA folder - gratis. Hanya bila
//!   namanya bisu (XAMPP), arsitektur dibaca dari header PE dan versi dari
//!   keluaran exe-nya, dengan singgahan per exe+waktu ubah.
//! - PHP Thread Safe ditentukan dari berkas `php*apache2_4.dll`, bukan dari nama:
//!   banyak arsip dinamai seragam padahal isinya NTS.
//! - Nama folder kembar (php-8.3.12 di bin Phoron DAN Laragon, "mysql" di dua
//!   XAMPP) mendapat kunci unik; yang pertama tetap memakai nama polosnya supaya
//!   folder php.ini dan folder datanya tidak berpindah.
//! - Folder yang sama dari dua akar yang beririsan hanya dihitung sekali.

use crate::model::{BinKind, BinPackage};
use crate::pola;
use crate::protokol::Pesan;
use crate::shell;
use crate::teks::sama_abaikan_besar;
use std::collections::HashMap;
use std::fs;
use std::io::{Read, Seek, SeekFrom};
use std::path::Path;
use std::sync::Mutex;
use std::time::SystemTime;

/// Sedalam apa penelusuran turun dari folder bin: cukup untuk WAMP.
const KEDALAMAN_MAKS: usize = 3;

const LEWATI: &[&str] = &[
    "ext", "conf", "logs", "log", "data", "tmp", "temp", "cache", "backup", "www", "htdocs",
    "public_html", "cgi-bin", "node_modules", ".git", ".svn", "include", "includes", "lib", "libs",
    "share", "man", "doc", "docs", "icons", "error", "modules", "sbin", "sessions", "uploads", "vendor",
];

/// Gabung jalur dengan gaya Windows, tanpa menormalkan apa pun: jalur yang
/// ditampilkan dan disimpan ke profil harus sama dengan yang ditulis orang.
pub fn gabung(a: &str, b: &str) -> String {
    if a.is_empty() {
        return b.to_string();
    }
    if a.ends_with(['\\', '/', ':']) {
        format!("{a}{b}")
    } else {
        format!("{a}\\{b}")
    }
}

fn nama_folder(p: &str) -> &str {
    let t = p.trim_end_matches('\\');
    t.rsplit(['\\', '/']).next().unwrap_or(t)
}

/// Subfolder langsung, dalam urutan yang diberikan Windows; galat = kosong.
fn subfolder(dir: &str) -> Vec<String> {
    let Ok(rd) = fs::read_dir(dir) else { return Vec::new() };
    rd.filter_map(|e| e.ok())
        .filter(|e| e.file_type().map(|t| t.is_dir()).unwrap_or(false))
        .map(|e| gabung(dir, &e.file_name().to_string_lossy()))
        .collect()
}

/// Pindai semua akar; hasilnya terurut jenis, versi menurun, lalu nama.
pub fn scan_all(roots: &[String]) -> Vec<BinPackage> {
    let mut found: Vec<BinPackage> = Vec::new();
    for root in roots {
        if root.trim().is_empty() || !Path::new(root).is_dir() {
            continue;
        }
        let mut hasil = Vec::new();
        telusuri(root, root, 0, &mut hasil);
        for pkg in hasil {
            if !found.iter().any(|f| sama_abaikan_besar(&f.path, &pkg.path)) {
                found.push(pkg);
            }
        }
    }
    found.sort_by(|a, b| {
        (a.kind as u8)
            .cmp(&(b.kind as u8))
            .then_with(|| b.parsed().cmp(&a.parsed()))
            .then_with(|| a.id.to_uppercase().cmp(&b.id.to_uppercase()))
    });
    tandai_kembar(&mut found);
    found
}

fn telusuri(dir: &str, root: &str, dalam: usize, keluar: &mut Vec<BinPackage>) {
    for sub in subfolder(dir) {
        let nama = nama_folder(&sub).to_lowercase();
        if LEWATI.contains(&nama.as_str()) {
            continue;
        }
        if let Some(pkg) = identify(&sub, root) {
            keluar.push(pkg);
            continue;
        }
        if dalam < KEDALAMAN_MAKS {
            telusuri(&sub, root, dalam + 1, keluar);
        }
    }
}

/// Kunci unik bagi paket berinama kembar. Urutan daftar menentukan siapa
/// "pertama": dialah yang mempertahankan nama polos.
pub fn tandai_kembar(daftar: &mut [BinPackage]) {
    let mut kelompok: Vec<(String, Vec<usize>)> = Vec::new();
    for (i, p) in daftar.iter().enumerate() {
        let k = format!("{:?}|{}", p.kind, p.id.to_lowercase());
        match kelompok.iter_mut().find(|(x, _)| *x == k) {
            Some((_, v)) => v.push(i),
            None => kelompok.push((k, vec![i])),
        }
    }
    for (_, anggota) in kelompok.into_iter().filter(|(_, v)| v.len() > 1) {
        let mut dipakai: Vec<String> = Vec::new();
        for (n, &i) in anggota.iter().enumerate() {
            let p = &mut daftar[i];
            p.kembar = true;
            let mut kunci =
                if n == 0 { p.id.clone() } else { format!("{}@{}", p.id, tanda_asal(&p.source_root)) };
            // Dua kembaran dari folder bin yang SAMA (bertingkat): diberi nomor.
            if dipakai.iter().any(|d| sama_abaikan_besar(d, &kunci)) {
                kunci = format!("{}-{}", kunci, n + 1);
            }
            dipakai.push(kunci.clone());
            p.kunci = kunci;
        }
    }
}

/// "C:\Phoron\bin" -> "C-Phoron-bin". Aman dipakai sebagai nama folder.
pub fn tanda_asal(root: &str) -> String {
    let s: String = root
        .chars()
        .map(|c| if c.is_alphanumeric() || c == '.' || c == '_' { c } else { '-' })
        .collect();
    let mut s = s;
    while s.contains("--") {
        s = s.replace("--", "-");
    }
    let s = s.trim_matches('-');
    if s.is_empty() { "bin".into() } else { s.into() }
}

/// Kenali satu folder sebagai paket, atau None. Nama folder yang menjanjikan
/// (php-..., httpd-...) tanpa exe = instalasi rusak/setengah diekstrak; tidak
/// ditawarkan.
pub fn identify(dir: &str, source_root: &str) -> Option<BinPackage> {
    if !Path::new(dir).is_dir() {
        return None;
    }
    let name = nama_folder(dir).to_string();
    let calon = [
        (BinKind::Php, gabung(dir, "php.exe")),
        (BinKind::Apache, gabung(&gabung(dir, "bin"), "httpd.exe")),
        (BinKind::Nginx, gabung(dir, "nginx.exe")),
        (BinKind::MySql, gabung(&gabung(dir, "bin"), "mysqld.exe")),
        (BinKind::Node, gabung(dir, "node.exe")),
    ];
    let (kind, exe) = calon.into_iter().find(|(_, e)| Path::new(e).is_file())?;
    let mut p = umum(kind, dir, &name, source_root, &exe);
    if kind == BinKind::Php {
        let dll = modul_apache(dir);
        p.thread_safe = dll.is_some() || !name.to_lowercase().contains("nts");
        p.apache_module_dll = dll.unwrap_or_default();
    }
    Some(p)
}

/// Berkas `php*apache2_4.dll` pertama di folder PHP.
fn modul_apache(dir: &str) -> Option<String> {
    let rd = fs::read_dir(dir).ok()?;
    rd.filter_map(|e| e.ok()).find_map(|e| {
        let n = e.file_name().to_string_lossy().to_lowercase();
        (n.starts_with("php") && n.ends_with("apache2_4.dll") && e.path().is_file())
            .then(|| gabung(dir, &e.file_name().to_string_lossy()))
    })
}

fn umum(kind: BinKind, dir: &str, name: &str, root: &str, exe: &str) -> BinPackage {
    let mut p = BinPackage::baru(kind, name, dir, "");
    p.main_exe = exe.into();
    p.source_root = root.into();
    // Node kerap dinamai hanya dengan nomor mayor ("node-v18").
    p.version = pola::versi(name).or_else(|| pola::mayor_saja(name)).unwrap_or("").into();
    p.compiler = pola::kompilator(name).map(str::to_uppercase).unwrap_or_default();
    let lower = name.to_lowercase();
    p.arch = if lower.contains("x64") || lower.contains("win64") {
        "x64".into()
    } else if lower.contains("x86") || lower.contains("win32") {
        "x86".into()
    } else {
        String::new()
    };
    // Nama bisu (gaya XAMPP): barulah binernya sendiri ditanya.
    if p.arch.is_empty() {
        p.arch = arsitektur(exe);
    }
    if p.version.is_empty() {
        p.version = versi_exe(kind, exe);
    }
    p
}

// ------------------------------------------------------------ penyelidikan

type Singgahan = Mutex<Option<HashMap<String, String>>>;
static SINGGAH_ARCH: Singgahan = Mutex::new(None);
static SINGGAH_VERSI: Singgahan = Mutex::new(None);

fn kunci_exe(exe: &str) -> String {
    let t = fs::metadata(exe)
        .and_then(|m| m.modified())
        .ok()
        .and_then(|t| t.duration_since(SystemTime::UNIX_EPOCH).ok())
        .map(|d| d.as_nanos())
        .unwrap_or(0);
    format!("{}|{}", exe.to_lowercase(), t)
}

fn singgah(tempat: &Singgahan, kunci: String, hitung: impl FnOnce() -> String) -> String {
    if let Some(v) = tempat.lock().unwrap().get_or_insert_with(HashMap::new).get(&kunci) {
        return v.clone();
    }
    let v = hitung();
    tempat.lock().unwrap().get_or_insert_with(HashMap::new).insert(kunci, v.clone());
    v
}

/// Arsitektur dari header PE: tidak menjalankan apa pun, aman walau exe rusak.
pub fn arsitektur(exe: &str) -> String {
    if !Path::new(exe).is_file() {
        return String::new();
    }
    singgah(&SINGGAH_ARCH, kunci_exe(exe), || baca_pe(exe).unwrap_or_default())
}

fn baca_pe(exe: &str) -> Option<String> {
    let mut f = fs::File::open(exe).ok()?;
    let panjang = f.metadata().ok()?.len() as i64;
    let mut b4 = [0u8; 4];
    f.seek(SeekFrom::Start(0x3C)).ok()?;
    f.read_exact(&mut b4).ok()?;
    let awal = i32::from_le_bytes(b4) as i64;
    if awal <= 0 || awal >= panjang - 6 {
        return None;
    }
    f.seek(SeekFrom::Start(awal as u64)).ok()?;
    f.read_exact(&mut b4).ok()?;
    if u32::from_le_bytes(b4) != 0x0000_4550 {
        return None;
    }
    let mut b2 = [0u8; 2];
    f.read_exact(&mut b2).ok()?;
    Some(
        match u16::from_le_bytes(b2) {
            0x8664 => "x64",
            0x014c => "x86",
            0xAA64 => "arm64",
            _ => "",
        }
        .into(),
    )
}

/// Versi menurut program itu sendiri; "" bila tidak pasti - menebak lebih
/// buruk daripada mengaku tidak tahu.
pub fn versi_exe(kind: BinKind, exe: &str) -> String {
    if !Path::new(exe).is_file() {
        return String::new();
    }
    let k = format!("{:?}|{}", kind, kunci_exe(exe));
    singgah(&SINGGAH_VERSI, k, || {
        // -n pada PHP: jangan muat php.ini (lambat, dan menyeret peringatan ekstensi).
        let (arg, awalan, spasi) = match kind {
            BinKind::Php => ("-n -v", "PHP", true),
            BinKind::Apache => ("-v", "Apache/", false),
            BinKind::MySql => ("--version", "Ver", true),
            BinKind::Nginx => ("-v", "nginx/", false),
            BinKind::Node => ("-v", "", false),
        };
        let dir = Path::new(exe).parent().map(Path::to_path_buf);
        // Batas waktu pendek: satu exe rusak tidak boleh menahan daftar versi.
        let Ok(r) = shell::run(exe, arg, dir.as_deref(), 8000, None, None) else { return String::new() };
        let semua = r.all();
        let v = if awalan.is_empty() { pola::versi(&semua) } else { pola::versi_setelah(&semua, awalan, spasi) };
        v.unwrap_or("").to_string()
    })
}

/// Nama modul Apache untuk sebuah PHP. PHP 8 memakai "php_module" tanpa
/// angka; salah menulisnya membuat Apache menolak start tanpa menyebut sebab.
pub fn apache_module_name(php: Option<&BinPackage>) -> &'static str {
    match php.map(|p| p.parsed().0) {
        Some(5) => "php5_module",
        Some(7) => "php7_module",
        _ => "php_module",
    }
}

// --------------------------------------------------------- runtime VC++

/// DLL penanda dan nama paket Redistributable per toolset. VC14 sampai VS17
/// berbagi runtime "140" yang sama.
fn runtime_untuk(toolset: &str) -> Option<(&'static str, &'static str)> {
    match toolset.to_uppercase().as_str() {
        "VC9" => Some(("msvcr90.dll", "Visual C++ 2008")),
        "VC11" => Some(("msvcr110.dll", "Visual C++ 2012")),
        "VC12" => Some(("msvcr120.dll", "Visual C++ 2013")),
        "VC14" | "VC15" | "VS16" | "VS17" => Some(("vcruntime140.dll", "Visual C++ 2015-2022")),
        _ => None,
    }
}

/// Runtime Visual C++ yang dibutuhkan paket tidak ada di komputer ini.
///
/// Gejalanya menyesatkan: Apache menolak memuat php5apache2_4.dll dengan
/// "The specified module could not be found" - menunjuk DLL PHP, padahal yang
/// hilang msvcr110.dll milik Microsoft. Satu profil gagal sementara profil
/// lain jalan, karena tiap toolset butuh redistributable berbeda.
pub fn runtime_kurang(pkg: &BinPackage) -> Option<(String, Pesan)> {
    let (dll, paket) = runtime_untuk(&pkg.compiler)?;
    let arch = if pkg.arch.is_empty() { arsitektur(&pkg.main_exe) } else { pkg.arch.clone() };
    let windows = std::env::var("SystemRoot").unwrap_or_else(|_| r"C:\Windows".into());
    // DLL 64-bit di System32, yang 32-bit di SysWOW64 - memang terbalik.
    let sistem = if sama_abaikan_besar(&arch, "x86") { "SysWOW64" } else { "System32" };
    let ada = Path::new(&windows).join(sistem).join(dll).is_file()
        // Sebagian build membawa runtime-nya sendiri; itu sah.
        || Path::new(&pkg.path).join(dll).is_file()
        || Path::new(&pkg.path).join("bin").join(dll).is_file();
    if ada {
        return None;
    }
    let jenis = format!("{:?}", pkg.kind);
    let pesan = if arch.is_empty() {
        Pesan::dengan(
            "{0} {1} dibangun dengan {2}, jadi butuh {3} Redistributable yang sesuai. \
             Di komputer ini {4} tidak ada, dan tanpa itu modul PHP tidak bisa dimuat - \
             Apache akan gagal start dengan pesan yang justru menunjuk berkas PHP. \
             Pasang {3} Redistributable dari situs Microsoft.",
            &[&jenis, &pkg.version, &pkg.compiler, paket, dll],
        )
    } else {
        Pesan::dengan(
            "{0} {1} dibangun dengan {2}, jadi butuh {3} Redistributable {4}. \
             Di komputer ini {5} tidak ada, dan tanpa itu modul PHP tidak bisa dimuat - \
             Apache akan gagal start dengan pesan yang justru menunjuk berkas PHP. \
             Pasang {3} Redistributable dari situs Microsoft.",
            &[&jenis, &pkg.version, &pkg.compiler, paket, &arch, dll],
        )
    };
    Some((dll.to_string(), pesan))
}

/// Keluhan runtime untuk beberapa paket; satu DLL cukup diadukan sekali.
pub fn runtime_kurang_semua(paket: &[&BinPackage]) -> Vec<Pesan> {
    let mut sudah: Vec<String> = Vec::new();
    let mut hasil = Vec::new();
    for p in paket {
        if let Some((dll, pesan)) = runtime_kurang(p) {
            if !sudah.iter().any(|d| sama_abaikan_besar(d, &dll)) {
                sudah.push(dll);
                hasil.push(pesan);
            }
        }
    }
    hasil
}

#[cfg(test)]
mod uji {
    use super::*;

    fn buat(akar: &Path, rel: &str) {
        let p = akar.join(rel);
        fs::create_dir_all(p.parent().unwrap()).unwrap();
        // Bukan PE sah: arsitektur kosong, dan versi dari nama.
        fs::write(p, b"MZ").unwrap();
    }

    fn sandbox(nama: &str) -> std::path::PathBuf {
        let d = std::env::temp_dir().join(format!("phoron-rs-bin-{}-{}", nama, std::process::id()));
        let _ = fs::remove_dir_all(&d);
        fs::create_dir_all(&d).unwrap();
        d
    }

    #[test]
    fn tata_letak_laragon_wamp_dan_folder_lewati() {
        let d = sandbox("letak");
        buat(&d, r"a\php\php-8.3.12-Win32-vs16-x64\php.exe");
        buat(&d, r"a\php\php-8.3.12-Win32-vs16-x64\php8apache2_4.dll");
        buat(&d, r"a\php\php-7.4.33-nts-Win32-vc15-x64\php.exe");
        buat(&d, r"a\apache\httpd-2.4.62-win64-VS17\bin\httpd.exe");
        buat(&d, r"a\mysql\mysql-8.0.30-winx64\bin\mysqld.exe");
        buat(&d, r"a\nodejs\node-v18\node.exe");
        buat(&d, r"b\bin\php\php8.1.0\php.exe");
        buat(&d, r"a\www\php-9.9.9\php.exe"); // di folder lewati
        buat(&d, r"a\php\php-5.6.40-rusak\README"); // tanpa exe
        let akar = |s: &str| d.join(s).to_string_lossy().into_owned();
        let semua = scan_all(&[akar("a"), akar("b"), akar("a")]);
        let id: Vec<&str> = semua.iter().map(|p| p.id.as_str()).collect();
        assert_eq!(
            id,
            vec![
                "php-8.3.12-Win32-vs16-x64",
                "php8.1.0",
                "php-7.4.33-nts-Win32-vc15-x64",
                "httpd-2.4.62-win64-VS17",
                "mysql-8.0.30-winx64",
                "node-v18"
            ]
        );
        assert!(semua[0].thread_safe && semua[0].apache_module_dll.ends_with("php8apache2_4.dll"));
        assert!(!semua[2].thread_safe);
        assert_eq!(semua[0].compiler, "VS16");
        assert_eq!(semua[0].arch, "x64");
        assert_eq!(semua[5].version, "18");
        let _ = fs::remove_dir_all(&d);
    }

    #[test]
    fn nama_kembar_diberi_kunci_unik() {
        let d = sandbox("kembar");
        buat(&d, r"c\xampp\php\php.exe");
        buat(&d, r"d\xampp\php\php.exe");
        let akar = |s: &str| d.join(s).join("xampp").to_string_lossy().into_owned();
        let semua = scan_all(&[akar("c"), akar("d")]);
        assert_eq!(semua.len(), 2);
        assert!(semua.iter().all(|p| p.kembar));
        assert_eq!(semua[0].kunci(), "php");
        assert!(semua[1].kunci().starts_with("php@") && semua[1].kunci().ends_with("d-xampp"));
        assert_eq!(semua[1].nilai_simpan(), semua[1].path);
        let _ = fs::remove_dir_all(&d);
    }

    #[test]
    fn arsitektur_dari_header_pe() {
        // Exe Rust uji ini sendiri adalah PE x64.
        let diri = std::env::current_exe().unwrap();
        assert_eq!(arsitektur(&diri.to_string_lossy()), "x64");
        let cmd = std::env::var("SystemRoot").unwrap() + r"\SysWOW64\cmd.exe";
        if Path::new(&cmd).is_file() {
            assert_eq!(arsitektur(&cmd), "x86");
        }
    }

    #[test]
    fn modul_apache_per_versi() {
        let p = |v: &str| BinPackage::baru(BinKind::Php, "p", "p", v);
        assert_eq!(apache_module_name(Some(&p("5.6.40"))), "php5_module");
        assert_eq!(apache_module_name(Some(&p("7.4.1"))), "php7_module");
        assert_eq!(apache_module_name(Some(&p("8.3.0"))), "php_module");
        assert_eq!(apache_module_name(None), "php_module");
    }

    #[test]
    fn gabung_jalur_gaya_windows() {
        assert_eq!(gabung(r"C:\laragon\bin", "php"), r"C:\laragon\bin\php");
        assert_eq!(gabung(r"C:\laragon\bin\", "php"), r"C:\laragon\bin\php");
        assert_eq!(gabung("C:", "php"), "C:php");
    }
}
