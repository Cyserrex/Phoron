//! Uji setara fitur "tulis konfigurasi": Core Rust harus menulis berkas yang
//! SAMA PERSIS dengan ConfigWriter C# untuk paket bin sungguhan di mesin ini.
//!
//! Bahannya dibangkitkan `Phoron.Tests.exe setara-modul config <folder>`
//! (src/Phoron.Tests/UjiSetaraConfig.cs) lalu diberikan lewat
//! `PHORON_SETARA_CONFIG`. Tanpa variabel itu uji ini dilewati - jalankan
//! lewat `daemon\uji_setara_modul.ps1 config`.
//!
//! Tiap kasus disalin dari masuk\<kasus> ke kerja\<kasus> - jalur yang sama
//! dengan yang dipakai C# - lalu seluruh berkas dibandingkan byte demi byte
//! dengan csharp\<kasus>. Bila Apache ada, httpd.conf buatan Rust juga diuji
//! dengan `httpd.exe -t`.

use phoron_inti::config::{self, Kolam, Opsi};
use phoron_inti::model::{BinKind, BinPackage, Site};
use phoron_inti::{paths, profile_store};
use std::collections::BTreeMap;
use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command;

fn baris(f: &Path) -> Vec<String> {
    let t = fs::read_to_string(f).unwrap_or_else(|e| panic!("{}: {e}", f.display()));
    t.lines().map(String::from).collect()
}

fn paket(akar: &Path) -> Vec<BinPackage> {
    baris(&akar.join("paket.txt"))
        .iter()
        .map(|b| {
            let x: Vec<&str> = b.split('|').collect();
            assert_eq!(x.len(), 12, "baris paket: {b}");
            let kind = match x[0] {
                "Php" => BinKind::Php,
                "Apache" => BinKind::Apache,
                "Nginx" => BinKind::Nginx,
                "MySql" => BinKind::MySql,
                _ => BinKind::Node,
            };
            let mut p = BinPackage::baru(kind, x[1], x[2], x[3]);
            p.compiler = x[4].into();
            p.arch = x[5].into();
            p.thread_safe = x[6] == "TS";
            p.main_exe = x[7].into();
            p.apache_module_dll = x[8].into();
            p.source_root = x[9].into();
            p.kunci = if x[10] == x[1] { String::new() } else { x[10].into() };
            p.kembar = x[11] == "kembar";
            p
        })
        .collect()
}

#[derive(Default, Debug)]
struct Kasus {
    nama: String,
    nilai: BTreeMap<String, String>,
    kolam: Vec<String>,
}

fn kasus(akar: &Path) -> Vec<Kasus> {
    let mut v: Vec<Kasus> = Vec::new();
    for b in baris(&akar.join("kasus.txt")) {
        let Some((k, isi)) = b.split_once('=') else { continue };
        match k {
            "kasus" => v.push(Kasus { nama: isi.into(), ..Kasus::default() }),
            "kolam" => v.last_mut().unwrap().kolam.push(isi.into()),
            _ => {
                v.last_mut().unwrap().nilai.insert(k.into(), isi.into());
            }
        }
    }
    v
}

fn salin(dari: &Path, ke: &Path) {
    fs::create_dir_all(ke).unwrap();
    for e in fs::read_dir(dari).unwrap() {
        let e = e.unwrap();
        let t = ke.join(e.file_name());
        if e.file_type().unwrap().is_dir() {
            salin(&e.path(), &t);
        } else {
            fs::copy(e.path(), t).unwrap();
        }
    }
}

fn semua_berkas(d: &Path, awal: &Path, v: &mut BTreeMap<String, Vec<u8>>) {
    for e in fs::read_dir(d).unwrap() {
        let e = e.unwrap();
        if e.file_type().unwrap().is_dir() {
            semua_berkas(&e.path(), awal, v);
        } else {
            let rel = e.path().strip_prefix(awal).unwrap().to_string_lossy().to_lowercase();
            v.insert(rel, fs::read(e.path()).unwrap());
        }
    }
}

fn beda_teks(nama: &str, rust: &[u8], cs: &[u8]) -> String {
    let a = String::from_utf8_lossy(rust);
    let b = String::from_utf8_lossy(cs);
    let (la, lb): (Vec<&str>, Vec<&str>) = (a.lines().collect(), b.lines().collect());
    let mut s = format!("{nama}: {} baris Rust vs {} baris C#", la.len(), lb.len());
    let mut n = 0;
    for i in 0..la.len().max(lb.len()) {
        if la.get(i) != lb.get(i) {
            s.push_str(&format!("\n  [{i}] C#:   {}\n  [{i}] Rust: {}", lb.get(i).unwrap_or(&"-"), la.get(i).unwrap_or(&"-")));
            n += 1;
            if n >= 8 {
                s.push_str("\n  ...");
                break;
            }
        }
    }
    s
}

