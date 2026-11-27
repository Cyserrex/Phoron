//! Uji butir daftar periksa config.rs.

use super::*;
use crate::model::BinKind;
use std::fs;

fn isi(p: &Path) -> String {
    fs::read_to_string(p).unwrap_or_else(|e| panic!("{}: {e}", p.display()))
}

fn berkas(p: &Path, t: &str) {
    fs::create_dir_all(p.parent().unwrap()).unwrap();
    fs::write(p, t).unwrap();
}

/// PHP palsu: cukup berkas yang diperiksa config (tidak ada yang dijalankan).
fn php_palsu(akar: &Path, nama: &str, versi: &str, ts: bool, ext: &[&str], ini_dev: &str) -> BinPackage {
    let dir = akar.join(nama);
    for e in ext {
        berkas(&dir.join(format!(r"ext\php_{e}.dll")), "");
    }
    berkas(&dir.join("php-cgi.exe"), "");
    if !ini_dev.is_empty() {
        berkas(&dir.join("php.ini-development"), ini_dev);
    }
    let mut p = BinPackage::baru(BinKind::Php, nama, &jalur(&dir), versi);
    p.arch = "x64".into();
    if ts {
        p.apache_module_dll = jalur(&dir.join("php8apache2_4.dll"));
        berkas(Path::new(&p.apache_module_dll), "");
    }
    p
}

fn apache_palsu(akar: &Path, versi: &str, conf: &str) -> BinPackage {
    let dir = akar.join(format!("httpd-{versi}"));
    berkas(&dir.join(r"conf\httpd.conf"), conf);
    for m in ["rewrite", "headers", "ssl", "proxy", "proxy_fcgi"] {
        berkas(&dir.join(format!(r"modules\mod_{m}.so")), "");
    }
    BinPackage::baru(BinKind::Apache, &format!("httpd-{versi}"), &jalur(&dir), versi)
}

const CONF: &str = "Define SRVROOT \"C:/lama\"\nServerRoot \"C:/lama\"\nListen 80\nListen 8080\n\
Include \"C:/laragon/etc/apache2/x.conf\"\nIncludeOptional conf/extra/*.conf\n\x20\x20\
  DocumentRoot \"${SRVROOT}/htdocs\"\nLoadModule dir_module modules/mod_dir.so\n";

fn profil(d: &Path) -> Profile {
    Profile {
        name: "Uji".into(),
        apache_id: "httpd".into(),
        project_roots: vec![jalur(&d.join("www"))],
        ..Profile::default()
    }
}

#[test]
fn teks_pesan_disisipi_sekali() {
    let p = Pesan::dengan("a {0} b {1} {2}", &["{1}", "x"]);
    assert_eq!(teks(&p), "a {1} b x {2}");
}

#[test]
fn httpd_conf_dari_bawaan_vendor() {
    let (_g, d) = paths::sandbox_uji("cfg-httpd");
    fs::create_dir_all(d.join("www")).unwrap();
    let ap = apache_palsu(&d.join("bin"), "2.4.54", CONF);
    let php = php_palsu(&d.join("bin"), "php-8.3.12", "8.3.12", true, &["mbstring"], "");
    let r = build(&profil(&d), Some(&php), Some(&ap), None, None, &[], &Opsi::default(), Vec::new()).unwrap();
    let h = isi(r.httpd_conf.as_ref().unwrap());
    let srv = fwd(&ap.path);
    assert!(h.contains(&format!("Define SRVROOT \"{srv}\"\r\nServerRoot \"{srv}\"\r\nListen 80\r\n")));
    assert_eq!(h.matches("\nListen ").count(), 1, "Listen hanya sekali");
    assert!(!h.contains("C:/laragon"), "Include mutlak dibuang");
    assert!(h.contains("IncludeOptional conf/extra/*.conf"), "Include relatif dipertahankan");
    assert!(h.contains("#   DocumentRoot \"${SRVROOT}/htdocs\"   # dinonaktifkan Phoron"));
    // Hanya modul yang ada berkasnya; PHP TS tidak butuh proxy.
    assert!(h.contains("LoadModule rewrite_module modules/mod_rewrite.so"));
    assert!(!h.contains("deflate_module") && !h.contains("proxy_module"));
    assert!(h.contains("Alias /phoron \""));
    assert!(!h.contains("RewriteRule"), "pengalihan beranda tidak di konteks server");
    assert!(h.contains("CustomLog "));
    let mp = isi(&paths::etc_apache().join("mod_php.conf"));
    assert!(mp.contains("LoadModule php_module \""));
    assert!(mp.contains(&format!("PHPIniDir \"{}\"", fwd(r.php_ini_dir.as_ref().unwrap()))));
    assert!(!r.php_fastcgi);

    // Tanpa log akses; beranda tidak di akar.
    let o = Opsi { log_akses: false, beranda_di_akar: false, ..Opsi::default() };
    build(&profil(&d), Some(&php), Some(&ap), None, None, &[], &o, Vec::new()).unwrap();
    let h = isi(&paths::etc_apache().join("httpd.conf"));
    assert!(h.contains("# Log akses dimatikan") && !h.contains("CustomLog"));
    assert!(!isi(&paths::sites_enabled().join("000-default.conf")).contains("RewriteRule"));
    let _ = fs::remove_dir_all(&d);
}

