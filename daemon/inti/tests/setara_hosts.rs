//! Uji setara fitur "berkas hosts dan HTTPS": hasil tulis Rust harus identik
//! dengan Core C#.
//!
//! Bahannya dibangkitkan `Phoron.Tests.exe setara-modul hosts <folder>` (lihat
//! `src/Phoron.Tests/UjiSetaraHosts.cs`), lalu folder itu diberikan lewat
//! `PHORON_SETARA_HOSTS`. Tanpa variabel itu uji ini dilewati diam-diam -
//! jalankan lewat `daemon\uji_setara_modul.ps1 hosts`.
//!
//! Berkas hosts sistem tidak pernah disentuh: tiap kasus memakai salinan di
//! folder sementara. Bagian SSL hanya MEMBACA paket Apache (openssl.exe) dan
//! menulis sertifikat di sandbox; tidak ada yang dipasang ke Trusted Root.
//!
//! Satu fungsi uji: akar Phoron dan jalur hosts adalah keadaan global proses.

use phoron_inti::model::{BinKind, BinPackage};
use phoron_inti::{hosts, hsts, paths, ssl};
use std::fs;
use std::path::{Path, PathBuf};

fn teks(f: &Path) -> String {
    fs::read_to_string(f).unwrap_or_else(|e| panic!("{}: {e}", f.display()))
}

fn baca_nama(f: &Path) -> Option<Vec<String>> {
    let t = fs::read_to_string(f).ok()?;
    Some(if t.is_empty() { Vec::new() } else { t.split('\n').map(String::from).collect() })
}

fn tampil(b: &[u8]) -> String {
    String::from_utf8_lossy(b).replace('\r', "\\r").replace('\n', "\\n\n")
}

fn coba(r: Result<impl Sized, hosts::GalatHosts>) -> &'static str {
    match r {
        Ok(_) => "ok",
        Err(hosts::GalatHosts::TidakBerhak) => "tidak-berhak",
        Err(_) => "galat",
    }
}

fn atur_readonly(f: &Path, ro: bool) {
    let mut izin = fs::metadata(f).unwrap().permissions();
    izin.set_readonly(ro);
    fs::set_permissions(f, izin).unwrap();
}

fn jalankan_kasus(d_masuk: &Path, d_cs: &Path, kerja: &Path, gagal: &mut Vec<String>) {
    let nama = d_masuk.file_name().unwrap().to_string_lossy().into_owned();
    let d = kerja.join(&nama);
    let akar = d.join("akar");
    fs::create_dir_all(&akar).unwrap();
    paths::set_root(&akar);
    let h = d.join("hosts");
    paths::set_hosts_file(Some(h.clone()));
    if d_masuk.join("hosts").exists() {
        fs::copy(d_masuk.join("hosts"), &h).unwrap();
    }
    if d_masuk.join("readonly").exists() {
        atur_readonly(&h, true);
    }

    let mut hasil = vec![format!("sync1={}", coba(hosts::sync(baca_nama(&d_masuk.join("nama.txt")).unwrap())))];
    if let Some(n2) = baca_nama(&d_masuk.join("nama2.txt")) {
        hasil.push(format!("sync2={}", coba(hosts::sync(n2))));
    }
    let asli = hosts::folder_cadangan().join(hosts::NAMA_ASLI);
    if d_masuk.join("pulihkan").exists() {
        hasil.push(format!("pulihkan={}", coba(hosts::pulihkan(&asli))));
    } else {
        hasil.push(format!("cadangan={}", hosts::daftar_cadangan().iter().filter(|c| !c.asli).count()));
    }
    let b = |x: bool| if x { "True" } else { "False" };
    hasil.push(format!("asli={}", b(asli.exists())));
    hasil.push(format!("readonly={}", b(h.exists() && fs::metadata(&h).unwrap().permissions().readonly())));
    let mut semua: Vec<String> = hosts::all_names().into_iter().collect();
    semua.sort();
    hasil.push(format!("nama={}", semua.join(",")));

    let hasil = hasil.join("\n");
    let hasil_cs = teks(&d_cs.join("hasil.txt"));
    if hasil != hasil_cs {
        gagal.push(format!("{nama}/hasil.txt\n--- C# ---\n{hasil_cs}\n--- Rust ---\n{hasil}"));
    }
    for (f_rust, f_cs) in [(h.clone(), d_cs.join("hosts")), (asli, d_cs.join("hosts-asli.bak"))] {
        let (r, c) = (fs::read(&f_rust).ok(), fs::read(&f_cs).ok());
        if r != c {
            gagal.push(format!(
                "{nama}/{} berbeda\n--- C# ---\n{}\n--- Rust ---\n{}",
                f_cs.file_name().unwrap().to_string_lossy(),
                c.as_deref().map_or("(tidak ada)".into(), tampil),
                r.as_deref().map_or("(tidak ada)".into(), tampil)
            ));
        }
    }
    if h.exists() {
        atur_readonly(&h, false);
    }
    paths::set_hosts_file(None);
}