fn hasil(r: &config::Hasil) -> Vec<String> {
    let j = |p: &Option<PathBuf>| p.as_ref().map_or("-".to_string(), |x| x.to_string_lossy().into_owned());
    let mut b = vec![
        format!("httpd={}", j(&r.httpd_conf)),
        format!("myini={}", j(&r.my_ini)),
        format!("phpinidir={}", r.php_ini_dir.clone().unwrap_or_else(|| "-".into())),
        format!("nginx={}", j(&r.nginx_conf)),
        format!("fastcgi={} port={} ssl={}", r.php_fastcgi as u8, r.fastcgi_port, r.ssl_enabled as u8),
        format!("diambil={}", r.ekstensi_diambil.as_ref().map_or("-".to_string(), |v| v.join(","))),
    ];
    for k in &r.kolam {
        let s: Vec<&str> = k.situs.iter().map(|s| s.folder.as_str()).collect();
        b.push(format!("kolam={}|{}|{}|{}|{}", k.php.kunci(), k.port, k.folder_ini, k.ekstensi.join(","), s.join(",")));
    }
    for w in &r.peringatan {
        b.push(format!("peringatan={}", config::teks(w)));
    }
    b
}

/// `httpd -t` pada config buatan Rust; PATH diberi folder PHP supaya DLL
/// pendamping modul PHP termuat seperti saat Phoron menyalakan Apache.
fn uji_live(apache: &BinPackage, php: Option<&BinPackage>, conf: &Path, gagal: &mut Vec<String>) {
    let exe = Path::new(&apache.path).join(r"bin\httpd.exe");
    if !exe.is_file() {
        return;
    }
    let mut path = std::env::var("PATH").unwrap_or_default();
    if let Some(p) = php {
        path = format!("{};{path}", p.path);
    }
    let out = Command::new(&exe).args(["-t", "-f"]).arg(conf).env("PATH", path).output().unwrap();
    let teks = format!("{}{}", String::from_utf8_lossy(&out.stdout), String::from_utf8_lossy(&out.stderr));
    eprintln!("  httpd -t ({}): {}", apache.id, teks.trim());
    if !teks.contains("Syntax OK") {
        gagal.push(format!("httpd -t {} gagal: {}", conf.display(), teks.trim()));
    }
}

/// `nginx -t` pada nginx.conf buatan Rust. ServiceManager memakai folder paket
/// sebagai prefiks; di uji prefiksnya sandbox (dengan logs\ dan temp\ seperti
/// paket nginx) supaya tidak ada yang ditulis ke folder bin.
fn uji_live_nginx(nginx: &BinPackage, conf: &Path, prefiks: &Path, gagal: &mut Vec<String>) {
    let exe = Path::new(&nginx.main_exe);
    if !exe.is_file() || fs::metadata(exe).is_ok_and(|m| m.len() == 0) {
        return;
    }
    fs::create_dir_all(prefiks.join("logs")).unwrap();
    fs::create_dir_all(prefiks.join("temp")).unwrap();
    let out = Command::new(exe)
        .arg("-t")
        .arg("-p")
        .arg(format!("{}\\", prefiks.display()))
        .arg("-c")
        .arg(conf)
        .output()
        .unwrap();
    let teks = format!("{}{}", String::from_utf8_lossy(&out.stdout), String::from_utf8_lossy(&out.stderr));
    eprintln!("  nginx -t ({}): {}", nginx.id, teks.trim());
    if !teks.contains("test is successful") {
        gagal.push(format!("nginx -t {} gagal: {}", conf.display(), teks.trim()));
    }
}