#[test]
fn fastcgi_ditentukan_dll_dan_versi_apache() {
    let (_g, d) = paths::sandbox_uji("cfg-fcgi");
    // NTS di folder tanpa kata "nts": tetap FastCGI, dan modul proxy dimuat.
    let php = php_palsu(&d.join("bin"), "php-8.3", "8.3.0", false, &[], "");
    let ap = apache_palsu(&d.join("bin"), "2.4.54", CONF);
    let r = build(&profil(&d), Some(&php), Some(&ap), None, None, &[], &Opsi::default(), Vec::new()).unwrap();
    assert!(r.php_fastcgi && pakai_fastcgi(Some(&php)));
    let h = isi(&paths::etc_apache().join("httpd.conf"));
    assert!(h.contains("LoadModule proxy_fcgi_module"));
    let mp = isi(&paths::etc_apache().join("mod_php.conf"));
    assert!(mp.contains("SetHandler \"proxy:fcgi://127.0.0.1:9123/\""));
    assert!(mp.contains(r"m#^proxy:fcgi://127\.0\.0\.1:\d+/(.*)$#"));
    assert_eq!(mp.matches("ProxyFCGISetEnvIf").count(), 1);

    // Apache lama: tanpa ProxyFCGISetEnvIf, dengan peringatan.
    let lama = apache_palsu(&d.join("bin"), "2.4.25", CONF);
    fs::remove_file(gabung(&php.path, "php-cgi.exe")).unwrap();
    let r = build(&profil(&d), Some(&php), Some(&lama), None, None, &[], &Opsi::default(), Vec::new()).unwrap();
    let mp = isi(&paths::etc_apache().join("mod_php.conf"));
    assert!(!mp.contains("ProxyFCGISetEnvIf \""));
    let k: Vec<&str> = r.peringatan.iter().map(|p| p.kunci.as_str()).collect();
    assert!(k.iter().any(|x| x.starts_with("Apache {0} belum mengenal ProxyFCGISetEnvIf")));
    assert!(k.contains(&"php-cgi.exe tidak ada di {0}; PHP tidak akan jalan lewat Apache."));
    // Versi tak terbaca dianggap baru.
    assert!(proxy_fcgi_setenvif_ada(Some(&BinPackage::baru(BinKind::Apache, "x", "x", ""))));
    let _ = fs::remove_dir_all(&d);
}

#[test]
fn web_server_menurut_pilihan_profil() {
    let (_g, d) = paths::sandbox_uji("cfg-pilih");
    let ap = apache_palsu(&d.join("bin"), "2.4.54", CONF);
    let mut p = profil(&d);
    p.web_server = "nginx".into();
    p.nginx_id = "nginx-1".into();
    let r = build(&p, None, Some(&ap), None, None, &[], &Opsi::default(), Vec::new()).unwrap();
    assert!(r.httpd_conf.is_none() && r.nginx_conf.is_none());
    assert!(r.peringatan[0].kunci.starts_with("Versi Nginx yang dicatat profil"));

    // Tanpa web server: diam.
    let p = Profile { project_roots: profil(&d).project_roots, ..Profile::default() };
    let r = build(&p, None, Some(&ap), None, None, &[], &Opsi::default(), Vec::new()).unwrap();
    assert!(r.peringatan.is_empty() && r.httpd_conf.is_none());
    // Beranda tetap ditulis, web = apache bila ada.
    assert!(isi(&beranda_folder().join("index.php")).contains(&format!("'web'     => '{}',", ap.id)));
    let _ = fs::remove_dir_all(&d);
}

