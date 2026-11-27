//! Uji Engine. Uji `nyata_*` memakai paket SUNGGUHAN di C:\laragon\bin (hanya
//! dibaca; setelan, profil, config, data MySQL, log, dan berkas hosts selalu di
//! sandbox) dan dilewati bila paketnya tidak ada. Port uji selalu acak dan bebas.

use super::*;
use crate::model::kamus_set;
use crate::protokol::StatusLayanan;
use std::fs;
use std::io::{Read, Write};
use std::net::{TcpListener, TcpStream};
use std::path::PathBuf;
use std::sync::OnceLock;
use std::time::{Duration, Instant};

// ------------------------------------------------------------ pembantu

/// Sandbox dengan berkas hosts sendiri: Terapkan menyunting hosts bila
/// "kelola hosts" menyala, dan berkas sistem tidak boleh tersentuh.
fn sandbox(nama: &str) -> (MutexGuard<'static, ()>, PathBuf) {
    let (g, d) = paths::sandbox_uji(nama);
    fs::write(d.join("hosts"), "").unwrap();
    paths::set_hosts_file(Some(d.join("hosts")));
    (g, d)
}

/// phoron.ini dengan folder bin yang kosong (tanpa memindai Laragon/XAMPP).
fn setelan(d: &Path, tambahan: &str) {
    let bin = d.join("bin-kosong");
    fs::write(d.join("phoron.ini"), format!("[umum]\r\nbin_roots={}\r\n{tambahan}", bin.display())).unwrap();
}

fn rekam() -> (Engine, Arc<Mutex<Vec<Kabar>>>) {
    let r: Arc<Mutex<Vec<Kabar>>> = Arc::new(Mutex::new(Vec::new()));
    let r2 = r.clone();
    (Engine::baru(move |k| kunci(&r2).push(k)), r)
}

fn log(e: &Engine) -> Vec<String> {
    e.riwayat_log().iter().map(|b| config::teks(&b.pesan)).collect()
}

fn hitung_log(e: &Engine, isi: &str) -> usize {
    log(e).iter().filter(|b| b.contains(isi)).count()
}

