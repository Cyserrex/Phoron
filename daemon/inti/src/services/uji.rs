//! Uji layanan. Uji `nyata_*` memakai paket SUNGGUHAN di C:\laragon\bin dan
//! C:\xampp (hanya dibaca; config, data, dan log selalu di sandbox) dan
//! dilewati bila paketnya tidak ada. Port uji selalu acak dan bebas.

use super::*;
use crate::config::{self, Opsi};
use crate::model::{BinKind, Site};
use crate::{bin, paths};
use std::fs;
use std::io::{Read, Write};
use std::net::{TcpListener, TcpStream};
use crate::shell::RunResult;

// ------------------------------------------------------------ pembantu

type Rekaman = Arc<Mutex<Vec<Peristiwa>>>;

fn rekam() -> (Layanan, Rekaman) {
    let r: Rekaman = Arc::new(Mutex::new(Vec::new()));
    let r2 = r.clone();
    (Layanan::baru(move |p| kunci(&r2).push(p)), r)
}

/// Semua baris log, sudah jadi teks Indonesia.
fn log(r: &Rekaman) -> Vec<String> {
    kunci(r)
        .iter()
        .filter_map(|p| match p {
            Peristiwa::Log(p) => Some(config::teks(p)),
            _ => None,
        })
        .collect()
}

fn ada_log(r: &Rekaman, isi: &str) -> bool {
    log(r).iter().any(|b| b.contains(isi))
}

fn hitung_log(r: &Rekaman, isi: &str) -> usize {
    log(r).iter().filter(|b| b.contains(isi)).count()
}

fn status_terakhir(r: &Rekaman) -> Option<StatusLayanan> {
    kunci(r).iter().rev().find_map(|p| match p {
        Peristiwa::Status(s) => Some(s.clone()),
        _ => None,
    })
}

fn port_bebas() -> u16 {
    let l = TcpListener::bind("127.0.0.1:0").unwrap();
    l.local_addr().unwrap().port()
}

fn tunggu(syarat: impl Fn() -> bool, ms: u64) -> bool {
    let mulai = Instant::now();
    while mulai.elapsed() < Duration::from_millis(ms) {
        if syarat() {
            return true;
        }
        std::thread::sleep(Duration::from_millis(50));
    }
    syarat()
}

fn jalur(p: &Path) -> String {
    p.to_string_lossy().into_owned()
}

/// PID proses yang exe-nya berada di bawah salah satu folder ini.
fn proses_di(folder: &[&str]) -> Vec<(u32, u32, String)> {
    use windows_sys::Win32::Foundation::{CloseHandle, INVALID_HANDLE_VALUE};
    use windows_sys::Win32::System::Diagnostics::ToolHelp::{
        CreateToolhelp32Snapshot, Process32FirstW, Process32NextW, PROCESSENTRY32W, TH32CS_SNAPPROCESS,
    };
    use windows_sys::Win32::System::Threading::{
        OpenProcess, QueryFullProcessImageNameW, PROCESS_QUERY_LIMITED_INFORMATION,
    };
    let awalan: Vec<String> = folder.iter().map(|f| format!("{}\\", f.to_lowercase())).collect();
    let mut hasil = Vec::new();
    // SAFETY: snapshot dan handle dibuka lalu ditutup; buffer berukuran tepat.
    unsafe {
        let snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if snap == INVALID_HANDLE_VALUE {
            return hasil;
        }
        let mut pe = PROCESSENTRY32W { dwSize: std::mem::size_of::<PROCESSENTRY32W>() as u32, ..Default::default() };
        let mut ada = Process32FirstW(snap, &mut pe) != 0;
        while ada {
            let h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pe.th32ProcessID);
            if !h.is_null() {
                let mut buf = [0u16; 1024];
                let mut n = buf.len() as u32;
                if QueryFullProcessImageNameW(h, 0, buf.as_mut_ptr(), &mut n) != 0 {
                    let j = String::from_utf16_lossy(&buf[..n as usize]).to_lowercase();
                    if awalan.iter().any(|a| j.starts_with(a.as_str())) {
                        hasil.push((pe.th32ProcessID, pe.th32ParentProcessID, j));
                    }
                }
                CloseHandle(h);
            }
            ada = Process32NextW(snap, &mut pe) != 0;
        }
        CloseHandle(snap);
    }
    hasil
}

/// Penjaga uji nyata: (1) proses baru dari folder paket yang masih hidup
/// sesudah uji selesai, (2) berkas di folder paket yang ditulisi uji.
/// Sekalian menyalakan "dengar hanya di 127.0.0.1" untuk config yang ditulis
/// utas uji ini, supaya Windows Firewall tidak memunculkan dialog.
struct Penjaga {
    folder: Vec<String>,
    awal: Vec<u32>,
    paket: crate::uji_paket::JagaPaket,
}

/// (pid, pid induk, jalur exe).
type Info = (u32, u32, String);

/// Folder paket yang dipakai uji nyata - hanya boleh dibaca.
const FOLDER_PAKET: &[&str] = &[
    r"C:\laragon\bin\apache",
    r"C:\laragon\bin\php",
    r"C:\laragon\bin\mysql",
    r"C:\laragon\bin\nginx",
    r"C:\xampp\apache",
    r"C:\xampp\mysql",
    r"C:\xampp\php",
];

impl Penjaga {
    fn baru(folder: &[&str]) -> Penjaga {
        config::uji_dengar_lokal(true);
        let paket = crate::uji_paket::JagaPaket::baru(&crate::uji_paket::yang_ada(FOLDER_PAKET));
        let folder: Vec<String> = folder.iter().map(|s| s.to_string()).collect();
        let f: Vec<&str> = folder.iter().map(String::as_str).collect();
        let awal = proses_di(&f).into_iter().map(|x| x.0).collect();
        Penjaga { folder, awal, paket }
    }

    fn yatim(&self) -> Vec<Info> {
        let f: Vec<&str> = self.folder.iter().map(String::as_str).collect();
        proses_di(&f).into_iter().filter(|p| !self.awal.contains(&p.0)).collect()
    }