fn situs_uji(d: &Path, nama: &str) -> Site {
    let path = jalur(&d.join("www").join(nama));
    fs::create_dir_all(&path).unwrap();
    Site {
        folder: nama.into(),
        path: path.clone(),
        host_name: format!("{nama}.test"),
        root: jalur(&d.join("www")),
        doc_root: path,
        ..Site::default()
    }
}

#[test]
fn ssl_dan_vhost() {
    let (_g, d) = paths::sandbox_uji("cfg-ssl");
    let ap = apache_palsu(&d.join("bin"), "2.4.54", CONF);
    let s = [situs_uji(&d, "blog"), situs_uji(&d, "toko")];
    berkas(&paths::sites_enabled().join("auto.lama.test.conf"), "");
    berkas(&paths::sites_enabled().join("buatan-tangan.conf"), "");
    // Sertifikat + kunci tanpa CA: ssl.conf menyala, vhost belum.
    berkas(&ssl::crt_path(), "x");
    berkas(&ssl::key_path(), "x");
    let r = build(&profil(&d), None, Some(&ap), None, None, &s, &Opsi::default(), Vec::new()).unwrap();
    assert!(r.ssl_enabled);
    assert!(isi(&paths::etc_apache().join("ssl.conf")).contains("    Listen 443"));
    let v = isi(&paths::sites_enabled().join("auto.blog.test.conf"));
    assert!(v.contains("<VirtualHost *:80>") && !v.contains("SSLEngine"));
    assert!(!paths::sites_enabled().join("auto.lama.test.conf").exists(), "vhost basi dihapus");
    assert!(paths::sites_enabled().join("buatan-tangan.conf").exists());
    let def = isi(&paths::sites_enabled().join("000-default.conf"));
    assert!(def.contains("<VirtualHost _default_:80>") && def.contains("RewriteRule \"^/?$\" \"/phoron/index.php\" [PT,L]"));
    assert!(isi(&paths::etc_apache().join("mod_php.conf")).contains("# Profil ini tidak memakai PHP."));

    berkas(&ssl::ca_crt_path(), "x");
    build(&profil(&d), None, Some(&ap), None, None, &s, &Opsi::default(), Vec::new()).unwrap();
    let v = isi(&paths::sites_enabled().join("auto.toko.test.conf"));
    assert!(v.contains("<VirtualHost *:443>") && v.contains("SSLEngine on"));
    assert!(isi(&paths::sites_enabled().join("000-default.conf")).contains("_default_:443"));

    // Kunci hilang: HTTPS mati di keduanya.
    fs::remove_file(ssl::key_path()).unwrap();
    let r = build(&profil(&d), None, Some(&ap), None, None, &s, &Opsi::default(), Vec::new()).unwrap();
    assert!(!r.ssl_enabled);
    assert!(isi(&paths::etc_apache().join("ssl.conf")).contains("HTTPS dimatikan"));
    assert!(!isi(&paths::sites_enabled().join("auto.toko.test.conf")).contains("443"));
    let _ = fs::remove_dir_all(&d);
}

const INI_DASAR: &str = "[PHP]\r\nshort_open_tag = On\r\nmemory_limit = 128M\r\n;extension_dir = \"ext\"\r\n\
extension=mbstring\r\n;extension=curl\r\nzend_extension=php_xdebug.dll\r\n[Date]\r\n;date.timezone =\r\n\
[Session]\r\nsession.save_path = \"/tmp\"\r\n[Lain]\r\nmemory_limit = 64M\r\n";