fn paket(kind: BinKind, id: &str, path: &str, versi: &str) -> BinPackage {
    BinPackage::baru(kind, id, path, versi)
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

fn simpan_profil(p: Profile) -> Profile {
    let mut p = p;
    profile_store::save(&mut p).unwrap();
    p
}

// ------------------------------------------------------------ paket

#[test]
fn find_jalur_kunci_dan_nama() {
    let mut a = paket(BinKind::MySql, "mysql", r"C:\xampp\mysql", "10.4.32");
    a.kembar = true;
    a.kunci = "mysql".into();
    let mut b = paket(BinKind::MySql, "mysql", r"D:\xampp\mysql", "10.1.38");
    b.kembar = true;
    b.kunci = "mysql@D-xampp".into();
    let php = paket(BinKind::Php, "php-8.3.12", r"C:\Phoron\bin\php\php-8.3.12", "8.3.12");
    let pk = vec![a, b, php];

    // Jalur lengkap menunjuk tepat satu kembaran, tanpa peka besar-kecil dan garis di ujung.
    assert_eq!(find(&pk, BinKind::MySql, r"d:\XAMPP\mysql\").unwrap().path, r"D:\xampp\mysql");
    // Kunci kembaran kedua.
    assert_eq!(find(&pk, BinKind::MySql, "MYSQL@D-xampp").unwrap().path, r"D:\xampp\mysql");
    // Nama polos: yang pertama.
    assert_eq!(find(&pk, BinKind::MySql, "mysql").unwrap().path, r"C:\xampp\mysql");
    // Jalur dari komputer lain: jatuh ke nama folder terakhirnya.
    assert_eq!(find(&pk, BinKind::Php, r"E:\lain\php-8.3.12").unwrap().id, "php-8.3.12");
    assert_eq!(find(&pk, BinKind::Php, "E:/lain/php-8.3.12/").unwrap().id, "php-8.3.12");
    // Jenis lain tidak tertukar, nilai kosong dan jalur tanpa nama = tidak ada.
    assert!(find(&pk, BinKind::Apache, "mysql").is_none());
    assert!(find(&pk, BinKind::Php, "").is_none());
    assert!(find(&pk, BinKind::Php, r"C:\").is_none());
}

#[test]
fn pakai_kosong_tetap_kosong() {
    let pk = vec![
        paket(BinKind::MySql, "mariadb-10.1.38", r"D:\xampp\mysql", "10.1.38"),
        paket(BinKind::Php, "php-8.3", r"C:\php83", "8.3.12"),
    ];
    // "(tidak dipakai)" bukan "pakai yang tertinggi": dulu menyalakan MariaDB XAMPP di 3306.
    assert!(pakai(&pk, BinKind::MySql, "", "php-8.3").is_none());
    let p = Profile { php_id: "php-8.3".into(), ..Profile::default() };
    let pp = PaketProfil::dari(&pk, &p);
    assert!(pp.mysql.is_none() && pp.apache.is_none());
    assert!(penyesuaian(&pk, &p).is_empty(), "pilihan kosong bukan penyesuaian");
}

#[test]
fn pakai_mengganti_dan_melapor() {
    let mut php = paket(BinKind::Php, "php-7.4", r"C:\php74", "7.4.33");
    php.compiler = "vc15".into();
    php.arch = "x64".into();
    let mut a15 = paket(BinKind::Apache, "httpd-vc15", r"C:\a15", "2.4.50");
    a15.compiler = "VC15".into();
    a15.arch = "x64".into();
    let mut a16 = paket(BinKind::Apache, "httpd-vs16", r"C:\a16", "2.4.62");
    a16.compiler = "VS16".into();
    a16.arch = "x64".into();
    let pk = vec![
        php,
        paket(BinKind::Php, "php-5.6", r"C:\php56", "5.6.40"),
        a16,
        a15,
        paket(BinKind::MySql, "mysql-5.7", r"C:\m57", "5.7.44"),
        paket(BinKind::MySql, "mysql-8.0", r"C:\m80", "8.0.30"),
    ];
    let mut p = Profile {
        php_id: "php-7.4".into(),
        apache_id: "httpd-dari-komputer-lain".into(),
        mysql_id: "mysql-hilang".into(),
        ..Profile::default()
    };
    kamus_set(&mut p.php_per_situs, r"D:\www\lama\", "php-5.4-hilang");
    // Apache pengganti dipilih lewat toolset PHP profil, bukan versi tertinggi.
    assert_eq!(pakai(&pk, BinKind::Apache, &p.apache_id, &p.php_id).unwrap().id, "httpd-vc15");
    assert_eq!(pakai(&pk, BinKind::MySql, &p.mysql_id, &p.php_id).unwrap().id, "mysql-8.0");

    let w = penyesuaian(&pk, &p);
    let teks: Vec<String> = w.iter().map(config::teks).collect();
    assert_eq!(teks.len(), 3, "{teks:#?}");
    assert_eq!(
        teks[0],
        "Versi Apache \"httpd-dari-komputer-lain\" tidak ada di komputer ini; Phoron memakai \"httpd-vc15\". \
         Simpan di halaman Profil kalau ingin penggantian ini tetap."
    );
    assert!(teks[1].starts_with("Versi MySQL \"mysql-hilang\" tidak ada di komputer ini"));
    assert_eq!(
        teks[2],
        "Situs lama memakai PHP \"php-5.4-hilang\", yang tidak ada di komputer ini; sementara ia dilayani PHP profil."
    );
    // Tanpa pengganti sama sekali.
    let kosong: Vec<BinPackage> = Vec::new();
    let w = penyesuaian(&kosong, &Profile { php_id: "php-9".into(), ..Profile::default() });
    assert_eq!(
        config::teks(&w[0]),
        "Versi PHP \"php-9\" yang dicatat profil tidak ada di komputer ini, dan tidak ada gantinya. \
         Pasang versinya di halaman Versi, atau pilih yang lain di Profil."
    );
    // Profil Nginx memeriksa Nginx, bukan Apache.
    let n = Profile { web_server: "nginx".into(), apache_id: "hilang".into(), ..Profile::default() };
    assert!(penyesuaian(&kosong, &n).is_empty());
}

#[test]
fn profil_bawaan_php_terbaru() {
    let pk = vec![
        paket(BinKind::Php, "php-5.6", r"C:\php56", "5.6.40"),
        paket(BinKind::Php, "php-8.3", r"C:\php83", "8.3.12"),
        paket(BinKind::Php, "php-10.0", r"C:\php10", "10.0.0"),
    ];
    let pr = |f: &str, php: &str| Profile { file_name: f.into(), php_id: php.into(), ..Profile::default() };
    // Urutan abjad menaruh versi tertua di depan; yang dipilih tetap yang terbaru.
    let daftar = vec![pr("a-php-5", "php-5.6"), pr("b-php-10", "php-10.0"), pr("c-php-8", "php-8.3"), pr("d", "")];
    assert_eq!(profil_bawaan(&daftar, &pk).unwrap().file_name, "b-php-10");
    // Seri: yang pertama.
    let seri = vec![pr("x", "hilang"), pr("y", "hilang-juga")];
    assert_eq!(profil_bawaan(&seri, &pk).unwrap().file_name, "x");
    assert!(profil_bawaan(&[], &pk).is_none());
}

#[test]
fn kolam_per_versi() {
    let php83 = paket(BinKind::Php, "php-8.3", r"C:\php83", "8.3.12");
    let php56 = paket(BinKind::Php, "php-5.6", r"C:\php56", "5.6.40");
    let pk = vec![php83.clone(), php56.clone(), paket(BinKind::Php, "php-7.4", r"C:\php74", "7.4.33")];
    let situs = |n: &str| Site { folder: n.into(), path: format!(r"D:\www\{n}"), ..Site::default() };
    let sites = vec![situs("a"), situs("b"), situs("c"), situs("d")];
    let mut aktif = Profile { file_name: "aktif".into(), php_id: "php-8.3".into(), ..Profile::default() };
    kamus_set(&mut aktif.php_per_situs, r"D:\www\a", "php-5.6");
    kamus_set(&mut aktif.php_per_situs, r"d:\WWW\b\", "php-5.6");
    kamus_set(&mut aktif.php_per_situs, r"D:\www\c", r"C:\PHP83"); // sama dengan PHP profil, lewat jalur
    kamus_set(&mut aktif.php_per_situs, r"D:\www\dihapus", "php-7.4");
    kamus_set(&mut aktif.php_per_situs, r"D:\www\d", "php-7.4");
    let mut sumber = Profile { file_name: "lama".into(), php_id: "php-5.6".into(), ..Profile::default() };
    sumber.php_extensions = vec!["oci8_11g".into(), "mysql".into()];
    kamus_set(&mut sumber.php_ini_overrides, "short_open_tag", "On");
    let profiles = vec![aktif.clone(), sumber];

    let k = susun_kolam(&pk, &profiles, &aktif, &sites, Some(&php83));
    assert_eq!(k.len(), 2, "satu kolam per VERSI");
    assert_eq!(k[0].php.id, "php-5.6");
    assert_eq!(k[0].situs.iter().map(|s| s.folder.as_str()).collect::<Vec<_>>(), ["a", "b"]);
    // Ekstensi dari profil yang memakai 5.6 sebagai PHP utama, bukan dari profil aktif.
    assert_eq!(k[0].ekstensi, ["oci8_11g", "mysql"]);
    assert_eq!(k[0].timpa, [("short_open_tag".to_string(), "On".to_string())]);
    assert_eq!(k[1].php.id, "php-7.4");
    assert_eq!(k[1].situs.len(), 1, "folder yang sudah dihapus tidak dilayani");
    assert!(k[1].timpa.is_empty());
    let tanpa: Vec<Profile> = Vec::new();
    assert!(susun_kolam(&pk, &tanpa, &Profile::default(), &sites, None).is_empty());
}

#[test]
fn paket_kembar_disebut_sekali() {
    let (_g, d) = sandbox("eng-kembar");
    setelan(&d, "");
    let mut a = paket(BinKind::Php, "php-8.3.12", r"C:\Phoron\bin\php\php-8.3.12", "8.3.12");
    a.source_root = r"C:\Phoron\bin".into();
    let mut b = paket(BinKind::Php, "php-8.3.12", r"C:\laragon\bin\php\php-8.3.12", "8.3.12");
    b.source_root = r"C:\laragon\bin".into();
    let pk = vec![a, b];
    let p = Profile { php_id: "php-8.3.12".into(), ..Profile::default() };
    let w = paket_kembar(&pk, &p);
    assert_eq!(w.len(), 1);
    assert_eq!(
        config::teks(&w[0]),
        "Nama folder \"php-8.3.12\" ada di lebih dari satu folder bin (C:\\Phoron\\bin, C:\\laragon\\bin). \
         Yang dipakai adalah C:\\Phoron\\bin\\php\\php-8.3.12; pilih ulang di halaman Profil untuk mengunci \
         salinan yang Anda maksud."
    );
    // Jalur lengkap tidak ambigu.
    let jalur = Profile { php_id: r"C:\laragon\bin\php\php-8.3.12".into(), ..Profile::default() };
    assert!(paket_kembar(&pk, &jalur).is_empty());

    // Dicatat ke log sekali per sesi, padahal tiap Terapkan memuatnya.
    let (e, _) = rekam();
    let lain = Pesan::baru("Folder proyek tidak ada: X");
    let semua = vec![lain.clone(), w[0].clone()];
    e.catat_peringatan(&semua, &w);
    e.catat_peringatan(&semua, &w);
    assert_eq!(hitung_log(&e, "Peringatan: Nama folder"), 1);
    assert_eq!(hitung_log(&e, "Peringatan: Folder proyek tidak ada"), 2);
    let _ = fs::remove_dir_all(&d);
}

#[test]
fn perlu_restart_menurut_versi() {
    assert!(!perlu_restart(ServiceState::Jalan, BELUM_NYALA, 5), "belum pernah menyala di sesi ini");
    assert!(!perlu_restart(ServiceState::Jalan, 5, 5));
    assert!(perlu_restart(ServiceState::Jalan, 5, 6));
    assert!(!perlu_restart(ServiceState::Berhenti, 5, 6), "yang mati tidak perlu dinyalakan ulang");
    assert!(!perlu_restart(ServiceState::Menyalakan, 5, 6));
}

#[test]
fn galat_hosts_dibungkus() {
    let p = pesan_galat_hosts(&hosts::GalatHosts::TidakBerhak);
    assert!(p.kunci.starts_with("Berkas hosts tidak bisa ditulis - jalankan Phoron sebagai Administrator"));
    let p = pesan_galat_hosts(&hosts::GalatHosts::Tulis("disk penuh".into()));
    assert_eq!(config::teks(&p), "Gagal menyunting hosts: disk penuh", "tidak dibungkus dua kali");
    let p = pesan_galat_hosts(&hosts::GalatHosts::BarisHilang { jumlah: 3, contoh: "1.2.3.4 x".into() });
    assert!(config::teks(&p).starts_with("Gagal menyunting hosts: Penulisan berkas hosts dibatalkan: 3 baris"));
    assert_eq!(p.args, ["3", "1.2.3.4 x"]);
}

/// Teks halaman sambutan dari Engine.cs, diurai dari literal string C#-nya.
fn sambutan_cs() -> String {
    let cs = include_str!("../../../../src/Phoron.Core/Engine.cs");
    let mulai = cs.find("public static string HalamanSambutan()").unwrap();
    let mut s = String::new();
    for baris in cs[mulai..].lines().skip(1) {
        let t = baris.trim();
        if t.starts_with("//") || t == "{" || t == "return" {
            continue;
        }
        if t == "}" {
            break;
        }
        let a = t.find('"').unwrap();
        let b = t.rfind('"').unwrap();
        let mut it = t[a + 1..b].chars();
        while let Some(c) = it.next() {
            if c == '\\' {
                match it.next() {
                    Some('n') => s.push('\n'),
                    Some(x) => s.push(x),
                    None => {}
                }
            } else {
                s.push(c);
            }
        }
    }
    s
}

#[test]
fn sambutan_hanya_di_folder_kosong() {
    assert_eq!(HALAMAN_SAMBUTAN, sambutan_cs(), "halaman sambutan berbeda dari versi C#");
    let (_g, d) = sandbox("eng-sambutan");
    let www = d.join("www-uji");
    fs::create_dir_all(&www).unwrap();
    let p = Profile { project_roots: vec![www.to_string_lossy().into_owned()], ..Profile::default() };
    pastikan_halaman_sambutan(&p);
    assert_eq!(fs::read_to_string(www.join("index.php")).unwrap(), HALAMAN_SAMBUTAN);
    // Pengguna menghapusnya dan menaruh proyek: tidak pernah ditulis lagi.
    fs::remove_file(www.join("index.php")).unwrap();
    fs::create_dir_all(www.join("toko")).unwrap();
    pastikan_halaman_sambutan(&p);
    assert!(!www.join("index.php").exists());
    // Folder yang tidak ada tidak dibuat.
    let hilang = Profile { project_roots: vec![d.join("tidak-ada").to_string_lossy().into_owned()], ..Profile::default() };
    pastikan_halaman_sambutan(&hilang);
    assert!(!d.join("tidak-ada").exists());
    let _ = fs::remove_dir_all(&d);
}

// ------------------------------------------------------------ muat dan setelan

#[test]
fn jalan_pertama_menulis_phoron_ini() {
    let (_g, d) = sandbox("eng-pertama");
    assert!(!d.join("phoron.ini").exists());
    let (e, r) = rekam();
    assert!(d.join("phoron.ini").is_file(), "penanda akar ditulis sejak jalan pertama");
    assert!(e.profiles().is_empty() || e.packages().iter().any(|p| p.kind == BinKind::Php));
    assert!(kunci(&r).iter().any(|k| matches!(k, Kabar::PerluMuatUlang)));
    let _ = fs::remove_dir_all(&d);
}

#[test]
fn masalah_muat_tidak_menumpuk() {
    let (_g, d) = sandbox("eng-masalah");
    setelan(&d, "ngawur\r\n");
    fs::write(paths::profiles().join("rusak.ini"), "[profil]\r\nnama=Rusak\r\nbaris tanpa sama dengan\r\n").unwrap();
    let (e, _) = rekam();
    let m = e.masalah_muat();
    let teks: Vec<String> = m.iter().map(config::teks).collect();
    assert_eq!(teks.len(), 2, "{teks:#?}");
    assert!(teks[0].starts_with("phoron.ini: "));
    assert!(teks[1].starts_with("Profil rusak.ini ada yang tidak terbaca"));
    e.muat_ulang();
    e.muat_ulang();
    assert_eq!(e.masalah_muat().len(), 2, "keluhan menumpuk tiap muat ulang");
    // Ikut di peringatan Terapkan dan di Keadaan.
    let w = e.terapkan().unwrap();
    assert!(w.iter().any(|p| config::teks(p).starts_with("Profil rusak.ini")));
    assert_eq!(e.keadaan().masalah_muat.len(), 2);
    let _ = fs::remove_dir_all(&d);
}

#[test]
fn catatan_situs_hanya_dengan_vhost() {
    let (_g, d) = sandbox("eng-catatan");
    setelan(&d, "kelola_hosts=0\r\n");
    let hilang = d.join("proyek-hilang").to_string_lossy().into_owned();
    simpan_profil(Profile { name: "A".into(), project_roots: vec![hilang.clone()], ..Profile::default() });
    let (e, _) = rekam();
    assert!(e.site_warnings().is_empty(), "tanpa Virtual Host catatan pemindai dibuang");
    let mut s = e.settings();
    s.auto_vhost = true;
    e.simpan_setelan(s).unwrap();
    assert_eq!(config::teks(&e.site_warnings()[0]), format!("Folder proyek tidak ada: {hilang}"));
    let _ = fs::remove_dir_all(&d);
}

#[test]
fn simpan_setelan_ditulis_lalu_dimuat_ulang() {
    let (_g, d) = sandbox("eng-simpan");
    setelan(&d, "");
    let (e, r) = rekam();
    e.catat_situs_dibuka(r"D:\www\dari-baki");
    let mut s = e.settings();
    s.tema = "gelap".into();
    s.db_sandi = "rahasia-uji".into();
    s.log_rinci = true;
    // Salinan UI: situs terakhir dan profil aktif di dalamnya diabaikan.
    s.situs_terakhir = (0..9).map(|i| format!(r"D:\www\s{i}")).collect();
    s.active_profile = "dari-ui".into();
    kunci(&r).clear();
    e.simpan_setelan(s).unwrap();
    let isi = fs::read_to_string(d.join("phoron.ini")).unwrap();
    assert!(isi.contains("tema=gelap"));
    assert!(!isi.contains("rahasia-uji"), "sandi tersimpan terbuka");
    let baru = Settings::load();
    assert_eq!(baru.db_sandi, "rahasia-uji");
    assert_eq!(baru.situs_terakhir, [r"D:\www\dari-baki"], "situs terakhir milik catat_situs_dibuka");
    assert_eq!(baru.active_profile, "", "profil aktif milik ganti_profil");
    assert_eq!(e.settings().tema, "gelap");
    assert!(e.layanan().log_rinci(), "log rinci tidak sampai ke layanan");
    assert!(kunci(&r).iter().any(|k| matches!(k, Kabar::PerluMuatUlang)));
    let _ = fs::remove_dir_all(&d);
}

#[test]
fn situs_terakhir() {
    let (_g, d) = sandbox("eng-situs");
    setelan(&d, "kelola_hosts=0\r\n");
    let www = d.join("www");
    for n in ["a", "b", "c"] {
        fs::create_dir_all(www.join(n)).unwrap();
    }
    simpan_profil(Profile { name: "A".into(), project_roots: vec![www.to_string_lossy().into_owned()], ..Profile::default() });
    let (e, _) = rekam();
    let j = |n: &str| www.join(n).to_string_lossy().into_owned();
    e.catat_situs_dibuka("");
    for p in [j("a"), j("b"), r"D:\hilang".to_string(), j("c"), "x1".into(), "x2".into(), j("A").to_uppercase()] {
        e.catat_situs_dibuka(&p);
    }
    let s = e.settings().situs_terakhir;
    assert_eq!(s.len(), 5);
    assert_eq!(s[0], j("A").to_uppercase(), "yang terakhir dibuka paling depan, tanpa kembar");
    assert_eq!(s.iter().filter(|x| sama_abaikan_besar(x, &j("a"))).count(), 1);
    assert_eq!(Settings::load().situs_terakhir, s, "Engine yang menyimpan phoron.ini");
    // Hanya yang masih ada di profil ini.
    let t: Vec<String> = e.situs_terakhir(3).into_iter().map(|x| x.folder).collect();
    assert_eq!(t, ["a", "c"]);
    let _ = fs::remove_dir_all(&d);
}

// ------------------------------------------------------------ log dan node

#[test]
fn riwayat_dibatasi_dan_berurutan() {
    let (_g, d) = sandbox("eng-riwayat");
    setelan(&d, "");
    let diri: Arc<OnceLock<Engine>> = Arc::new(OnceLock::new());
    let terlihat = Arc::new(Mutex::new(Vec::<bool>::new()));
    let (diri2, terlihat2) = (diri.clone(), terlihat.clone());
    let e = Engine::baru(move |k| {
        if let (Kabar::Log(b), Some(e)) = (&k, diri2.get()) {
            // Pendengar sudah melihat barisnya di riwayat.
            kunci(&terlihat2).push(e.riwayat_log().last() == Some(b));
        }
    });
    let _ = diri.set(e.clone());
    for i in 0..205 {
        e.say(Pesan::dengan("baris {0}", &[&i.to_string()]));
    }
    let r = e.riwayat_log();
    assert_eq!(r.len(), RIWAYAT_MAKS);
    assert_eq!(config::teks(&r[0].pesan), "baris 5");
    assert!(Saat::urai_o(&r[0].waktu).is_some());
    assert!(kunci(&terlihat).iter().all(|x| *x) && kunci(&terlihat).len() == 205);
    let isi = fs::read_to_string(paths::logs().join("phoron.log")).unwrap();
    let akhir = isi.lines().last().unwrap();
    assert!(akhir.ends_with("  baris 204"), "{akhir}");
    assert_eq!(akhir.len(), "yyyy-MM-dd HH:mm:ss  baris 204".len());
    let _ = fs::remove_dir_all(&d);
}

#[test]
fn hsts_disisipkan_ke_keluaran() {
    let (_g, d) = sandbox("eng-hsts");
    setelan(&d, "");
    let (e, r) = rekam();
    let folder = r"D:\proyek\toko";
    sampaikan_hsts(&e.inti.siar, folder, "https://localhost:3000/", Some("max-age=31536000"));
    let baris = e.node().riwayat(folder);
    assert_eq!(baris.len(), 3);
    assert!(matches!(&baris[0], BarisNode::Pesan(p) if p.args == ["toko"]));
    assert_eq!(hitung_log(&e, "PERINGATAN HSTS: toko"), 1);
    assert_eq!(hitung_log(&e, "selama 365 hari"), 1);
    let keluaran: Vec<Option<Pesan>> = kunci(&r)
        .iter()
        .filter_map(|k| match k {
            Kabar::NodeKeluaran { path, pesan, .. } if path == folder => Some(pesan.clone()),
            _ => None,
        })
        .collect();
    assert_eq!(keluaran.len(), 3);
    assert!(keluaran.iter().all(Option::is_some), "baris buatan Phoron membawa pesan");
    // Tanpa header atau header kosong: diam.
    sampaikan_hsts(&e.inti.siar, folder, "https://localhost:3000/", None);
    sampaikan_hsts(&e.inti.siar, folder, "https://localhost:3000/", Some(""));
    assert_eq!(e.node().riwayat(folder).len(), 3);
    let _ = fs::remove_dir_all(&d);
}

#[test]
fn port_bebas_melewati_port_profil() {
    let (_g, d) = sandbox("eng-port");
    setelan(&d, "");
    let a = port_bebas();
    let b = port_bebas();
    simpan_profil(Profile { name: "P".into(), http_port: a as i32, mysql_port: b as i32, ..Profile::default() });
    let (e, _) = rekam();
    assert!(port::is_free(a));
    let p = e.cari_port_bebas(a).unwrap();
    assert_ne!(p, a, "port web profil ditawarkan");
    assert!(p > a);
    // Port yang dipakai proses lain dilewati.
    let l = TcpListener::bind("127.0.0.1:0").unwrap();
    let dipakai = l.local_addr().unwrap().port();
    assert_ne!(e.cari_port_bebas(dipakai), Some(dipakai));
    assert!(e.port_tawaran(r"D:\tidak\pernah\jalan").is_none());
    let _ = fs::remove_dir_all(&d);
}

// ------------------------------------------------------------ layanan

#[test]
fn tanpa_web_bukan_gagal() {
    let (_g, d) = sandbox("eng-tanpaweb");
    setelan(&d, "kelola_hosts=0\r\n");
    simpan_profil(Profile { name: "Hanya MySQL".into(), mysql_id: "mysql-hilang".into(), ..Profile::default() });
    let (e, _) = rekam();
    assert_eq!(e.nyalakan_web(), Ok(false));
    assert_eq!(e.layanan().web_state(), ServiceState::Berhenti, "pilihan, bukan kegagalan");
    assert_eq!(hitung_log(&e, "Profil \"Hanya MySQL\" tidak memakai web server"), 1);
    assert!(e.layanan().status().galat_web.is_none());

    let mut p = e.active().unwrap();
    p.mysql_id.clear();
    simpan_profil(p);
    e.muat_ulang();
    assert_eq!(e.nyalakan_db(), Ok(false));
    assert_eq!(e.layanan().db_state(), ServiceState::Berhenti);
    assert_eq!(hitung_log(&e, "tidak memakai MySQL, jadi tidak ada yang dinyalakan"), 1);
    let _ = fs::remove_dir_all(&d);
}

#[test]
fn ganti_profil_tanpa_layanan() {
    let (_g, d) = sandbox("eng-ganti");
    setelan(&d, "kelola_hosts=0\r\n");
    let a = simpan_profil(Profile { name: "A".into(), ..Profile::default() });
    let b = simpan_profil(Profile { name: "B".into(), http_port: 8081, ..Profile::default() });
    let (e, r) = rekam();
    assert_eq!(e.active().unwrap().file_name, a.file_name);
    kunci(&r).clear();
    e.ganti_profil(&b.file_name, true).unwrap();
    assert_eq!(e.active().unwrap().file_name, b.file_name);
    assert_eq!(e.keadaan().profil_aktif, b.file_name);
    assert_eq!(Settings::load().active_profile, b.file_name);
    assert!(kunci(&r).iter().any(|k| matches!(k, Kabar::PerluMuatUlang)));
    assert_eq!(hitung_log(&e, "Mematikan layanan sebelum ganti profil"), 0, "tidak ada yang hidup");
    assert_eq!(hitung_log(&e, "Konfigurasi profil \"B\" ditulis ulang."), 1);
    // Profil aktif bertahan sesudah muat ulang.
    e.muat_ulang();
    assert_eq!(e.active().unwrap().file_name, b.file_name);
    assert!(e.ganti_profil("tidak-ada", false).is_err());
    let _ = fs::remove_dir_all(&d);
}

/// phoron.ini dibaca hanya saat Engine dibuat: muat ulang tidak menimpa
/// setelan di memori dengan isi cakram (berkas cacat, suntingan luar), dan
/// perubahan ganti profil serta situs terakhir bertahan.
#[test]
fn muat_ulang_tidak_membaca_phoron_ini() {
    let (_g, d) = sandbox("eng-muatini");
    setelan(&d, "kelola_hosts=0\r\ntema=terang\r\n");
    let a = simpan_profil(Profile { name: "A".into(), ..Profile::default() });
    let b = simpan_profil(Profile { name: "B".into(), ..Profile::default() });
    let (e, _) = rekam();
    e.ganti_profil(&b.file_name, false).unwrap();
    e.catat_situs_dibuka(r"D:\www\toko");
    // Berkas di cakram diubah dari luar, lalu dirusak.
    fs::write(d.join("phoron.ini"), format!("[umum]\r\nprofil_aktif={}\r\ntema=gelap\r\n", a.file_name)).unwrap();
    e.muat_ulang();
    assert_eq!(e.settings().tema, "terang", "muat ulang membaca phoron.ini");
    assert_eq!(e.active().unwrap().file_name, b.file_name);
    fs::write(d.join("phoron.ini"), [0xff_u8, 0xfe, 0x00, 0x5b]).unwrap();
    e.muat_ulang();
    let s = e.settings();
    assert_eq!((s.active_profile.as_str(), s.situs_terakhir.len()), (b.file_name.as_str(), 1));
    assert_eq!(e.active().unwrap().file_name, b.file_name);
    let _ = fs::remove_dir_all(&d);
}

/// Simpan/ubah setelan tidak mengganti profil aktif; ubah_setelan yang
/// menyebutnya ditolak. cek_terakhir tetap boleh; log rinci sampai ke layanan.
#[test]
fn setelan_tidak_mengganti_profil_aktif() {
    let (_g, d) = sandbox("eng-setelanaktif");
    setelan(&d, "kelola_hosts=0\r\n");
    let a = simpan_profil(Profile { name: "A".into(), ..Profile::default() });
    let b = simpan_profil(Profile { name: "B".into(), ..Profile::default() });
    let (e, _) = rekam();
    assert_eq!(e.active().unwrap().file_name, a.file_name);
    let mut s = e.settings();
    s.active_profile = b.file_name.clone();
    e.simpan_setelan(s).unwrap();
    assert_eq!(e.active().unwrap().file_name, a.file_name, "simpan_setelan memindah profil aktif");
    assert_eq!(Settings::load().active_profile, "");

    let ubah = |j: serde_json::Value| e.ubah_setelan(j.as_object().unwrap().clone());
    assert_eq!(
        ubah(serde_json::json!({ "active_profile": b.file_name })),
        Err(Pesan::baru("Profil aktif diganti lewat Ganti profil, bukan lewat setelan."))
    );
    assert!(ubah(serde_json::json!({ "situs_terakhir": ["x"] })).is_err());
    assert_eq!(e.active().unwrap().file_name, a.file_name);
    assert!(!e.layanan().log_rinci());
    let saat = e.settings().cek_terakhir;
    ubah(serde_json::json!({ "log_rinci": true, "cek_terakhir": { "detik": 1_800_000_000_i64, "tick": 0 } })).unwrap();
    assert!(e.layanan().log_rinci(), "log rinci tidak diteruskan ke layanan");
    assert_ne!(e.settings().cek_terakhir, saat, "cek_terakhir boleh diubah UI");
    assert!(ubah(serde_json::json!({ "nama_ngawur": 1 })).is_err());
    let _ = fs::remove_dir_all(&d);
}

/// Dua ganti profil berpapasan (dobel klik, Beranda dan Profil bersamaan):
/// hasilnya satu profil yang utuh - memori, phoron.ini, dan Keadaan sepakat.
#[test]
fn ganti_profil_berpapasan() {
    let (_g, d) = sandbox("eng-papasan");
    setelan(&d, "kelola_hosts=0\r\n");
    let a = simpan_profil(Profile { name: "A".into(), ..Profile::default() });
    let b = simpan_profil(Profile { name: "B".into(), ..Profile::default() });
    let (e, _) = rekam();
    let mut utas = Vec::new();
    for i in 0..6 {
        let (e2, f) = (e.clone(), if i % 2 == 0 { a.file_name.clone() } else { b.file_name.clone() });
        utas.push(std::thread::spawn(move || {
            e2.ganti_profil(&f, true).unwrap();
            e2.muat_ulang();
            e2.catat_situs_dibuka(&format!(r"D:\www\s{i}"));
        }));
    }
    for t in utas {
        t.join().unwrap();
    }
    let aktif = e.active().unwrap().file_name;
    assert_eq!(e.settings().active_profile, aktif);
    assert_eq!(Settings::load().active_profile, aktif);
    assert_eq!(e.keadaan().profil_aktif, aktif);
    assert_eq!(Settings::load().situs_terakhir.len(), settings::BATAS_SITUS_TERAKHIR, "situs terakhir hilang");
    let _ = fs::remove_dir_all(&d);
}

/// Profil aktif tidak berganti di tengah start: ganti profil menunggu start
/// (pemegang kunci profil) selesai.
#[test]
fn ganti_profil_menunggu_start() {
    let (_g, d) = sandbox("eng-tunggustart");
    setelan(&d, "kelola_hosts=0\r\n");
    let a = simpan_profil(Profile { name: "A".into(), ..Profile::default() });
    let b = simpan_profil(Profile { name: "B".into(), ..Profile::default() });
    let (e, _) = rekam();
    let start = e.profil_baca(); // seolah nyalakan_web sedang berjalan
    let e2 = e.clone();
    let f = b.file_name.clone();
    let t = std::thread::spawn(move || e2.ganti_profil(&f, false));
    std::thread::sleep(Duration::from_millis(300));
    assert_eq!(e.active().unwrap().file_name, a.file_name, "profil aktif berganti di tengah start");
    assert!(!t.is_finished());
    drop(start);
    t.join().unwrap().unwrap();
    assert_eq!(e.active().unwrap().file_name, b.file_name);
    let _ = fs::remove_dir_all(&d);
}

/// Sesudah tutup: start dan ganti profil baru ditolak dengan pesan.
#[test]
fn tutup_menolak_start_baru() {
    let (_g, d) = sandbox("eng-tutup");
    setelan(&d, "kelola_hosts=0\r\n");
    let a = simpan_profil(Profile { name: "A".into(), ..Profile::default() });
    let (e, _) = rekam();
    let mulai = Instant::now();
    e.tutup();
    assert!(mulai.elapsed() < Duration::from_secs(10), "tutup menunggu padahal tidak ada yang berjalan");
    let ditutup = Err(Pesan::baru("Phoron sedang ditutup."));
    assert_eq!(e.nyalakan_web(), ditutup);
    assert_eq!(e.nyalakan_db(), ditutup);
    assert_eq!(e.nyalakan_semua(), Err(Pesan::baru("Phoron sedang ditutup.")));
    assert_eq!(e.ganti_profil(&a.file_name, true), Err(Pesan::baru("Phoron sedang ditutup.")));
    assert_eq!(
        tangani(&e, Perintah::NyalakanWeb),
        IsiJawaban::Galat { galat: Galat::Gagal { pesan: Pesan::baru("Phoron sedang ditutup.") } }
    );
    let _ = fs::remove_dir_all(&d);
}

// ------------------------------------------------------------ protokol

#[test]
fn tangani_semua_perintah() {
    let (_g, d) = sandbox("eng-tangani");
    setelan(&d, "");
    let (e, _) = rekam();
    let galat = |j: &IsiJawaban| match j {
        IsiJawaban::Galat { galat } => Some(galat.clone()),
        IsiJawaban::Ok { .. } => None,
    };
    let semua = vec![
        Perintah::Keadaan,
        Perintah::MuatUlang,
        Perintah::SimpanSetelan { setelan: e.settings() },
        Perintah::Terapkan,
        Perintah::GantiProfil { file_name: "tidak-ada".into(), restart: false },
        Perintah::NyalakanSemua,
        Perintah::MatikanSemua,
        Perintah::NyalakanWeb,
        Perintah::MatikanWeb,
        Perintah::NyalakanDb,
        Perintah::MatikanDb,
        Perintah::JalankanNode { path: r"D:\tidak\terdaftar".into(), port_sekali: 0 },
        Perintah::HentikanNode { path: r"D:\tidak\terdaftar".into() },
        Perintah::HentikanSemuaNode,
        Perintah::RiwayatNode { path: r"D:\tidak\terdaftar".into() },
        Perintah::RiwayatLog,
        Perintah::SisaProses,
        Perintah::HentikanSisa,
        Perintah::CatatSitusDibuka { path: r"D:\www\toko".into() },
        Perintah::Catat { pesan: Pesan::dengan("Dari UI {0}", &["x"]) },
    ];
    for p in semua {
        let j = tangani(&e, p.clone());
        assert!(!matches!(galat(&j), Some(Galat::BelumAda { .. })), "{p:?} belum dipetakan");
    }
    assert!(matches!(
        galat(&tangani(&e, Perintah::Halo { versi_ui: "1".into(), protokol: 2 })),
        Some(Galat::BelumAda { .. })
    ));
    assert!(matches!(galat(&tangani(&e, Perintah::Keluar)), Some(Galat::BelumAda { .. })));

    // Bentuk hasil.
    match tangani(&e, Perintah::Keadaan) {
        IsiJawaban::Ok { hasil, .. } => {
            let k: Keadaan = serde_json::from_value(hasil).unwrap();
            assert_eq!(k.status, StatusLayanan {
                web: ServiceState::Berhenti,
                db: ServiceState::Berhenti,
                galat_web: None,
                galat_db: None
            });
            assert_eq!(k.profil_aktif, "");
        }
        j => panic!("{j:?}"),
    }
    match tangani(&e, Perintah::Terapkan) {
        IsiJawaban::Ok { peringatan, .. } => assert_eq!(peringatan, [Pesan::baru("Belum ada profil.")]),
        j => panic!("{j:?}"),
    }
    assert_eq!(
        galat(&tangani(&e, Perintah::JalankanNode { path: "x".into(), port_sekali: 0 })),
        Some(Galat::Gagal { pesan: Pesan::baru("Proyek belum dipilih.") })
    );
    match tangani(&e, Perintah::RiwayatLog) {
        IsiJawaban::Ok { hasil, .. } => {
            let r: Vec<BarisLogJarak> = serde_json::from_value(hasil).unwrap();
            assert!(r.iter().any(|b| b.pesan == Pesan::dengan("Dari UI {0}", &["x"])));
        }
        j => panic!("{j:?}"),
    }
    assert_eq!(e.settings().situs_terakhir, [r"D:\www\toko"]);
    let _ = fs::remove_dir_all(&d);
}

#[test]
fn node_terdaftar_dijalankan_lewat_perintah() {
    let (_g, d) = sandbox("eng-node");
    setelan(&d, "");
    let proyek = d.join("api");
    fs::create_dir_all(&proyek).unwrap();
    // Proyek yang bukan proyek Node: galat dari runner sampai ke jawaban.
    node_apps::save_all(&[NodeApp { path: proyek.to_string_lossy().into_owned(), ..NodeApp::default() }]).unwrap();
    let (e, _) = rekam();
    let j = tangani(&e, Perintah::JalankanNode { path: proyek.to_string_lossy().to_uppercase(), port_sekali: 0 });
    match j {
        IsiJawaban::Galat { galat: Galat::Gagal { pesan } } => assert_eq!(pesan.kunci, "Tidak ada package.json di {0}."),
        j => panic!("{j:?}"),
    }
    // apps.ini yang baru ditulis UI tanpa muat ulang tetap ditemukan.
    let lain = d.join("web");
    fs::create_dir_all(&lain).unwrap();
    node_apps::save_all(&[NodeApp { path: lain.to_string_lossy().into_owned(), ..NodeApp::default() }]).unwrap();
    assert!(matches!(
        e.jalankan_node(&lain.to_string_lossy(), 0),
        Err(p) if p.kunci == "Tidak ada package.json di {0}."
    ));
    let _ = fs::remove_dir_all(&d);
}

// ------------------------------------------------------------ nyata

const LARAGON_BIN: &str = r"C:\laragon\bin";
const PHP81: &str = r"C:\laragon\bin\php\php-8.1.10-Win32-vs16-x64";
const PHP74: &str = r"C:\laragon\bin\php\php-7.4.22-Win32-VC15-x64";

fn laragon_ada() -> bool {
    Path::new(PHP81).is_dir() && Path::new(PHP74).is_dir()
}

#[test]
fn nyata_jalan_pertama_membuat_profil() {
    if !laragon_ada() {
        eprintln!("dilewati: paket Laragon tidak ada");
        return;
    }
    let (_g, d) = sandbox("eng-seed");
    fs::write(d.join("phoron.ini"), format!("[umum]\r\nbin_roots={LARAGON_BIN}\r\n")).unwrap();
    let (e, _) = rekam();
    let php = e.packages().iter().filter(|p| p.kind == BinKind::Php).count();
    let pr = e.profiles();
    assert_eq!(pr.len(), php, "satu profil per versi PHP");
    assert_eq!(hitung_log(&e, "Belum ada profil - membuatkan satu profil per versi PHP yang ditemukan."), 1);
    for p in &pr {
        assert!(paths::profiles().join(format!("{}.ini", p.file_name)).is_file());
        assert!(!p.php_extensions.is_empty(), "profil {} tanpa ekstensi", p.name);
        assert!(!p.mysql_id.is_empty());
    }
    // Tersimpan berurutan dari PHP terbaru; Apache sepadan toolset-nya.
    assert_eq!(pr[0].name, "PHP 8.1.10 + Apache 2.4.54");
    assert_eq!(pr[0].file_name, "php-8-1-10-apache-2-4-54");
    // Profil aktif = PHP terbaru walau berkasnya bukan yang pertama menurut abjad.
    let a = e.active().unwrap();
    assert_eq!(a.php_id, "php-8.1.10-Win32-vs16-x64");
    assert_eq!(e.php().unwrap().version, "8.1.10");
    // Jalan berikutnya tidak membuat lagi.
    let (e2, _) = rekam();
    assert_eq!(e2.profiles().len(), pr.len());
    assert_eq!(hitung_log(&e2, "Belum ada profil"), 0);
    let _ = fs::remove_dir_all(&d);
}

/// Folder paket Laragon yang dipakai uji nyata - hanya boleh dibaca.
const FOLDER_PAKET: &[&str] =
    &[r"C:\laragon\bin\apache", r"C:\laragon\bin\php", r"C:\laragon\bin\mysql", r"C:\laragon\bin\nginx"];

/// Siapkan uji nyata: config utas ini mendengarkan hanya di 127.0.0.1 (tanpa
/// dialog Windows Firewall) dan folder paket dipotret untuk diperiksa.
fn mulai_nyata() -> crate::uji_paket::JagaPaket {
    config::uji_dengar_lokal(true);
    crate::uji_paket::JagaPaket::baru(&crate::uji_paket::yang_ada(FOLDER_PAKET))
}

/// Engine dari Laragon dengan port acak dan folder proyek di sandbox untuk
/// semua profil hasil seed. Hasil: Engine, rekaman kabar, profil PHP 8.1 dan 7.4.
fn engine_laragon(d: &Path) -> (Engine, Arc<Mutex<Vec<Kabar>>>, Profile, Profile) {
    fs::write(d.join("phoron.ini"), format!("[umum]\r\nbin_roots={LARAGON_BIN}\r\nkelola_hosts=0\r\n")).unwrap();
    let www = d.join("www");
    fs::create_dir_all(&www).unwrap();
    fs::write(www.join("uji.php"), "<?php echo 'phoron-engine ' . PHP_VERSION . ' ' . php_sapi_name();").unwrap();
    let (e, r) = rekam();
    for mut p in e.profiles() {
        p.http_port = port_bebas() as i32;
        p.https_port = port_bebas() as i32;
        p.mysql_port = port_bebas() as i32;
        p.project_roots = vec![www.to_string_lossy().into_owned()];
        profile_store::save(&mut p).unwrap();
    }
    e.muat_ulang();
    let p81 = e.active().unwrap();
    let p74 = e.profiles().into_iter().find(|p| p.php_id.starts_with("php-7.4.22")).unwrap();
    (e, r, p81, p74)
}

/// Tunggu sampai init MySQL (folder data kosong) benar-benar berjalan.
fn tunggu_init(e: &Engine) {
    assert!(
        tunggu(|| hitung_log(e, "belum ada - menyiapkan basis data awal") > 0, 30_000),
        "init MySQL tidak pernah mulai: {:#?}",
        log(e)
    );
    std::thread::sleep(Duration::from_millis(1500));
}

/// Ganti profil selagi MySQL MASIH menyala (init folder data kosong): init
/// profil lama dibatalkan, dan MySQL akhirnya Jalan dengan profil baru. Dulu
/// stop tidak bisa memotong init dan nyalakan profil baru menerima `false`
/// milik start yang dibatalkan - ganti profil Ok, MySQL mati.
#[test]
fn nyata_ganti_profil_saat_mysql_init() {
    if !laragon_ada() || !Path::new(r"C:\laragon\bin\mysql\mysql-8.0.30-winx64").is_dir() {
        return;
    }
    let (_g, d) = sandbox("eng-gantiinit");
    let jaga = mulai_nyata();
    let awal: Vec<u32> = proses_di(LARAGON_BIN).into_iter().map(|p| p.0).collect();
    let (e, _r, _p81, p74) = engine_laragon(&d);
    let e2 = e.clone();
    let t = std::thread::spawn(move || e2.nyalakan_db());
    tunggu_init(&e);
    assert_eq!(e.layanan().db_state(), ServiceState::Menyalakan);
    e.ganti_profil(&p74.file_name, true).unwrap();
    assert_eq!(t.join().unwrap(), Ok(false), "start profil lama tidak dibatalkan");
    assert_eq!(e.active().unwrap().file_name, p74.file_name);
    assert_eq!(e.layanan().db_state(), ServiceState::Jalan, "{}", log(&e).join("\n"));
    assert!(!port::is_free(p74.mysql_port as u16));
    assert_eq!(hitung_log(&e, "Penyiapan folder data dibatalkan."), 1, "{}", log(&e).join("\n"));
    e.tutup();
    assert!(tunggu(|| port::is_free(p74.mysql_port as u16), 10_000));
    assert!(hitung_log(&e, "MySQL dimatikan dengan rapi.") >= 1, "{}", log(&e).join("\n"));
    pastikan_tanpa_yatim(&awal);
    config::uji_dengar_lokal(false);
    jaga.pastikan_utuh();
    let _ = fs::remove_dir_all(&d);
}

/// Daemon keluar di tengah init MySQL: tutup membatalkan init (proses init
/// ikut mati, tidak yatim), menunggu start itu berbenah, dan folder setengah
/// jadinya tidak dianggap siap pada start berikutnya.
#[test]
fn nyata_tutup_di_tengah_init() {
    if !laragon_ada() || !Path::new(r"C:\laragon\bin\mysql\mysql-8.0.30-winx64").is_dir() {
        return;
    }
    let (_g, d) = sandbox("eng-tutupinit");
    let jaga = mulai_nyata();
    let awal: Vec<u32> = proses_di(LARAGON_BIN).into_iter().map(|p| p.0).collect();
    let (e, _r, p81, _p74) = engine_laragon(&d);
    let mysql = e.mysql().unwrap();
    let data_dir = config::mysql_data_dir(&mysql);
    let e2 = e.clone();
    let t = std::thread::spawn(move || e2.nyalakan_db());
    tunggu_init(&e);
    let mulai = Instant::now();
    e.tutup();
    assert!(mulai.elapsed() < BATAS_TUTUP, "tutup menunggu sampai batas");
    // Start sudah selesai berbenah saat tutup kembali.
    assert!(tunggu(|| t.is_finished(), 2000), "tutup kembali sebelum start selesai berbenah");
    assert_eq!(t.join().unwrap(), Ok(false));
    assert_eq!(e.layanan().db_state(), ServiceState::Berhenti);
    assert!(port::is_free(p81.mysql_port as u16));
    assert!(proses_di(&mysql.path).is_empty(), "init MySQL tertinggal sesudah tutup");
    assert!(
        !Path::new(&data_dir).join("mysql").is_dir() || services::penanda_init(&data_dir).exists(),
        "folder data setengah jadi akan dianggap siap"
    );
    pastikan_tanpa_yatim(&awal);
    config::uji_dengar_lokal(false);
    jaga.pastikan_utuh();
    let _ = fs::remove_dir_all(&d);
}

fn pastikan_tanpa_yatim(awal: &[u32]) {
    let yatim = || -> Vec<(u32, String)> {
        proses_di(LARAGON_BIN).into_iter().filter(|p| !awal.contains(&p.0)).collect()
    };
    let bersih = tunggu(|| yatim().is_empty(), 10_000);
    let sisa = yatim();
    for p in &sisa {
        crate::shell::kill_tree(p.0);
    }
    assert!(bersih, "proses yatim tertinggal: {sisa:?}");
}

fn ambil(port: u16, jalan: &str) -> String {
    let Ok(mut s) = TcpStream::connect(("127.0.0.1", port)) else { return String::new() };
    let _ = s.set_read_timeout(Some(Duration::from_secs(15)));
    let _ = write!(s, "GET {jalan} HTTP/1.0\r\nHost: localhost\r\n\r\n");
    let mut b = Vec::new();
    let _ = s.read_to_end(&mut b);
    String::from_utf8_lossy(&b).into_owned()
}

/// PID proses yang exe-nya di bawah folder ini.
fn proses_di(folder: &str) -> Vec<(u32, String)> {
    use windows_sys::Win32::Foundation::{CloseHandle, INVALID_HANDLE_VALUE};
    use windows_sys::Win32::System::Diagnostics::ToolHelp::{
        CreateToolhelp32Snapshot, Process32FirstW, Process32NextW, PROCESSENTRY32W, TH32CS_SNAPPROCESS,
    };
    use windows_sys::Win32::System::Threading::{
        OpenProcess, QueryFullProcessImageNameW, PROCESS_QUERY_LIMITED_INFORMATION,
    };
    let awalan = format!("{}\\", folder.to_lowercase());
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
                    if j.starts_with(&awalan) {
                        hasil.push((pe.th32ProcessID, j));
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

/// Muat dari C:\laragon\bin, Terapkan, nyalakan semua di port acak, HTTP ke
/// situs, ganti profil saat hidup (PHP 8.1 -> 7.4), matikan semua; tidak ada
/// proses yatim.
#[test]
fn nyata_ujung_ke_ujung() {
    if !laragon_ada() {
        eprintln!("dilewati: paket Laragon tidak ada");
        return;
    }
    let (_g, d) = sandbox("eng-nyata");
    let awal: Vec<u32> = proses_di(LARAGON_BIN).into_iter().map(|p| p.0).collect();
    fs::write(d.join("phoron.ini"), format!("[umum]\r\nbin_roots={LARAGON_BIN}\r\n")).unwrap();
    let www = d.join("www");
    fs::create_dir_all(&www).unwrap();
    fs::write(www.join("uji.php"), "<?php echo 'phoron-engine ' . PHP_VERSION . ' ' . php_sapi_name();").unwrap();

    let jaga = mulai_nyata();
    let (e, r) = rekam();
    // Port acak untuk semua profil hasil seed; folder proyek di sandbox.
    for mut p in e.profiles() {
        p.http_port = port_bebas() as i32;
        p.https_port = port_bebas() as i32;
        p.mysql_port = port_bebas() as i32;
        p.project_roots = vec![www.to_string_lossy().into_owned()];
        profile_store::save(&mut p).unwrap();
    }
    e.muat_ulang();
    let p81 = e.active().unwrap();
    assert!(p81.php_id.starts_with("php-8.1.10"));
    let p74 = e.profiles().into_iter().find(|p| p.php_id.starts_with("php-7.4.22")).unwrap();

    let w = e.terapkan().unwrap();
    assert!(!w.iter().any(|p| p.kunci.contains("tidak ada di komputer ini")), "{w:#?}");
    assert!(e.layanan().web_state() == ServiceState::Berhenti);

    e.nyalakan_semua().unwrap();
    let log_teks = || log(&e).join("\n");
    assert_eq!(e.layanan().db_state(), ServiceState::Jalan, "{}", log_teks());
    assert_eq!(e.layanan().web_state(), ServiceState::Jalan, "{}", log_teks());
    let h = ambil(p81.http_port as u16, "/uji.php");
    assert!(h.contains("phoron-engine 8.1.10 apache2handler"), "halaman: {h}");
    assert!(!e.perlu_restart_web() && !e.perlu_restart_db());
    let k = e.keadaan();
    assert_eq!(k.profil_aktif, p81.file_name);
    assert!(e.sisa_proses().is_empty(), "port milik kita sendiri bukan sisa");

    // Ganti profil saat hidup: dimatikan, ditulis ulang, dinyalakan lagi.
    e.ganti_profil(&p74.file_name, true).unwrap();
    assert_eq!(e.active().unwrap().file_name, p74.file_name);
    assert_eq!(e.layanan().web_state(), ServiceState::Jalan, "{}", log_teks());
    assert_eq!(e.layanan().db_state(), ServiceState::Jalan, "{}", log_teks());
    let h = ambil(p74.http_port as u16, "/uji.php");
    assert!(h.contains("phoron-engine 7.4.22"), "halaman: {h}");
    assert!(tunggu(|| port::is_free(p81.http_port as u16), 5000), "Apache profil lama masih hidup");
    assert!(port::is_free(p81.mysql_port as u16), "MySQL profil lama masih hidup");
    assert!(!port::is_free(p74.mysql_port as u16));
    assert_eq!(hitung_log(&e, "Mematikan layanan sebelum ganti profil..."), 1);
    assert!(hitung_log(&e, "MySQL dimatikan") >= 1, "MySQL tidak dimatikan dengan rapi: {}", log_teks());

    e.matikan_semua();
    assert_eq!(e.layanan().web_state(), ServiceState::Berhenti);
    assert_eq!(e.layanan().db_state(), ServiceState::Berhenti);
    for p in [p74.http_port, p74.https_port, p74.mysql_port] {
        assert!(tunggu(|| port::is_free(p as u16), 10_000), "port {p} masih terpakai");
    }
    // Kabar status sampai ke pendengar.
    let k = kunci(&r);
    assert!(k.iter().any(|x| matches!(x, Kabar::Status(s) if s.web == ServiceState::Jalan)));
    assert!(matches!(k.iter().rev().find(|x| matches!(x, Kabar::Status(_))), Some(Kabar::Status(s))
        if s.web == ServiceState::Berhenti && s.db == ServiceState::Berhenti));
    drop(k);

    // Tidak ada proses yatim dari folder paket, dan tidak ada yang ditulisi.
    pastikan_tanpa_yatim(&awal);
    config::uji_dengar_lokal(false);
    jaga.pastikan_utuh();
    let _ = fs::remove_dir_all(&d);
}