    fn pastikan_bersih(&self) {
        let ok = tunggu(|| self.yatim().is_empty(), 10_000);
        let sisa = self.yatim();
        // Bereskan dulu supaya uji yang gagal tidak meninggalkan proses.
        for p in &sisa {
            shell::kill_tree(p.0);
        }
        config::uji_dengar_lokal(false);
        assert!(ok, "proses yatim tertinggal: {sisa:?}");
        self.paket.pastikan_utuh();
    }
}

fn paket(dir: &str) -> Option<BinPackage> {
    let akar = Path::new(dir).parent().and_then(Path::parent).map(jalur).unwrap_or_default();
    bin::identify(dir, &akar)
}

/// Ambil satu halaman lewat HTTP/1.0; isi tubuh saja.
fn ambil(port: u16, jalan: &str) -> Option<String> {
    let mut s = TcpStream::connect(("127.0.0.1", port)).ok()?;
    s.set_read_timeout(Some(Duration::from_secs(15))).ok()?;
    write!(s, "GET {jalan} HTTP/1.0\r\nHost: localhost\r\n\r\n").ok()?;
    let mut b = Vec::new();
    s.read_to_end(&mut b).ok()?;
    let t = String::from_utf8_lossy(&b).into_owned();
    Some(t)
}

const APACHE: &str = r"C:\laragon\bin\apache\httpd-2.4.54-win64-VS16";
const PHP81: &str = r"C:\laragon\bin\php\php-8.1.10-Win32-vs16-x64";
const NGINX: &str = r"C:\laragon\bin\nginx\nginx-1.22.0";
const MYSQL8: &str = r"C:\laragon\bin\mysql\mysql-8.0.30-winx64";
const XAMPP_MYSQL: &str = r"C:\xampp\mysql";

/// Profil web uji dengan situs berisi uji.php; port acak.
fn profil_web(d: &Path, web_server: &str, web: &BinPackage, php: &BinPackage) -> Profile {
    let www = d.join("www");
    fs::create_dir_all(&www).unwrap();
    fs::write(www.join("uji.php"), "<?php echo 'phoron-uji ' . PHP_VERSION . ' ' . php_sapi_name();").unwrap();
    let mut p = Profile {
        name: "Uji layanan".into(),
        file_name: "uji-layanan".into(),
        php_id: php.id.clone(),
        web_server: web_server.into(),
        http_port: port_bebas() as i32,
        https_port: port_bebas() as i32,
        project_roots: vec![jalur(&www)],
        ..Profile::default()
    };
    if web_server == "nginx" {
        p.nginx_id = web.id.clone();
    } else {
        p.apache_id = web.id.clone();
    }
    p
}

fn opsi_uji() -> Opsi {
    Opsi { php_ini_ke_folder_php: false, log_akses: false, beranda_di_akar: false, opcache: true, akses_jaringan: false }
}

// ------------------------------------------------------------ uji unit

#[test]
fn baris_penting() {
    assert!(penting("[ERROR] [MY-010119] Aborting"));
    assert!(penting("AH00072: make_sock: could not bind to address; Permission DENIED"));
    assert!(penting("PHP Warning:  Module 'x' already loaded"));
    assert!(penting("Cannot load modules/x.so"));
    assert!(!penting("[System] [MY-010931] ready for connections"));
    assert!(!penting(""));
}

#[test]
fn env_php_cgi() {
    let (_g, d) = paths::sandbox_uji("svc-env");
    let mut php = BinPackage::baru(BinKind::Php, "php-8.3", r"C:\php83", "8.3.12");
    php.kunci = "php-8.3@x".into();
    let e = env_for(Some(&php), Some(r"D:\ini"));
    assert!(e["PATH"].starts_with(r"C:\php83;"));
    assert_eq!(e["PHPRC"], r"D:\ini");
    // Tanpa folder: folder yang ditulis config, menurut KUNCI paket.
    let e = env_for(Some(&php), None);
    assert_eq!(e["PHPRC"], config::folder_php_ini(&php, false));
    assert!(e["PHPRC"].ends_with(r"etc\php\php-8.3@x"));
    assert!(env_for(None, None).is_empty());

    let e = Layanan::env_php_cgi_profil(&php, None);
    assert_eq!((e["PHP_FCGI_MAX_REQUESTS"].as_str(), e["PHP_FCGI_CHILDREN"].as_str()), ("0", "4"));
    let mut k = config::Kolam::baru(php.clone(), vec!["mbstring".into()], Vec::new(), Vec::new());
    k.folder_ini = r"E:\kolam".into();
    let e = Layanan::env_php_cgi_kolam(&k);
    assert_eq!((e["PHP_FCGI_MAX_REQUESTS"].as_str(), e["PHP_FCGI_CHILDREN"].as_str()), ("0", "2"));
    assert_eq!(e["PHPRC"], r"E:\kolam");
    let _ = fs::remove_dir_all(&d);
}

#[test]
fn batas_bangkit_per_menit() {
    let t0 = Instant::now();
    let mut v = Vec::new();
    assert!(boleh_bangkit(&mut v, t0));
    assert!(boleh_bangkit(&mut v, t0 + Duration::from_secs(1)));
    assert!(boleh_bangkit(&mut v, t0 + Duration::from_secs(2)));
    assert!(!boleh_bangkit(&mut v, t0 + Duration::from_secs(3)), "keempat dalam semenit ditolak");
    assert_eq!(v.len(), 3);
    // Yang pertama sudah lewat semenit: satu jatah kembali.
    assert!(boleh_bangkit(&mut v, t0 + Duration::from_secs(61)));
    assert!(!boleh_bangkit(&mut v, t0 + Duration::from_secs(61)));
}