fn openssl_teks(openssl: &Path, crt: &Path) -> String {
    let o = std::process::Command::new(openssl)
        .args(["x509", "-noout", "-text", "-in"])
        .arg(crt)
        .current_dir(openssl.parent().unwrap())
        .env("OPENSSL_CONF", "nul")
        .output()
        .unwrap();
    String::from_utf8_lossy(&o.stdout).into_owned()
}

fn bagian_ssl(akar: &Path, kerja: &Path, gagal: &mut Vec<String>) {
    let Ok(apache_path) = fs::read_to_string(akar.join("masuk").join("apache.txt")) else {
        eprintln!("(SSL dilewati: tidak ada paket Apache yang membawa openssl.exe)");
        return;
    };
    let apache = BinPackage::baru(BinKind::Apache, "apache", apache_path.trim(), "");
    let openssl = ssl::find_openssl(Some(&apache)).expect("openssl.exe di paket Apache");

    // CA buatan C#: dibaca dengan CryptoAPI dari Rust.
    let cs_akar = akar.join("csharp").join("ssl-akar");
    paths::set_root(&cs_akar);
    let r = format!(
        "generate=ok\nmencakup-test={}\nmencakup-local={}\nmencakup-com={}",
        ssl::ca_mencakup("test"),
        ssl::ca_mencakup("local"),
        ssl::ca_mencakup("com")
    )
    .replace("true", "True")
    .replace("false", "False");
    let c = teks(&akar.join("csharp").join("ssl.txt"));
    if r != c {
        gagal.push(format!("ssl.txt (CA buatan C# dibaca Rust)\n--- C# ---\n{c}\n--- Rust ---\n{r}"));
    }

    // Sertifikat buatan Rust di sandbox sendiri.
    let d = kerja.join("ssl-akar");
    fs::create_dir_all(&d).unwrap();
    paths::set_root(&d);
    if let Err(e) = ssl::generate(Some(&apache), "test", &["local"]) {
        gagal.push(format!("ssl::generate gagal: {e:?}"));
        return;
    }
    for f in ["phoron-ca.cnf", "phoron-ssl.cnf"] {
        let (r, c) = (fs::read(d.join("tmp").join(f)).ok(), fs::read(cs_akar.join("tmp").join(f)).ok());
        if r != c {
            gagal.push(format!("tmp/{f} berbeda dari buatan C#"));
        }
    }
    let ca = openssl_teks(&openssl, &ssl::ca_crt_path());
    let situs = openssl_teks(&openssl, &ssl::crt_path());
    let ca_cs = openssl_teks(&openssl, &cs_akar.join(r"etc\ssl\phoron-ca.crt"));
    let situs_cs = openssl_teks(&openssl, &cs_akar.join(r"etc\ssl\phoron.crt"));
    let tuntut = |nama: &str, isi: &str, harus: &[&str], gagal: &mut Vec<String>| {
        for h in harus {
            if !isi.contains(h) {
                gagal.push(format!("{nama}: tidak memuat \"{h}\"\n{isi}"));
            }
        }
    };
    let san = ["DNS:*.test", "DNS:test", "DNS:localhost", "IP Address:127.0.0.1", "IP Address:0:0:0:0:0:0:0:1", "CA:FALSE", "TLS Web Server Authentication"];
    let nc = ["X509v3 Name Constraints: critical", "DNS:test", "DNS:localhost", "DNS:local", "IP:127.0.0.1/255.255.255.255", "CA:TRUE, pathlen:0"];
    tuntut("sertifikat situs Rust", &situs, &san, gagal);
    tuntut("sertifikat situs C#", &situs_cs, &san, gagal);
    tuntut("CA Rust", &ca, &nc, gagal);
    tuntut("CA C#", &ca_cs, &nc, gagal);
    // Bagian ekstensi kedua CA harus sama, kecuali pengenal kunci.
    let ekstensi = |t: &str| -> Vec<String> {
        let mut v: Vec<String> = Vec::new();
        let mut di = false;
        let mut lewati = false;
        for l in t.lines() {
            let tl = l.trim();
            if tl.starts_with("X509v3 extensions") {
                di = true;
                continue;
            }
            if tl.starts_with("Signature Algorithm") && di {
                break;
            }
            if !di {
                continue;
            }
            if lewati {
                lewati = false;
                continue;
            }
            if tl.contains("Key Identifier") {
                lewati = true;
                v.push(tl.into());
                continue;
            }
            v.push(tl.into());
        }
        v
    };
    if ekstensi(&ca) != ekstensi(&ca_cs) {
        gagal.push(format!("ekstensi CA berbeda\n--- C# ---\n{:#?}\n--- Rust ---\n{:#?}", ekstensi(&ca_cs), ekstensi(&ca)));
    }
    if ekstensi(&situs) != ekstensi(&situs_cs) {
        gagal.push(format!("ekstensi sertifikat situs berbeda\n--- C# ---\n{:#?}\n--- Rust ---\n{:#?}", ekstensi(&situs_cs), ekstensi(&situs)));
    }
    eprintln!("SSL diperiksa dengan {}", openssl.display());
}