#[test]
fn php_ini_dari_dasar_dengan_penimpaan() {
    let (_g, d) = paths::sandbox_uji("cfg-phpini");
    let php = php_palsu(&d.join("bin"), "php-8.3.12", "8.3.12", true, &["mbstring", "curl", "opcache", "xdebug"], "");
    berkas(Path::new(&gabung(&php.path, "php.ini")), INI_DASAR);
    let mut p = profil(&d);
    kamus_set(&mut p.php_ini_overrides, "Memory_Limit", "512M");
    kamus_set(&mut p.php_ini_overrides, "opcache.enable", "0");
    kamus_set(&mut p.php_ini_overrides, "upload_max_filesize", "64M");
    let mut r = Hasil::default();
    let dir = write_php_ini(&p, &php, &mut r, false, true).unwrap();
    assert_eq!(dir, jalur(&d.join(r"etc\php\php-8.3.12")));
    let t = isi(&Path::new(&dir).join("php.ini"));
    assert!(t.starts_with("; php.ini untuk profil \"Uji\" - dibuat otomatis oleh Phoron.\r\n"));
    assert!(t.contains("short_open_tag = On"), "php.ini yang dipakai jadi dasar");
    assert!(t.contains("\r\nmemory_limit = 512M\r\n"), "kunci asli dipertahankan, nilai profil");
    assert!(t.contains("\r\n;memory_limit = 64M\r\n"), "kemunculan kedua dimatikan");
    assert!(t.contains("\r\n;extension=mbstring\r\n") && t.contains("\r\n;zend_extension=php_xdebug.dll\r\n"));
    assert!(t.contains("\r\ndate.timezone = Asia/Makassar\r\n"));
    assert!(t.contains(&format!("extension_dir = \"{}/ext\"", fwd(&php.path))));
    // opcache.enable ditimpa profil; sisanya setelan Phoron.
    assert!(t.contains("opcache.enable = 0\r\n") && t.contains("opcache.revalidate_freq = 0\r\n"));
    assert!(t.contains("upload_max_filesize = 64M"));
    // Ekstensi diambil alih dari dasar (mbstring, xdebug) dengan direktif benar.
    assert_eq!(r.ekstensi_diambil, Some(vec!["mbstring".to_string(), "xdebug".to_string()]));
    assert!(t.contains("\r\nextension = mbstring\r\nzend_extension = xdebug\r\n"));
    assert!(t.contains("; --- opcache (setelan Phoron, bukan daftar ekstensi profil) ---\r\nzend_extension = opcache\r\n"));

    // Keluaran Phoron sendiri tidak jadi dasar: tulis ke folder PHP, lalu ulangi.
    let mut r = Hasil::default();
    berkas(&d.join(r"bin\php-8.3.12\php.ini-production"), "[PHP]\nprod = 1\n");
    write_php_ini(&p, &php, &mut r, true, false).unwrap();
    assert!(Path::new(&gabung(&php.path, "php.ini.sebelum-phoron")).is_file(), "asli dicadangkan");
    assert!(r.peringatan.is_empty(), "folder di bawah bin Phoron: tanpa peringatan");
    let pertama = isi(&Path::new(&php.path).join("php.ini"));
    write_php_ini(&p, &php, &mut Hasil::default(), true, false).unwrap();
    assert_eq!(isi(&Path::new(&php.path).join("php.ini")), pertama, "tidak menumpuk");
    assert!(!pertama.contains("opcache.revalidate_freq"), "opcache mati: tanpa setelan opcache");
    let _ = fs::remove_dir_all(&d);
}