#[test]
fn init_berhasil_hanya_bila_tuntas() {
    let d = std::env::temp_dir().join(format!("phoron-rs-svc-init-{}", std::process::id()));
    let _ = fs::remove_dir_all(&d);
    fs::create_dir_all(d.join("mysql")).unwrap();
    let dd = jalur(&d);
    let r = |kode: i32, habis: bool| RunResult { exit_code: kode, timed_out: habis, ..RunResult::default() };
    assert!(init_berhasil(&r(0, false), &dd));
    assert!(!init_berhasil(&r(0, true), &dd), "terputus selalu gagal");
    // Folder mysql\ saja tidak cukup.
    assert!(!init_berhasil(&r(1, false), &dd));
    assert!(!tabel_sistem_ada(&dd));
    fs::write(d.join(r"mysql\global_priv.MAD"), b"").unwrap();
    assert!(init_berhasil(&r(1, false), &dd), "MariaDB yang keluar bukan 0 sesudah selesai");
    fs::remove_file(d.join(r"mysql\global_priv.MAD")).unwrap();
    fs::write(d.join("mysql.ibd"), b"").unwrap();
    assert!(tabel_sistem_ada(&dd));
    assert!(!init_berhasil(&r(1, true), &dd));
    let _ = fs::remove_dir_all(&d);
}

#[test]
fn init_gagal_dipindah() {
    let (_g, d) = paths::sandbox_uji("svc-initgagal");
    // "mysql_install_db" palsu yang selalu gagal: findstr dengan argumen asing.
    let bin = d.join(r"bin\mariadb-palsu\bin");
    fs::create_dir_all(&bin).unwrap();
    let sistem = std::env::var("SystemRoot").unwrap();
    fs::copy(Path::new(&sistem).join(r"System32\findstr.exe"), bin.join("mysql_install_db.exe")).unwrap();
    let m = BinPackage::baru(BinKind::MySql, "mariadb-palsu", &jalur(&d.join(r"bin\mariadb-palsu")), "10.4.0");
    let p = Profile { mysql_id: m.id.clone(), mysql_port: port_bebas() as i32, ..Profile::default() };
    config::write_my_ini(&p, &m).unwrap();
    let data_dir = config::mysql_data_dir(&m);

    let (l, r) = rekam();
    assert!(!l.start_db(&p, Some(&m)));
    let s = l.status();
    assert_eq!(s.db, ServiceState::Gagal);
    assert_eq!(s.galat_db.unwrap().kunci, "Gagal menyiapkan folder data{0}:\n{1}");
    assert!(!Path::new(&data_dir).exists(), "sisa init gagal tidak dipindah");
    let sisih: Vec<_> = fs::read_dir(paths::data())
        .unwrap()
        .filter_map(|e| e.ok())
        .filter(|e| e.file_name().to_string_lossy().starts_with("mariadb-palsu.gagal-"))
        .collect();
    assert_eq!(sisih.len(), 1);
    assert!(ada_log(&r, "Sisa percobaan yang gagal dipindah ke"));
    assert!(ada_log(&r, "Folder data mariadb-palsu belum ada"));

    // Folder yang sudah berisi sesuatu bukan buatan kita: tidak dipindah.
    fs::create_dir_all(&data_dir).unwrap();
    fs::write(Path::new(&data_dir).join("punya-orang.txt"), b"x").unwrap();
    assert!(!l.start_db(&p, Some(&m)));
    assert!(Path::new(&data_dir).join("punya-orang.txt").is_file());
    let _ = fs::remove_dir_all(&d);
}

#[test]
fn exe_hilang_berakhir_gagal() {
    let (_g, d) = paths::sandbox_uji("svc-exehilang");
    let ap = BinPackage::baru(BinKind::Apache, "httpd-hilang", &jalur(&d.join(r"bin\httpd-hilang")), "2.4.54");
    let p = Profile { apache_id: ap.id.clone(), http_port: port_bebas() as i32, ..Profile::default() };
    let (l, r) = rekam();
    for _ in 0..2 {
        // Kedua kalinya juga harus berjalan: start yang gagal tidak boleh
        // tertinggal sebagai "start yang masih berjalan".
        assert!(!l.start_web(&p, Some(&ap), None, &config::Hasil::default()));
        let s = l.status();
        assert_eq!(s.web, ServiceState::Gagal, "tersangkut di {:?}", s.web);
        assert_eq!(s.galat_web.unwrap().kunci, "Web server tidak bisa dinyalakan: {0}");
    }
    assert_eq!(hitung_log(&r, "Web server tidak bisa dinyalakan: "), 2);
    // Kabar: Menyalakan lalu Gagal.
    let urut: Vec<ServiceState> = kunci(&r)
        .iter()
        .filter_map(|x| match x {
            Peristiwa::Status(s) => Some(s.web),
            _ => None,
        })
        .collect();
    assert_eq!(urut, vec![ServiceState::Menyalakan, ServiceState::Gagal, ServiceState::Menyalakan, ServiceState::Gagal]);
    let _ = fs::remove_dir_all(&d);
}

#[test]
fn port_terpakai_disebut_pemegangnya() {
    let l0 = TcpListener::bind("127.0.0.1:0").unwrap();
    let dipakai = l0.local_addr().unwrap().port();
    let ap = BinPackage::baru(BinKind::Apache, "httpd", r"Z:\tidak-ada", "2.4.54");
    let my = BinPackage::baru(BinKind::MySql, "mysql", r"Z:\tidak-ada", "8.0.30");
    let p = Profile {
        apache_id: "httpd".into(),
        mysql_id: "mysql".into(),
        http_port: dipakai as i32,
        mysql_port: dipakai as i32,
        ..Profile::default()
    };
    let (l, r) = rekam();
    assert!(!l.start_web(&p, Some(&ap), None, &config::Hasil::default()));
    let g = l.status().galat_web.unwrap();
    assert_eq!(g.kunci, "Port {0} sedang dipakai {1} (PID {2}). Matikan dulu aplikasi itu atau ganti port profil.");
    assert_eq!(g.args[0], dipakai.to_string());
    assert_eq!(g.args[2], std::process::id().to_string());

    // HTTPS ikut diperiksa bila config membukanya.
    let q = Profile { http_port: port_bebas() as i32, https_port: dipakai as i32, ..p.clone() };
    let cfg = config::Hasil { ssl_enabled: true, ..config::Hasil::default() };
    assert!(!l.start_web(&q, Some(&ap), None, &cfg));
    assert!(l.status().galat_web.unwrap().kunci.ends_with(" HTTPS memakai port itu, dan Apache menolak start kalau salah satu portnya terpakai."));

    assert!(!l.start_db(&p, Some(&my)));
    assert_eq!(
        l.status().galat_db.unwrap().kunci,
        "Port {0} sedang dipakai {1} (PID {2}). Matikan dulu, atau ubah port MySQL profil ini."
    );
    assert!(ada_log(&r, &format!("Port {dipakai} sedang dipakai")));
    drop(l0);
}

