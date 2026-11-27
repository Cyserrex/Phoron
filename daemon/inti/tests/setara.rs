//! Uji setara: Core Rust harus membaca dan menulis berkas persis seperti Core C#.
//!
//! Bahannya dibangkitkan `Phoron.Tests.exe setara <folder>` (lihat
//! `src/Phoron.Tests/UjiSetara.cs`), lalu folder itu diberikan lewat variabel
//! `PHORON_SETARA`. Tanpa variabel itu uji ini dilewati - jalankan semuanya
//! lewat `daemon\uji_setara.ps1`.
//!
//! Satu fungsi uji, bukan banyak: akar Phoron adalah keadaan global proses,
//! dan uji Rust berjalan paralel.

use phoron_inti::ini::Ini;
use phoron_inti::model::{kamus_set, Profile};
use phoron_inti::{paths, profile_store, settings::Settings};
use std::fs;
use std::path::{Path, PathBuf};

fn salin_folder(dari: &Path, ke: &Path) {
    fs::create_dir_all(ke).unwrap();
    for e in fs::read_dir(dari).unwrap() {
        let e = e.unwrap();
        fs::copy(e.path(), ke.join(e.file_name())).unwrap();
    }
}

fn isi_folder(d: &Path) -> Vec<(String, Vec<u8>)> {
    let mut v: Vec<(String, Vec<u8>)> = fs::read_dir(d)
        .unwrap()
        .map(|e| e.unwrap())
        .map(|e| (e.file_name().to_string_lossy().into_owned(), fs::read(e.path()).unwrap()))
        .collect();
    v.sort();
    v
}

fn beda(nama: &str, rust: &[u8], cs: &[u8]) -> String {
    format!(
        "{nama} berbeda\n--- C# ---\n{}\n--- Rust ---\n{}",
        String::from_utf8_lossy(cs).replace('\0', "\\0"),
        String::from_utf8_lossy(rust).replace('\0', "\\0")
    )
}

fn baris_csharp(f: &Path) -> Vec<String> {
    let t = fs::read_to_string(f).unwrap_or_else(|e| panic!("{}: {e}", f.display()));
    t.lines().map(String::from).collect()
}

fn bandingkan_baris(nama: &str, rust: &[String], cs: &[String], gagal: &mut Vec<String>) {
    if rust == cs {
        return;
    }
    // Hanya baris yang berbeda yang ditampilkan: daftar paket bisa panjang.
    let mut s = format!("{nama}: {} baris Rust vs {} baris C#", rust.len(), cs.len());
    for i in 0..rust.len().max(cs.len()) {
        let (a, b) = (rust.get(i), cs.get(i));
        if a != b {
            s.push_str(&format!("\n  [{i}] C#:   {}\n  [{i}] Rust: {}", b.map_or("-", |x| x), a.map_or("-", |x| x)));
        }
    }
    gagal.push(s);
}