#[test]
fn ekstensi_disaring_dan_bentuk_lama() {
    let (_g, d) = paths::sandbox_uji("cfg-ext");
    let php = php_palsu(&d.join("bin"), "php-5.6.40", "5.6.40", true, &["mbstring", "gd2", "oci8_12c"], "[PHP]\n");
    berkas(&d.join(r"bin\php-5.6.40\ext\php_mysqli.dllaaa"), "");
    assert_eq!(available_extensions(Some(&php)), vec!["gd2", "mbstring", "oci8_12c"]);
    assert_eq!(ekstensi_disarankan(Some(&php)), vec!["mbstring", "gd2"]);
    let mut p = profil(&d);
    p.php_extensions = vec!["mbstring".into(), "MBString".into(), "mysqli".into(), "oci8_12c".into()];
    let mut r = Hasil::default();
    let dir = write_php_ini(&p, &php, &mut r, false, true).unwrap();
    let t = isi(&Path::new(&dir).join("php.ini"));
    assert_eq!(t.matches("extension = php_mbstring.dll").count(), 1, "PHP < 7.2: nama DLL, sekali saja");
    assert!(!t.contains("mysqli"));
    assert!(r.ekstensi_diambil.is_none());
    let k: Vec<String> = r.peringatan.iter().map(teks).collect();
    assert!(k.iter().any(|x| x.contains("tidak ada DLL-nya di php-5.6.40") && x.contains(": mysqli.")));
    // Instant Client: tergantung mesin, tapi oci8 hanya ditulis bila layak.
    let layak = oracle::periksa(Some(&php), None).layak;
    assert_eq!(t.contains("php_oci8_12c.dll"), layak);
    // Profil asli tidak diubah.
    assert_eq!(p.php_extensions.len(), 4);
    let _ = fs::remove_dir_all(&d);
}

#[test]
fn ekstensi_aktif_dari_teks() {
    let t = "extension=php_MBSTRING.dll\n  zend_extension = \"C:\\php\\ext\\php_xdebug.dll\" ; x\n;extension=curl\n\
extension_dir=ext\nextension = \"gd\"\nextension=mbstring\nextension= ;kosong\n";
    assert_eq!(ekstensi_aktif(t), vec!["mbstring", r"c:\php\ext\php_xdebug", "gd"]);
    assert!(baris_ekstensi(";  Zend_Extension  =x") && !baris_ekstensi("extension_dir = x"));
    assert_eq!(kunci_ini("; session.save_path = x"), Some("session.save_path"));
    assert_eq!(kunci_ini("[PHP]"), None);
    assert!(include_mutlak("IncludeOptional \"d:/x\"") && include_mutlak("include  C:/x"));
    assert!(!include_mutlak("Include conf/x.conf") && !include_mutlak("IncludeC:/x"));
    let p = BinPackage::baru(BinKind::Php, "p", "p", "7.2.0");
    assert_eq!(extension_value(&p, "gd"), "gd");
}