#[test]
fn tanpa_paket_dan_stop_tanpa_proses() {
    let (l, r) = rekam();
    assert!(!l.start_web(&Profile::default(), None, None, &config::Hasil::default()));
    assert_eq!(l.status().galat_web.unwrap().kunci, "Profil belum menunjuk web server.");
    // MySQL tanpa paket: dikeluhkan, keadaan tidak diubah.
    assert!(!l.start_db(&Profile::default(), None));
    assert_eq!(l.db_state(), ServiceState::Berhenti);
    assert!(ada_log(&r, "Profil belum menunjuk versi MySQL."));

    l.stop_db(None, None);
    assert_eq!(l.db_state(), ServiceState::Berhenti);
    assert!(!ada_log(&r, "Meminta MySQL berhenti"));
    l.stop_web();
    let s = status_terakhir(&r).unwrap();
    assert_eq!(s.web, ServiceState::Berhenti);
    assert!(s.galat_web.is_none(), "galat lama ikut terhapus saat berhenti");
    assert!(ada_log(&r, "Web server dimatikan."));
    l.stop_all();
    assert_eq!((l.web_state(), l.db_state()), (ServiceState::Berhenti, ServiceState::Berhenti));
}

#[test]
fn event_mysql_yang_tidak_ada() {
    assert!(!minta_mysql_berhenti(std::process::id()));
    assert!(!minta_mysql_berhenti(u32::MAX - 3));
}

#[test]
fn sisa_tidak_dicari_saat_layanan_jalan() {
    let l0 = TcpListener::bind("127.0.0.1:0").unwrap();
    let dipakai = l0.local_addr().unwrap().port();
    let p = Profile { apache_id: "httpd".into(), http_port: dipakai as i32, ..Profile::default() };
    let (l, _r) = rekam();
    // Pemegang port ini proses uji, bukan proses jenis milik Phoron.
    assert!(l.sisa_proses(&p, false).is_empty());
    assert!(l.hentikan_sisa(&p, None, false).is_empty());
    assert!(!port::is_free(dipakai), "proses yang bukan milik Phoron tidak boleh disentuh");
    l.ubah(ServiceKind::Db, ServiceState::Jalan, None);
    assert!(l.sisa_proses(&p, false).is_empty());
    drop(l0);
}

#[test]
fn ekor_log_apache_tanpa_baris_kosong() {
    let (_g, d) = paths::sandbox_uji("svc-ekor");
    assert_eq!(web::ekor_log_apache(6), "");
    let mut isi = Vec::new();
    for i in 1..=8 {
        isi.extend_from_slice(format!("baris {i}\r\n").as_bytes());
    }
    isi.extend_from_slice(b"  \r\n\xff\xfe rusak\r\n");
    fs::write(paths::logs().join("apache-error.log"), &isi).unwrap();
    let e = web::ekor_log_apache(6);
    assert_eq!(e, "baris 5\r\nbaris 6\r\nbaris 7\r\nbaris 8\r\n\u{fffd}\u{fffd} rusak");
    let _ = fs::remove_dir_all(&d);
}

// ------------------------------------------------------------ uji nyata

/// Apache + PHP 8.1 (mod_php) dari Laragon: halaman PHP lewat HTTP, dua start
/// serentak = satu httpd, httpd yang dibunuh dari luar = Gagal, stop bersih.
#[test]
fn nyata_apache_php() {
    let (Some(ap), Some(php)) = (paket(APACHE), paket(PHP81)) else {
        eprintln!("dilewati: paket Laragon tidak ada");
        return;
    };
    let (_g, d) = paths::sandbox_uji("svc-apache");
    let jaga = Penjaga::baru(&[r"C:\laragon\bin", r"C:\xampp"]);
    let p = profil_web(&d, "apache", &ap, &php);
    // Log akses MENYALA: ditulis ke logs Phoron, bukan ke logs\ paket.
    let opsi = Opsi { log_akses: true, ..opsi_uji() };
    let cfg = config::build(&p, Some(&php), Some(&ap), None, None, &[], &opsi, Vec::new()).unwrap();
    assert!(!cfg.php_fastcgi, "PHP 8.1 TS memakai mod_php");
    let port = p.http_port as u16;

    let (l, r) = rekam();
    // Dua start serentak: yang kedua menunggu yang pertama.
    let l2 = l.clone();
    let (p2, ap2, php2, cfg2) = (p.clone(), ap.clone(), php.clone(), cfg.clone());
    let t = std::thread::spawn(move || l2.start_web(&p2, Some(&ap2), Some(&php2), &cfg2));
    let a = l.start_web(&p, Some(&ap), Some(&php), &cfg);
    let b = t.join().unwrap();
    assert!(a && b, "log: {:#?}", log(&r));
    assert_eq!(hitung_log(&r, "Apache 2.4.54 jalan di port"), 1, "dua start melahirkan dua httpd");
    assert_eq!(l.web_state(), ServiceState::Jalan);

    let halaman = ambil(port, "/uji.php").unwrap_or_default();
    assert!(halaman.contains("phoron-uji 8.1.10 apache2handler"), "halaman: {halaman}");
    let akses = paths::logs().join("apache-access.log");
    assert!(
        tunggu(|| fs::read_to_string(&akses).is_ok_and(|s| s.contains("GET /uji.php")), 5000),
        "log akses tidak ditulis ke logs Phoron"
    );

    // Mati sendiri sesudah jalan: dibunuh dari luar.
    let pid = l.web_pid();
    assert!(pid > 0);
    shell::kill_tree(pid);
    assert!(tunggu(|| l.web_state() == ServiceState::Gagal, 10_000));
    assert_eq!(l.status().galat_web.unwrap().kunci, "{0} berhenti sendiri (kode {1}). Lihat logs\\apache-error.log.");
    assert_eq!(l.web_pid(), 0);

    // Nyala lagi, lalu stop yang disengaja: TIDAK dilaporkan berhenti sendiri.
    assert!(l.start_web(&p, Some(&ap), Some(&php), &cfg));
    assert!(ambil(port, "/uji.php").unwrap_or_default().contains("phoron-uji"));
    let pid = l.web_pid();
    let anak = process_job::keturunan(pid);
    l.stop_web();
    assert_eq!(l.web_state(), ServiceState::Berhenti);
    std::thread::sleep(Duration::from_millis(500));
    assert_eq!(hitung_log(&r, "berhenti sendiri"), 1, "stop yang disengaja dilaporkan berhenti sendiri");
    assert!(!process_job::masih_hidup(pid) && anak.iter().all(|a| !process_job::masih_hidup(*a)));
    assert!(tunggu(|| port::is_free(port), 5000));
    assert_eq!(status_terakhir(&r).unwrap().web, ServiceState::Berhenti);
    jaga.pastikan_bersih();
    let _ = fs::remove_dir_all(&d);
}