#[test]
fn config_setara_dengan_csharp() {
    let Ok(akar) = std::env::var("PHORON_SETARA_CONFIG") else {
        eprintln!("PHORON_SETARA_CONFIG tidak disetel - uji setara config dilewati");
        return;
    };
    let akar = PathBuf::from(akar);
    let semua = paket(&akar);
    let cari = |k: &str| -> Option<BinPackage> {
        if k.is_empty() {
            return None;
        }
        Some(semua.iter().find(|p| p.kunci() == k).unwrap_or_else(|| panic!("paket {k}")).clone())
    };
    let mut gagal: Vec<String> = Vec::new();
    let daftar = kasus(&akar);
    assert!(!daftar.is_empty());

    for k in &daftar {
        eprintln!("kasus {}", k.nama);
        let masuk = akar.join("masuk").join(&k.nama);
        let kerja = akar.join("kerja").join(&k.nama);
        let _ = fs::remove_dir_all(&kerja);
        salin(&masuk, &kerja);
        paths::set_root(&kerja);

        let profil = profile_store::load(&kerja.join("profiles").join(format!("{}.ini", k.nilai["profil"]))).unwrap();
        let sites: Vec<Site> = baris(&akar.join(format!("situs-{}.txt", k.nama)))
            .iter()
            .map(|b| {
                let x: Vec<&str> = b.split('|').collect();
                Site {
                    folder: x[0].into(),
                    path: x[1].into(),
                    root: x[2].into(),
                    host_name: x[3].into(),
                    doc_root: x[4].into(),
                    ..Site::default()
                }
            })
            .collect();
        let o = k.nilai["opsi"].as_bytes();
        let opsi = Opsi {
            php_ini_ke_folder_php: o[0] == b'1',
            log_akses: o[1] == b'1',
            beranda_di_akar: o[2] == b'1',
            opcache: o[3] == b'1',
            akses_jaringan: o.get(4) == Some(&b'1'),
        };
        let php = cari(&k.nilai["php"]);
        let apache = cari(&k.nilai["apache"]);
        let nginx = cari(&k.nilai["nginx"]);
        let mysql = cari(&k.nilai["mysql"]);
        let ulang: usize = k.nilai["ulang"].parse().unwrap();

        let mut r = None;
        for _ in 0..ulang {
            let kolam: Vec<Kolam> = k
                .kolam
                .iter()
                .map(|b| {
                    let x: Vec<&str> = b.split('|').collect();
                    let p = cari(x[0]).unwrap();
                    let ekstensi = if x[1] == "*" {
                        config::ekstensi_disarankan(Some(&p))
                    } else {
                        x[1].split(',').filter(|s| !s.is_empty()).map(String::from).collect()
                    };
                    let timpa = x[2]
                        .split(';')
                        .filter_map(|kv| kv.split_once('='))
                        .map(|(a, b)| (a.to_string(), b.to_string()))
                        .collect();
                    let nama: Vec<&str> = x[3].split(',').collect();
                    let situs = sites.iter().filter(|s| nama.contains(&s.folder.as_str())).cloned().collect();
                    Kolam::baru(p, ekstensi, timpa, situs)
                })
                .collect();
            r = Some(
                config::build(&profil, php.as_ref(), apache.as_ref(), mysql.as_ref(), nginx.as_ref(), &sites, &opsi, kolam)
                    .unwrap(),
            );
        }
        let r = r.unwrap();

        let h = hasil(&r);
        let h_cs = baris(&akar.join(format!("hasil-cs-{}.txt", k.nama)));
        if h != h_cs {
            gagal.push(format!("{}: hasil berbeda\n--- C# ---\n{}\n--- Rust ---\n{}", k.nama, h_cs.join("\n"), h.join("\n")));
        }

        let mut a = BTreeMap::new();
        let mut b = BTreeMap::new();
        semua_berkas(&kerja, &kerja, &mut a);
        let cs = akar.join("csharp").join(&k.nama);
        semua_berkas(&cs, &cs, &mut b);
        let nama_a: Vec<&String> = a.keys().collect();
        let nama_b: Vec<&String> = b.keys().collect();
        if nama_a != nama_b {
            gagal.push(format!("{}: daftar berkas berbeda\n  C#:   {:?}\n  Rust: {:?}", k.nama, nama_b, nama_a));
        }
        let mut sama = 0;
        for (n, isi) in &a {
            match b.get(n) {
                Some(x) if x == isi => sama += 1,
                Some(x) => gagal.push(beda_teks(&format!("{}\\{n}", k.nama), isi, x)),
                None => {}
            }
        }
        eprintln!("  {sama} berkas identik, {} peringatan", r.peringatan.len());

        // PHP palsu membawa DLL modul kosong yang mustahil dimuat Apache, dan
        // Apache palsu (kasus apache-tua) tidak punya httpd.exe.
        let php_palsu = php.as_ref().is_some_and(|p| {
            !p.apache_module_dll.is_empty() && fs::metadata(&p.apache_module_dll).is_ok_and(|m| m.len() == 0)
        });
        if let (Some(conf), Some(ap), false) = (&r.httpd_conf, &apache, php_palsu) {
            uji_live(ap, php.as_ref(), conf, &mut gagal);
        }
        if let (Some(conf), Some(ng)) = (&r.nginx_conf, &nginx) {
            uji_live_nginx(ng, conf, &kerja, &mut gagal);
        }

        let simpan = akar.join("rust").join(&k.nama);
        let _ = fs::remove_dir_all(&simpan);
        fs::create_dir_all(simpan.parent().unwrap()).unwrap();
        fs::rename(&kerja, &simpan).unwrap();
    }

    assert!(gagal.is_empty(), "{} perbedaan:\n{}", gagal.len(), gagal.join("\n\n"));
}