/// Hasil fitur tahap B: pindai bin sungguhan, daftar situs, apps.ini, package.json.
fn fitur(akar: &Path, masuk: &Path, cs: &Path, kerja: &Path, gagal: &mut Vec<String>) {
    use phoron_inti::{bin, node_apps, situs};

    // Pindai bin: akar yang sama persis dengan yang dipakai C#.
    let roots: Vec<String> = baris_csharp(&akar.join("akar-bin.txt"));
    let paket: Vec<String> = bin::scan_all(&roots)
        .iter()
        .map(|p| {
            [
                format!("{:?}", p.kind),
                p.id.clone(),
                p.path.clone(),
                p.version.clone(),
                p.compiler.clone(),
                p.arch.clone(),
                if p.thread_safe { "TS" } else { "NTS" }.into(),
                p.main_exe.clone(),
                p.apache_module_dll.clone(),
                p.source_root.clone(),
                p.kunci().into(),
                if p.kembar { "kembar" } else { "" }.into(),
            ]
            .join("|")
        })
        .collect();
    let paket_cs = baris_csharp(&akar.join("paket.txt"));
    eprintln!("pindai bin: {} paket dari {} akar", paket_cs.len(), roots.len());
    bandingkan_baris("paket.txt", &paket, &paket_cs, gagal);

    // Situs.
    paths::set_root(masuk.join("situs-akar"));
    let hosts = situs::nama_hosts(&fs::read_to_string(paths::root().join("hosts")).unwrap());
    let p = Profile {
        project_roots: vec![
            masuk.join("proyek").to_string_lossy().into_owned(),
            r"Z:\tidak-ada-sama-sekali".into(),
        ],
        ..Profile::default()
    };
    let mut peringatan = Vec::new();
    let mut s: Vec<String> = situs::scan(Some(&p), &hosts, &mut peringatan)
        .iter()
        .map(|x| {
            [
                x.folder.as_str(),
                &x.path,
                &x.root,
                &x.host_name,
                &x.doc_root,
                if x.in_hosts { "hosts" } else { "" },
                if x.has_vhost { "vhost" } else { "" },
            ]
            .join("|")
        })
        .collect();
    s.push(format!("peringatan={}", peringatan.len()));
    bandingkan_baris("situs.txt", &s, &baris_csharp(&cs.join("situs.txt")), gagal);

    // apps.ini dan package.json.
    let d = kerja.join("node");
    fs::create_dir_all(&d).unwrap();
    paths::set_root(&d);
    node_apps::save_all(&[
        node_apps::NodeApp { path: r"D:\web\toko\".into(), port: 3001, ..Default::default() },
        node_apps::NodeApp {
            path: r"D:\web\blog".into(),
            name: "Blog ✓".into(),
            manager: "pnpm".into(),
            node_id: "node-v20".into(),
            script: "start".into(),
            ..Default::default()
        },
        node_apps::NodeApp { path: "  ".into(), ..Default::default() },
    ])
    .unwrap();
    let (br, bc) = (fs::read(d.join("apps.ini")).unwrap(), fs::read(cs.join("node").join("apps.ini")).unwrap());
    if br != bc {
        gagal.push(beda("apps.ini", &br, &bc));
    }
    let proyek = masuk.join("node-proyek").to_string_lossy().into_owned();
    let pkg = vec![
        node_apps::scripts(&proyek).join(","),
        node_apps::detect_manager(&proyek).to_string(),
        node_apps::detect_framework(&proyek).to_string(),
    ];
    bandingkan_baris("package.txt", &pkg, &baris_csharp(&cs.join("node").join("package.txt")), gagal);
}

/// Akar Phoron adalah keadaan global proses; dua uji di berkas ini tidak
/// boleh menggantinya bersamaan.
static KUNCI: std::sync::Mutex<()> = std::sync::Mutex::new(());

/// Data sungguhan: salinan `phoron.ini` dan `profiles\` dari pemasangan yang
/// dipakai sehari-hari (`PHORON_SETARA_NYATA` menunjuk SALINANNYA, bukan
/// aslinya). Berkas yang ditulis Phoron C# harus keluar identik setelah
/// dimuat dan disimpan ulang oleh Rust. Hanya dijalankan di mesin pengembang;
/// datanya tidak pernah masuk repo.
#[test]
fn data_nyata_tidak_berubah_sesudah_muat_simpan() {
    let _g = KUNCI.lock().unwrap_or_else(|e| e.into_inner());
    let Ok(akar) = std::env::var("PHORON_SETARA_NYATA") else { return };
    let akar = PathBuf::from(akar);
    let sebelum_ini = fs::read(akar.join("phoron.ini")).unwrap();
    let sebelum_profil = isi_folder(&akar.join("profiles"));
    paths::set_root(&akar);

    Settings::load().save().unwrap();
    for mut p in profile_store::load_all(None) {
        profile_store::save(&mut p).unwrap();
    }

    let mut gagal = Vec::new();
    let sesudah_ini = fs::read(akar.join("phoron.ini")).unwrap();
    if sesudah_ini != sebelum_ini {
        gagal.push(beda("phoron.ini", &sesudah_ini, &sebelum_ini));
    }
    for ((n, a), (_, b)) in sebelum_profil.iter().zip(isi_folder(&akar.join("profiles")).iter()) {
        if a != b {
            gagal.push(beda(n, b, a));
        }
    }
    eprintln!("data nyata: phoron.ini + {} profil", sebelum_profil.len());
    assert!(gagal.is_empty(), "\n{}", gagal.join("\n\n"));
}

#[test]
fn berkas_identik_dengan_buatan_csharp() {
    let _g = KUNCI.lock().unwrap_or_else(|e| e.into_inner());
    let Ok(akar) = std::env::var("PHORON_SETARA") else {
        eprintln!("PHORON_SETARA tidak disetel; uji setara dilewati");
        return;
    };
    let akar = PathBuf::from(akar);
    let masuk = akar.join("masuk");
    let cs = akar.join("csharp");
    let kerja = akar.join("rust");
    let _ = fs::remove_dir_all(&kerja);
    let mut gagal: Vec<String> = Vec::new();

    // ---- Profil: muat semua, simpan semua, tambah satu profil baru.
    paths::set_root(&kerja);
    salin_folder(&masuk.join("profiles"), &paths::profiles());
    for mut p in profile_store::load_all(None) {
        profile_store::save(&mut p).unwrap();
    }
    let mut baru = Profile {
        name: "Kopi Pagi ✓ 8.3".into(),
        php_id: "php-8.3.12".into(),
        notes: "baris\r\nkedua".into(),
        project_roots: vec![r"D:\proyek".into()],
        ..Profile::default()
    };
    kamus_set(&mut baru.php_ini_overrides, "upload_max_filesize", "64M");
    profile_store::save(&mut baru).unwrap();

    let (r, c) = (isi_folder(&paths::profiles()), isi_folder(&cs.join("profiles")));
    let nama_r: Vec<&String> = r.iter().map(|x| &x.0).collect();
    let nama_c: Vec<&String> = c.iter().map(|x| &x.0).collect();
    if nama_r != nama_c {
        gagal.push(format!("daftar berkas profil berbeda: Rust {nama_r:?}, C# {nama_c:?}"));
    } else {
        for ((n, br), (_, bc)) in r.iter().zip(c.iter()) {
            if br != bc {
                gagal.push(beda(&format!("profiles\\{n}"), br, bc));
            }
        }
    }

    // ---- Setelan: tiap kasus dimuat lalu disimpan ulang.
    let harapan = Ini::load(&akar.join("harapan.ini")).unwrap();
    for kasus in ["setelan-a", "setelan-b", "setelan-c"] {
        let d = kerja.join(kasus);
        fs::create_dir_all(&d).unwrap();
        fs::copy(masuk.join(kasus).join("phoron.ini"), d.join("phoron.ini")).unwrap();
        paths::set_root(&d);
        let mut s = Settings::load();
        if let Some(sandi) = harapan.get(kasus, "sandi") {
            if s.db_sandi != sandi {
                gagal.push(format!("{kasus}: sandi DPAPI buatan C# terbaca {:?}, harusnya {sandi:?}", s.db_sandi));
            }
        }
        s.save().unwrap();
        let br = fs::read(d.join("phoron.ini")).unwrap();
        let bc = fs::read(cs.join(kasus).join("phoron.ini")).unwrap();
        if br != bc {
            gagal.push(beda(&format!("{kasus}\\phoron.ini"), &br, &bc));
        }
    }

    // ---- Tahap B: hasil fitur, bukan berkas.
    fitur(&akar, &masuk, &cs, &kerja, &mut gagal);

    eprintln!("dibandingkan: {} berkas profil {:?}, 3 phoron.ini", c.len(), nama_c);
    assert!(c.len() >= 5, "bahan profil C# kurang dari yang dibangkitkan");
    assert!(gagal.is_empty(), "\n{}", gagal.join("\n\n"));
}