/// Stop datang di tengah start (selama jeda pemeriksaan): start mundur diam,
/// tidak ada "berhenti seketika", tidak ada httpd yang tertinggal.
#[test]
fn nyata_stop_di_tengah_start() {
    let (Some(ap), Some(php)) = (paket(APACHE), paket(PHP81)) else { return };
    let (_g, d) = paths::sandbox_uji("svc-tengah");
    let jaga = Penjaga::baru(&[r"C:\laragon\bin", r"C:\xampp"]);
    let p = profil_web(&d, "apache", &ap, &php);
    let cfg = config::build(&p, Some(&php), Some(&ap), None, None, &[], &opsi_uji(), Vec::new()).unwrap();
    let (l, r) = rekam();
    for jeda in [50u64, 900] {
        let l2 = l.clone();
        let (p2, ap2, php2, cfg2) = (p.clone(), ap.clone(), php.clone(), cfg.clone());
        let t = std::thread::spawn(move || l2.start_web(&p2, Some(&ap2), Some(&php2), &cfg2));
        // 50 ms: di tengah httpd -t. 900 ms: biasanya sesudah httpd lahir.
        // Stop harus datang SESUDAH start mulai; stop yang mendahuluinya
        // memang tidak membatalkan apa pun.
        assert!(tunggu(|| l.web_state() == ServiceState::Menyalakan, 10_000));
        if jeda > 100 {
            assert!(tunggu(|| l.web_pid() != 0, 10_000), "httpd tidak pernah lahir");
        } else {
            std::thread::sleep(Duration::from_millis(jeda));
        }
        l.stop_web();
        let hasil = t.join().unwrap();
        assert!(!hasil, "start tetap melapor berhasil sesudah stop (jeda {jeda})");
        assert_eq!(l.web_state(), ServiceState::Berhenti);
        assert!(tunggu(|| port::is_free(p.http_port as u16), 5000), "Apache tetap hidup sesudah stop");
    }
    assert!(!ada_log(&r, "berhenti seketika"));
    assert!(!ada_log(&r, "berhenti sendiri"));
    jaga.pastikan_bersih();
    let _ = fs::remove_dir_all(&d);
}

/// Config rusak ditolak `httpd -t` sebelum httpd dilahirkan.
#[test]
fn nyata_config_ditolak() {
    let (Some(ap), Some(php)) = (paket(APACHE), paket(PHP81)) else { return };
    let (_g, d) = paths::sandbox_uji("svc-tolak");
    let jaga = Penjaga::baru(&[r"C:\laragon\bin", r"C:\xampp"]);
    let p = profil_web(&d, "apache", &ap, &php);
    let cfg = config::build(&p, Some(&php), Some(&ap), None, None, &[], &opsi_uji(), Vec::new()).unwrap();
    let conf = cfg.httpd_conf.clone().unwrap();
    let mut isi = fs::read_to_string(&conf).unwrap();
    isi.push_str("\r\nDirektifNgawur On\r\n");
    fs::write(&conf, isi).unwrap();
    let (l, _r) = rekam();
    assert!(!l.start_web(&p, Some(&ap), Some(&php), &cfg));
    let g = l.status().galat_web.unwrap();
    assert_eq!(g.kunci, "Konfigurasi Apache ditolak:\r\n{0}{1}");
    assert!(g.args[0].contains("DirektifNgawur"), "keluaran httpd -t: {}", g.args[0]);
    assert_eq!(l.web_pid(), 0);
    jaga.pastikan_bersih();
    let _ = fs::remove_dir_all(&d);
}

/// Stop lalu Start cepat selagi start lama masih di `httpd -t`: start baru
/// TIDAK memakai ulang hasil start yang dibatalkan, jadi Apache akhirnya
/// jalan. Dulu start kedua menerima `false` milik yang pertama.
#[test]
fn nyata_stop_lalu_start_cepat() {
    let (Some(ap), Some(php)) = (paket(APACHE), paket(PHP81)) else { return };
    let (_g, d) = paths::sandbox_uji("svc-stopstart");
    let jaga = Penjaga::baru(&[r"C:\laragon\bin", r"C:\xampp"]);
    let p = profil_web(&d, "apache", &ap, &php);
    let cfg = config::build(&p, Some(&php), Some(&ap), None, None, &[], &opsi_uji(), Vec::new()).unwrap();
    let (l, r) = rekam();
    let l2 = l.clone();
    let (p2, ap2, php2, cfg2) = (p.clone(), ap.clone(), php.clone(), cfg.clone());
    let t = std::thread::spawn(move || l2.start_web(&p2, Some(&ap2), Some(&php2), &cfg2));
    assert!(tunggu(|| l.web_state() == ServiceState::Menyalakan, 10_000));
    std::thread::sleep(Duration::from_millis(30));
    l.stop_web();
    // Start baru selagi start lama mungkin belum selesai berbenah.
    let baru = l.start_web(&p, Some(&ap), Some(&php), &cfg);
    assert!(!t.join().unwrap(), "start yang dibatalkan melapor berhasil");
    assert!(baru, "start sesudah stop tidak menyalakan apa pun: {:#?}", log(&r));
    assert_eq!(l.web_state(), ServiceState::Jalan);
    assert!(ambil(p.http_port as u16, "/uji.php").unwrap_or_default().contains("phoron-uji"));
    assert_eq!(hitung_log(&r, "Apache 2.4.54 jalan di port"), 1);
    l.stop_web();
    assert!(tunggu(|| port::is_free(p.http_port as u16), 5000));
    jaga.pastikan_bersih();
    let _ = fs::remove_dir_all(&d);
}