#[test]
fn setara_dengan_csharp() {
    let Ok(akar) = std::env::var("PHORON_SETARA_HOSTS") else {
        return;
    };
    let akar = PathBuf::from(akar);
    let kerja = std::env::temp_dir().join(format!("phoron-setara-hosts-rs-{}", std::process::id()));
    let _ = fs::remove_dir_all(&kerja);
    let mut gagal = Vec::new();

    let mut kasus: Vec<PathBuf> =
        fs::read_dir(akar.join("masuk").join("kasus")).unwrap().map(|e| e.unwrap().path()).collect();
    kasus.sort();
    for k in &kasus {
        jalankan_kasus(k, &akar.join("csharp").join(k.file_name().unwrap()), &kerja, &mut gagal);
    }
    eprintln!("hosts: {} kasus", kasus.len());

    // Batasan nama CA.
    let masuk = teks(&akar.join("masuk").join("batasan.txt"));
    let r: Vec<String> = masuk
        .split('\n')
        .map(|l| {
            let v: Vec<&str> = l.split('|').collect();
            ssl::batasan_nama(v[0], &v[1..])
        })
        .collect();
    let c = teks(&akar.join("csharp").join("batasan.txt"));
    if r.join("\n") != c {
        gagal.push(format!("batasan.txt\n--- C# ---\n{c}\n--- Rust ---\n{}", r.join("\n")));
    }

    // HSTS.
    let masuk = teks(&akar.join("masuk").join("hsts.txt"));
    let r: Vec<String> = masuk
        .split('\n')
        .map(|l| format!("{}|{}", if hsts::berlaku(l) { 1 } else { 0 }, hsts::hari_dari_max_age(l)))
        .collect();
    let c = teks(&akar.join("csharp").join("hsts.txt"));
    let cs: Vec<&str> = c.split('\n').collect();
    for (i, l) in masuk.split('\n').enumerate() {
        if r.get(i).map(String::as_str) != cs.get(i).copied() {
            gagal.push(format!("hsts \"{l}\": C# {:?}, Rust {:?}", cs.get(i), r.get(i)));
        }
    }

    bagian_ssl(&akar, &kerja, &mut gagal);

    let _ = fs::remove_dir_all(&kerja);
    assert!(gagal.is_empty(), "{} perbedaan:\n\n{}", gagal.len(), gagal.join("\n\n"));
}