#[test]
fn kolam_php_per_situs() {
    let (_g, d) = paths::sandbox_uji("cfg-kolam");
    let php = php_palsu(&d.join("bin"), "php-8.3.12", "8.3.12", true, &["mbstring"], "");
    let k56 = php_palsu(&d.join("bin"), "php-5.6.40", "5.6.40", false, &["mbstring", "gd2"], "extension=php_gd2.dll\n");
    let k74 = php_palsu(&d.join("bin"), "php-7.4.0", "7.4.0", false, &[], "");
    fs::remove_file(gabung(&k74.path, "php-cgi.exe")).unwrap();
    let ap = apache_palsu(&d.join("bin"), "2.4.54", CONF);
    let lama = situs_uji(&d, "lama");
    let ci = situs_uji(&d, "ci 2");
    let s = [lama.clone(), ci.clone(), situs_uji(&d, "baru")];
    let kolam = vec![
        Kolam::baru(k56.clone(), Vec::new(), vec![("memory_limit".into(), "1G".into())], vec![lama.clone(), ci.clone()]),
        Kolam::baru(k74.clone(), Vec::new(), Vec::new(), vec![situs_uji(&d, "baru")]),
        Kolam::baru(k56.clone(), Vec::new(), Vec::new(), Vec::new()),
    ];
    let mut p = profil(&d);
    p.php_extensions = vec!["mbstring".into()];
    let r = build(&p, Some(&php), Some(&ap), None, None, &s, &Opsi::default(), kolam.clone()).unwrap();
    assert_eq!(r.kolam.len(), 1);
    assert_eq!(r.kolam[0].port, 9124);
    assert_eq!(r.kolam[0].folder_ini, jalur(&d.join(r"etc\php\php-5.6.40")));
    assert!(r.ekstensi_diambil.is_none(), "ambil alih kolam tidak bocor ke profil");
    let kolam_ini = isi(&d.join(r"etc\php\php-5.6.40\php.ini"));
    assert!(kolam_ini.contains("profil \"Uji - PHP 5.6.40 per situs\""));
    assert!(kolam_ini.contains("extension = php_gd2.dll") && kolam_ini.contains("memory_limit = 1G"));
    assert!(r.peringatan.iter().any(|w| w.kunci == "php-cgi.exe tidak ada di {0}, jadi {1} memakai PHP profil."));
    let mp = isi(&paths::etc_apache().join("mod_php.conf"));
    assert!(mp.contains(&format!("<Directory \"{}\">\r\n    CGIPassAuth On", fwd(&ci.path))));
    assert!(mp.contains("SetHandler \"proxy:fcgi://127.0.0.1:9124/\""));
    assert_eq!(mp.matches("ProxyFCGISetEnvIf \"").count(), 1);
    assert!(isi(&paths::etc_apache().join("httpd.conf")).contains("proxy_fcgi_module"));
    let b = isi(&beranda_folder().join("index.php"));
    assert!(b.contains("'nama' => 'lama'") && b.contains("'php' => '5.6.40'") && b.contains("'php' => '8.3.12'"));

    // Apache lama: semua situs ikut PHP profil.
    let tua = apache_palsu(&d.join("bin"), "2.4.23", CONF);
    let r = build(&p, Some(&php), Some(&tua), None, None, &s, &Opsi::default(), kolam.clone()).unwrap();
    assert!(r.kolam.is_empty());
    let w = r.peringatan.iter().find(|w| w.kunci.starts_with("Versi PHP per situs butuh Apache 2.4.26")).unwrap();
    assert_eq!(w.args, vec!["2.4.23".to_string()]);

    // Nginx: server situs kolam ke port kolam, location berkutip di localhost.
    let ng = BinPackage::baru(BinKind::Nginx, "nginx-1.27", &jalur(&d.join(r"bin\nginx")), "1.27.0");
    p.web_server = "nginx".into();
    p.nginx_id = ng.id.clone();
    let r = build(&p, Some(&php), None, None, Some(&ng), &s, &Opsi::default(), kolam).unwrap();
    let n = isi(r.nginx_conf.as_ref().unwrap());
    assert!(n.contains("        location ^~ \"/ci 2/\" {   # PHP 5.6.40\r\n"));
    assert!(n.contains("server_name  lama.test;\r\n") && n.matches("fastcgi_pass   127.0.0.1:9124;").count() >= 3);
    let _ = fs::remove_dir_all(&d);
}