/// Badan start yang panik (daemon memakai panic=unwind) tidak meninggalkan
/// Menyalakan selamanya, dan start berikutnya tidak menunggu selamanya.
#[test]
fn panik_di_start_berakhir_gagal() {
    let (l, r) = rekam();
    for jenis in [ServiceKind::Web, ServiceKind::Db] {
        let l2 = l.clone();
        let hasil = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
            l2.antre(jenis, |_| {
                l2.ubah(jenis, ServiceState::Menyalakan, None);
                panic!("panik uji");
            })
        }));
        assert!(hasil.is_err());
        let s = l.status();
        let (state, galat) = match jenis {
            ServiceKind::Web => (s.web, s.galat_web),
            ServiceKind::Db => (s.db, s.galat_db),
        };
        assert_eq!(state, ServiceState::Gagal, "{jenis:?} tertinggal di {state:?}");
        assert!(galat.unwrap().kunci.ends_with("tidak bisa dinyalakan karena galat di dalam Phoron."));
        // Start berikutnya berjalan (tidak menunggu Mulai yang tak pernah selesai).
        let (tx, rx) = mpsc::channel();
        let l3 = l.clone();
        std::thread::spawn(move || {
            let _ = tx.send(l3.antre(jenis, |_| Ok(true)));
        });
        assert_eq!(rx.recv_timeout(Duration::from_secs(5)), Ok(true), "start sesudah panik macet");
    }
    assert!(ada_log(&r, "MySQL tidak bisa dinyalakan karena galat di dalam Phoron."));
}

/// Start yang dibatalkan stop tidak dipakai ulang, start pada giliran yang
/// sama dipakai ulang.
#[test]
fn start_basi_tidak_dipakai_ulang() {
    let (l, _r) = rekam();
    let (mulai_tx, mulai_rx) = mpsc::channel();
    let (lepas_tx, lepas_rx) = mpsc::channel::<()>();
    let l1 = l.clone();
    let t1 = std::thread::spawn(move || {
        l1.antre(ServiceKind::Web, |_| {
            mulai_tx.send(()).unwrap();
            lepas_rx.recv().unwrap();
            Ok(false)
        })
    });
    mulai_rx.recv().unwrap();
    // Giliran sama: menunggu dan menerima hasil start pertama.
    let l2 = l.clone();
    let t2 = std::thread::spawn(move || l2.antre(ServiceKind::Web, |_| Ok(true)));
    std::thread::sleep(Duration::from_millis(100));
    // Sesudah stop: start baru menunggu yang lama berbenah, lalu jalan sendiri.
    l.naikkan_giliran(ServiceKind::Web);
    let l3 = l.clone();
    let t3 = std::thread::spawn(move || l3.antre(ServiceKind::Web, |_| Ok(true)));
    std::thread::sleep(Duration::from_millis(100));
    assert!(!t3.is_finished(), "start baru tidak menunggu start lama selesai berbenah");
    lepas_tx.send(()).unwrap();
    assert!(!t1.join().unwrap());
    assert!(!t2.join().unwrap(), "start pada giliran yang sama tidak memakai ulang hasilnya");
    assert!(t3.join().unwrap(), "start sesudah stop memakai ulang hasil start yang dibatalkan");
}

/// Penanda init yang tertinggal (daemon mati di tengah init): folder data
/// setengah jadi TIDAK dianggap siap, sisanya disisihkan dulu.
#[test]
fn init_terputus_tidak_dianggap_siap() {
    let (_g, d) = paths::sandbox_uji("svc-initputus");
    let bin = d.join(r"bin\mariadb-palsu\bin");
    fs::create_dir_all(&bin).unwrap();
    let sistem = std::env::var("SystemRoot").unwrap();
    fs::copy(Path::new(&sistem).join(r"System32\findstr.exe"), bin.join("mysql_install_db.exe")).unwrap();
    let m = BinPackage::baru(BinKind::MySql, "mariadb-palsu", &jalur(&d.join(r"bin\mariadb-palsu")), "10.4.0");
    let p = Profile { mysql_id: m.id.clone(), mysql_port: port_bebas() as i32, ..Profile::default() };
    config::write_my_ini(&p, &m).unwrap();
    let data_dir = config::mysql_data_dir(&m);
    // Sisa init yang terputus: folder mysql\ sudah ada, penanda masih ada.
    fs::create_dir_all(Path::new(&data_dir).join("mysql")).unwrap();
    fs::write(db::penanda_init(&data_dir), "1").unwrap();
    let (l, r) = rekam();
    assert!(!l.start_db(&p, Some(&m)));
    assert!(ada_log(&r, "Penyiapan folder data sebelumnya terputus sebelum selesai."), "{:#?}", log(&r));
    assert!(ada_log(&r, "Folder data mariadb-palsu belum ada"), "folder setengah jadi dikira siap");
    // Dua sisa (yang terputus dan yang baru gagal) disisihkan, tidak berebut nama.
    let sisih = fs::read_dir(paths::data())
        .unwrap()
        .filter_map(|e| e.ok())
        .filter(|e| e.file_name().to_string_lossy().starts_with("mariadb-palsu.gagal-"))
        .count();
    assert_eq!(sisih, 2, "{:#?}", log(&r));
    assert!(!db::penanda_init(&data_dir).exists());
    let _ = fs::remove_dir_all(&d);
}

/// Nginx + php-cgi FastCGI + satu kolam PHP 7.4 per situs. php-cgi yang
/// dibunuh dari luar dinyalakan ulang. Nginx dijalankan LANGSUNG dari folder
/// Laragon (tidak disalin): awalannya (`-p`) folder Phoron, jadi logs dan
/// temp-nya tidak menulisi folder paket.
#[test]
fn nyata_nginx_fastcgi_bangkit() {
    let (Some(ng), Some(php)) = (paket(NGINX), paket(PHP81)) else { return };
    let php74 = paket(r"C:\laragon\bin\php\php-7.4.22-Win32-VC15-x64");
    if !port::is_free(config::FASTCGI_PORT as u16) || !port::is_free(config::FASTCGI_PORT as u16 + 1) {
        eprintln!("dilewati: port FastCGI 9123/9124 dipakai");
        return;
    }
    let (_g, d) = paths::sandbox_uji("svc-nginx");
    let jaga = Penjaga::baru(&[r"C:\laragon\bin", r"C:\xampp"]);
    let p = profil_web(&d, "nginx", &ng, &php);
    let www = d.join("www");
    let mut kolam = Vec::new();
    let mut situs = Vec::new();
    if let Some(k) = &php74 {
        let lama = www.join("lama");
        fs::create_dir_all(&lama).unwrap();
        fs::write(lama.join("index.php"), "<?php echo 'kolam ' . PHP_VERSION;").unwrap();
        let s = Site {
            folder: "lama".into(),
            path: jalur(&lama),
            host_name: "lama.test".into(),
            root: jalur(&www),
            doc_root: jalur(&lama),
            ..Site::default()
        };
        situs.push(s.clone());
        kolam.push(config::Kolam::baru(k.clone(), Vec::new(), Vec::new(), vec![s]));
    }
    let cfg = config::build(&p, Some(&php), None, None, Some(&ng), &situs, &opsi_uji(), kolam).unwrap();
    let port = p.http_port as u16;
    let (l, r) = rekam();
    assert!(l.start_web(&p, Some(&ng), Some(&php), &cfg), "log: {:#?}", log(&r));
    assert!(ada_log(&r, "php-cgi (FastCGI) jalan di 127.0.0.1:9123."));
    let halaman = ambil(port, "/uji.php").unwrap_or_default();
    assert!(halaman.contains("phoron-uji 8.1.10 cgi-fcgi"), "halaman: {halaman}");
    if php74.is_some() {
        assert_eq!(cfg.kolam.len(), 1);
        assert!(ada_log(&r, "PHP 7.4.22 jalan di 127.0.0.1:9124 untuk lama."), "log: {:#?}", log(&r));
        let h = ambil(port, "/lama/index.php").unwrap_or_default();
        assert!(h.contains("kolam 7.4.22"), "halaman kolam: {h}");
    }

    // php-cgi profil dibunuh dari luar: dinyalakan ulang, halaman pulih.
    let fcgi_lama = l.data().fcgi.as_ref().map(|p| p.pid).unwrap();
    shell::kill_tree(fcgi_lama);
    assert!(
        tunggu(|| l.data().fcgi.as_ref().is_some_and(|p| p.pid != fcgi_lama && !p.keluar()), 10_000),
        "log: {:#?}",
        log(&r)
    );
    assert!(ada_log(&r, "php-cgi berhenti sendiri (kode 1) - dinyalakan ulang."));
    assert!(tunggu(|| ambil(port, "/uji.php").unwrap_or_default().contains("phoron-uji"), 5000));
    assert_eq!(l.web_state(), ServiceState::Jalan, "php-cgi yang mati tidak boleh menggagalkan web server");

    let pids: Vec<u32> = std::iter::once(l.web_pid()).chain(l.php_cgi_pid()).collect();
    l.stop_web();
    assert!(tunggu(|| pids.iter().all(|p| !process_job::masih_hidup(*p)), 5000));
    assert!(l.php_cgi_pid().is_empty());
    assert!(tunggu(|| port::is_free(port) && port::is_free(9123) && port::is_free(9124), 5000));
    std::thread::sleep(Duration::from_millis(300));
    assert!(!ada_log(&r, "nginx berhenti sendiri"));
    jaga.pastikan_bersih();
    let _ = fs::remove_dir_all(&d);
}

/// Jalankan satu kueri lewat mysql.exe paket itu.
fn kueri(m: &BinPackage, port: u16, sql: &str) -> String {
    let exe = jalur(&Path::new(&m.path).join(r"bin\mysql.exe"));
    let r = shell::run(
        &exe,
        &format!("--protocol=tcp -h 127.0.0.1 --port={port} -u root -N -B -e \"{sql}\""),
        Some(Path::new(&m.path)),
        20_000,
        None,
        None,
    )
    .unwrap();
    r.all()
}