#[test]
fn nginx_params_dan_hash() {
    let (_g, d) = paths::sandbox_uji("cfg-nginx");
    let ng = BinPackage::baru(BinKind::Nginx, "nginx-1.27", &jalur(&d.join(r"bin\nginx")), "1.27.0");
    let mut p = profil(&d);
    p.web_server = "nginx".into();
    p.nginx_id = ng.id.clone();
    let panjang = Site { host_name: "bandarmasih-mobile-pm-service.test".into(), ..situs_uji(&d, "x") };
    let o = Opsi { log_akses: false, ..Opsi::default() };
    let r = build(&p, None, None, None, Some(&ng), &[panjang], &o, Vec::new()).unwrap();
    assert!(r.php_fastcgi);
    assert_eq!(r.peringatan[0].kunci, "Nginx melayani PHP lewat FastCGI; php-cgi.exe tidak ditemukan.");
    let n = isi(r.nginx_conf.as_ref().unwrap());
    let fp = paths::etc_nginx().join("fastcgi_params");
    assert!(fp.is_file(), "fastcgi_params minimal ditulis Phoron");
    assert!(n.contains(&format!("include        \"{}\";", fwd_p(&fp))));
    assert!(n.contains("server_names_hash_bucket_size 64;") && n.contains("access_log off;"));
    assert!(n.contains("location = / { rewrite ^ /phoron/index.php last; }"));
    assert_eq!(n.matches("location = /").count(), 1, "beranda di akar hanya server bawaan");

    // Paket membawa fastcgi_params sendiri.
    berkas(&d.join(r"bin\nginx\conf\fastcgi_params"), "x");
    build(&p, None, None, None, Some(&ng), &[], &Opsi::default(), Vec::new()).unwrap();
    assert!(isi(&paths::etc_nginx().join("nginx.conf")).contains("/bin/nginx/conf/fastcgi_params\";"));

    assert_eq!(ember_hash(&["a".repeat(57)]), 128);
    assert_eq!(ember_hash(&["a".repeat(56)]), 64);
    assert_eq!(ember_hash(&["a".repeat(5000)]), 1024);
    assert_eq!(maks_hash(65), 1024);
    assert_eq!(maks_hash(10_000), 16384);
    assert_eq!(kutip_nginx(r#"a\"b"#), r#"a\\\"b"#);
    let _ = fs::remove_dir_all(&d);
}

#[test]
fn folder_data_mysql_tidak_dipakai_bersama() {
    let (_g, d) = paths::sandbox_uji("cfg-mysql");
    let a_dir = d.join(r"x1\mysql");
    let b_dir = d.join(r"x2\mysql");
    fs::create_dir_all(&a_dir).unwrap();
    fs::create_dir_all(&b_dir).unwrap();
    let mut a = BinPackage::baru(BinKind::MySql, "mysql", &jalur(&a_dir), "10.1.36");
    a.source_root = r"C:\x1".into();
    a.kembar = true;
    let mut b = BinPackage::baru(BinKind::MySql, "mysql", &jalur(&b_dir), "10.1.38");
    b.source_root = r"C:\x2".into();
    b.kembar = true;
    b.kunci = "mysql@C-x2".into();

    let da = mysql_data_dir(&a);
    assert_eq!(da, jalur(&d.join(r"data\mysql")));
    fs::create_dir_all(&da).unwrap();
    catat_pemilik_data(&da, Some(&a));
    // Kembaran kedua tidak boleh ikut folder data yang pertama.
    assert_eq!(mysql_data_dir(&b), jalur(&d.join(r"data\mysql@C-x2")));
    // Ejaan lain jalur pemilik tetap dikenali.
    fs::write(Path::new(&da).join(BERKAS_PEMILIK), fwd(&a.path).to_uppercase() + "/").unwrap();
    assert_eq!(mysql_data_dir(&a), da);

    // Paket unik yang pindah tempat memakai folder lamanya.
    let mut u = BinPackage::baru(BinKind::MySql, "mysql-8", &jalur(&d.join(r"baru\mysql-8")), "8.0.30");
    u.source_root = jalur(&d.join("baru"));
    let lama = d.join(r"data\mysql-8");
    fs::create_dir_all(&lama).unwrap();
    fs::write(lama.join(BERKAS_PEMILIK), r"Z:\hilang\mysql-8").unwrap();
    assert_eq!(mysql_data_dir(&u), jalur(&lama));
    catat_pemilik_data(&jalur(&lama), Some(&u));
    assert_eq!(pemilik_data(&jalur(&lama)), Some(u.path.clone()));

    let m = write_my_ini(&Profile { mysql_port: 3307, ..Profile::default() }, &u).unwrap();
    let t = isi(&m);
    assert!(t.contains("[mysqld]\r\nport=3307\r\n") && t.contains(&format!("datadir=\"{}\"", fwd_p(&lama))));
    assert!(t.ends_with("sql_mode=\"NO_ENGINE_SUBSTITUTION\"\r\n"));
    assert!(nama_pendek(&jalur(&d)).is_some());
    let _ = fs::remove_dir_all(&d);
}

#[test]
fn tanpa_php_ini_contoh_dan_folder_luar_bin() {
    let (_g, d) = paths::sandbox_uji("cfg-luar");
    let php = php_palsu(&d.join("luar"), "php-8.2", "8.2.0", true, &[], "");
    let mut r = Hasil::default();
    write_php_ini(&profil(&d), &php, &mut r, true, true).unwrap();
    let k: Vec<&str> = r.peringatan.iter().map(|p| p.kunci.as_str()).collect();
    assert!(k[0].starts_with("php.ini ditulis ke {0}, folder yang TIDAK milik Phoron."));
    assert_eq!(k[1], "Tidak ada php.ini contoh di {0}; dibuat dari nol.");
    let t = isi(&Path::new(&php.path).join("php.ini"));
    assert!(t.contains("; --- disetel Phoron ---\r\nextension_dir = "));
    // Tanpa apa pun di ext: tidak ada yang diambil alih.
    assert!(r.ekstensi_diambil.is_none());
    let _ = fs::remove_dir_all(&d);
}