/// MySQL 8.0 Laragon dari folder data kosong di sandbox: init, SELECT
/// VERSION(), lalu stop RAPI ("Shutdown complete" di mysql-error.log).
#[test]
fn nyata_mysql_init_dan_rapi() {
    let Some(m) = paket(MYSQL8) else { return };
    let (_g, d) = paths::sandbox_uji("svc-mysql");
    let jaga = Penjaga::baru(&[r"C:\laragon\bin", r"C:\xampp"]);
    let p = Profile { name: "Uji MySQL".into(), mysql_id: m.id.clone(), mysql_port: port_bebas() as i32, ..Profile::default() };
    let cfg = config::build(&p, None, None, Some(&m), None, &[], &opsi_uji(), Vec::new()).unwrap();
    assert!(cfg.my_ini.is_some());
    let port = p.mysql_port as u16;
    let data_dir = config::mysql_data_dir(&m);
    assert!(data_dir.starts_with(&jalur(&d)), "folder data di luar sandbox: {data_dir}");

    let (l, r) = rekam();
    assert!(l.start_db(&p, Some(&m)), "log: {:#?}", log(&r));
    assert!(ada_log(&r, "Basis data siap. Pengguna: root, tanpa kata sandi."));
    assert!(tabel_sistem_ada(&data_dir));
    assert_eq!(config::pemilik_data(&data_dir).as_deref(), Some(m.path.as_str()));
    let v = kueri(&m, port, "SELECT VERSION()");
    assert!(v.contains("8.0.30"), "SELECT VERSION(): {v}");

    // Start kedua saat sudah jalan: langsung benar, tidak ada mysqld kedua.
    let pid = l.db_pid();
    assert!(l.start_db(&p, Some(&m)));
    assert_eq!(l.db_pid(), pid);

    l.stop_db(Some(&p), Some(&m));
    assert_eq!(l.db_state(), ServiceState::Berhenti);
    assert!(ada_log(&r, "Meminta MySQL berhenti dengan rapi..."));
    assert!(!ada_log(&r, "dimatikan paksa"), "log: {:#?}", log(&r));
    std::thread::sleep(Duration::from_millis(500));
    assert!(!ada_log(&r, "MySQL berhenti sendiri"), "penghentian rapi dilaporkan berhenti sendiri");
    let isi_log = String::from_utf8_lossy(&fs::read(mysql_error_log()).unwrap()).into_owned();
    assert!(isi_log.contains("Shutdown complete"), "mysqld tidak berhenti rapi");
    assert!(!process_job::masih_hidup(pid));

    // Nyala lagi dari folder data yang sudah ada, lalu stop_all: tetap rapi.
    assert!(l.start_db(&p, Some(&m)));
    assert!(!ada_log(&r, "Gagal menyiapkan"));
    l.stop_all();
    assert!(ada_log(&r, "MySQL dimatikan dengan rapi."));
    assert_eq!(status_terakhir(&r).unwrap().db, ServiceState::Berhenti);
    assert!(tunggu(|| port::is_free(port), 5000));
    jaga.pastikan_bersih();
    let _ = fs::remove_dir_all(&d);
}

/// Stop lalu Start cepat selagi `mysqld --initialize` masih berjalan: init
/// dibatalkan (prosesnya mati, terikat Job), folder setengah jadinya
/// disisihkan, dan start baru menyiapkan folder dari awal lalu Jalan. Dulu
/// init tidak bisa dibatalkan dan start baru menerima `false` milik start
/// yang dibatalkan.
#[test]
fn nyata_mysql_stop_lalu_start_saat_init() {
    let Some(m) = paket(MYSQL8) else { return };
    let (_g, d) = paths::sandbox_uji("svc-mysql-batal");
    let jaga = Penjaga::baru(&[r"C:\laragon\bin", r"C:\xampp"]);
    let p = Profile { name: "Uji MySQL".into(), mysql_id: m.id.clone(), mysql_port: port_bebas() as i32, ..Profile::default() };
    config::build(&p, None, None, Some(&m), None, &[], &opsi_uji(), Vec::new()).unwrap();
    let data_dir = config::mysql_data_dir(&m);
    let (l, r) = rekam();
    let l2 = l.clone();
    let (p2, m2) = (p.clone(), m.clone());
    let t = std::thread::spawn(move || l2.start_db(&p2, Some(&m2)));
    assert!(tunggu(|| ada_log(&r, "belum ada - menyiapkan basis data awal"), 10_000));
    // Init benar-benar sudah menulis sesuatu ke folder data.
    assert!(tunggu(|| fs::read_dir(&data_dir).is_ok_and(|mut x| x.next().is_some()), 20_000));
    let init = proses_di(&[m.path.as_str()]);
    assert!(!init.is_empty(), "mysqld --initialize tidak terlihat");
    l.stop_db(Some(&p), Some(&m));
    let baru = l.start_db(&p, Some(&m));
    assert!(!t.join().unwrap(), "start yang dibatalkan melapor berhasil");
    assert!(baru, "start sesudah stop tidak menyalakan MySQL: {:#?}", log(&r));
    assert_eq!(l.db_state(), ServiceState::Jalan);
    assert!(ada_log(&r, "Penyiapan folder data dibatalkan."), "{:#?}", log(&r));
    assert!(ada_log(&r, "Sisa percobaan yang gagal dipindah ke"));
    assert!(init.iter().all(|x| !process_job::masih_hidup(x.0)), "init yang dibatalkan masih hidup");
    assert!(!db::penanda_init(&data_dir).exists());
    assert!(kueri(&m, p.mysql_port as u16, "SELECT VERSION()").contains("8.0.30"));
    l.stop_db(Some(&p), Some(&m));
    assert!(!ada_log(&r, "dimatikan paksa"), "log: {:#?}", log(&r));
    assert!(tunggu(|| port::is_free(p.mysql_port as u16), 5000));
    jaga.pastikan_bersih();
    let _ = fs::remove_dir_all(&d);
}

/// MariaDB XAMPP: init lewat mysql_install_db, kueri, stop rapi.
#[test]
fn nyata_mariadb_xampp() {
    let Some(m) = paket(XAMPP_MYSQL) else { return };
    let (_g, d) = paths::sandbox_uji("svc-mariadb");
    let jaga = Penjaga::baru(&[r"C:\laragon\bin", r"C:\xampp"]);
    let p = Profile { name: "Uji MariaDB".into(), mysql_id: m.id.clone(), mysql_port: port_bebas() as i32, ..Profile::default() };
    config::build(&p, None, None, Some(&m), None, &[], &opsi_uji(), Vec::new()).unwrap();
    let data_dir = config::mysql_data_dir(&m);
    assert!(data_dir.starts_with(&jalur(&d)));
    let (l, r) = rekam();
    assert!(l.start_db(&p, Some(&m)), "log: {:#?}", log(&r));
    let v = kueri(&m, p.mysql_port as u16, "SELECT VERSION()");
    assert!(v.to_lowercase().contains("mariadb"), "SELECT VERSION(): {v}");
    l.stop_db(None, None);
    assert!(!ada_log(&r, "dimatikan paksa"), "log: {:#?}", log(&r));
    assert!(tunggu(|| port::is_free(p.mysql_port as u16), 5000));
    jaga.pastikan_bersih();
    let _ = fs::remove_dir_all(&d);
}
