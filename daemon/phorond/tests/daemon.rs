//! Uji integrasi: menjalankan phorond.exe sungguhan dengan nama uji (akhiran
//! pada mutex/pipe/sinyal), lalu berbicara dengannya lewat pipe.
//!
//! Klien uji sengaja satu utas: pegangan pipe sinkron menjalankan I/O satu
//! per satu, jadi ReadFile yang menunggu akan menahan WriteFile. Klien hanya
//! membaca sebanyak yang sudah tersedia (PeekNamedPipe), tidak pernah
//! menunggu di dalam ReadFile.

use serde_json::{json, Value};
use std::fs::{File, OpenOptions};
use std::io::{Read, Write};
use std::os::windows::io::AsRawHandle;
use std::path::PathBuf;
use std::process::{Child, Command};
use std::sync::atomic::{AtomicU32, Ordering};
use std::time::{Duration, Instant};
use windows_sys::Win32::Foundation::{CloseHandle, LocalFree, HANDLE};
use windows_sys::Win32::Security::Authorization::{
    ConvertSecurityDescriptorToStringSecurityDescriptorW, GetSecurityInfo, SDDL_REVISION_1, SE_KERNEL_OBJECT,
};
use windows_sys::Win32::Security::{DACL_SECURITY_INFORMATION, LABEL_SECURITY_INFORMATION, PSECURITY_DESCRIPTOR};
use windows_sys::Win32::Storage::FileSystem::READ_CONTROL;
use windows_sys::Win32::System::Pipes::PeekNamedPipe;
use windows_sys::Win32::System::Threading::{OpenEventW, OpenMutexW, SetEvent, EVENT_MODIFY_STATE};

const PROTOKOL: u32 = 2;
/// Pamit menunggu utas utama daemon, yang bisa sesaat tertahan oleh
/// Explorer saat banyak ikon baki uji dipasang bersamaan.
const BATAS_PAMIT: Duration = Duration::from_secs(15);

fn akhiran(uji: &str) -> String {
    format!("-uji-{}-{}", std::process::id(), uji)
}

fn lebar(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}

struct Daemon {
    anak: Child,
    akar: PathBuf,
    akhiran: String,
}

/// Kunci teks galat dari daemon (salinan konstanta di phorond).
const PESAN_DITUTUP: &str = "Phoron sedang ditutup.";

impl Daemon {
    fn mulai(akhiran: &str) -> Daemon {
        Daemon::mulai_dengan(akhiran, &[], &[])
    }

    /// Daemon dengan PelaksanaUji (lihat pelaksana.rs) dan env tambahan.
    fn mulai_uji(akhiran: &str, env: &[(&str, &str)], arg: &[&str]) -> Daemon {
        let mut semua = vec![("PHORON_UJI_PELAKSANA", "1")];
        semua.extend_from_slice(env);
        Daemon::mulai_dengan(akhiran, &semua, arg)
    }

    /// Tiap daemon mendapat folder akar sendiri: Drop menghapusnya, dan daemon
    /// kedua bernama sama tidak boleh menghapus akar milik yang pertama.
    fn mulai_dengan(akhiran: &str, env: &[(&str, &str)], arg: &[&str]) -> Daemon {
        static NOMOR: AtomicU32 = AtomicU32::new(0);
        let n = NOMOR.fetch_add(1, Ordering::SeqCst);
        let akar = std::env::temp_dir().join(format!("phorond{akhiran}-{n}"));
        let _ = std::fs::create_dir_all(&akar);
        let mut c = Command::new(env!("CARGO_BIN_EXE_phorond"));
        c.args(["--baki", "--akar"]).arg(&akar).env_remove("PHORON_PIPE").env_remove("PHORON_NAMA_UJI");
        // Tanpa PHORON_PIPE di env, akhiran selalu lewat --pipe-uji.
        if !env.iter().any(|(k, _)| *k == "PHORON_PIPE") {
            c.args(["--pipe-uji", akhiran]);
        }
        c.args(arg);
        for (k, v) in env {
            c.env(k, v);
        }
        let anak = c.spawn().expect("phorond tidak bisa dijalankan");
        Daemon { anak, akar, akhiran: akhiran.to_string() }
    }

    fn ada(&self, berkas: &str) -> bool {
        self.akar.join(berkas).is_file()
    }

    /// Tunggu berkas penanda muncul; isinya.
    fn tunggu_berkas(&self, berkas: &str, batas: Duration) -> Option<String> {
        let sampai = Instant::now() + batas;
        while Instant::now() < sampai {
            if let Ok(s) = std::fs::read_to_string(self.akar.join(berkas)) {
                return Some(s);
            }
            std::thread::sleep(Duration::from_millis(20));
        }
        None
    }

    /// Tunggu proses berakhir; kode keluarnya, atau None bila melewati batas.
    fn tunggu_keluar(&mut self, batas: Duration) -> Option<i32> {
        let sampai = Instant::now() + batas;
        while Instant::now() < sampai {
            if let Some(s) = self.anak.try_wait().unwrap() {
                return Some(s.code().unwrap_or(-1));
            }
            std::thread::sleep(Duration::from_millis(20));
        }
        None
    }
}

impl Drop for Daemon {
    fn drop(&mut self) {
        // Keluar rapi dulu lewat sinyal pemasang supaya ikon baki uji ikut
        // terhapus; baru dibunuh bila tidak berhenti.
        if self.anak.try_wait().ok().flatten().is_none() {
            let nama = lebar(&format!("Phoron.KeluarSekarang{}", self.akhiran));
            let h = unsafe { OpenEventW(EVENT_MODIFY_STATE, 0, nama.as_ptr()) };
            if !h.is_null() {
                unsafe {
                    SetEvent(h);
                    windows_sys::Win32::Foundation::CloseHandle(h);
                }
                let _ = self.tunggu_keluar(Duration::from_secs(5));
            }
        }
        let _ = self.anak.kill();
        let _ = self.anak.wait();
        let _ = std::fs::remove_dir_all(&self.akar);
    }
}

struct Klien {
    f: File,
    tampung: Vec<u8>,
    putus: bool,
    id: u64,
}

impl Klien {
    fn sambung(akhiran: &str) -> Klien {
        let nama = format!(r"\\.\pipe\phoron{akhiran}");
        let sampai = Instant::now() + Duration::from_secs(10);
        loop {
            match OpenOptions::new().read(true).write(true).open(&nama) {
                Ok(f) => return Klien { f, tampung: Vec::new(), putus: false, id: 0 },
                Err(e) => {
                    if Instant::now() > sampai {
                        panic!("pipe {nama} tidak bisa dibuka: {e}");
                    }
                    std::thread::sleep(Duration::from_millis(30));
                }
            }
        }
    }

    fn kirim_mentah(&mut self, s: &str) {
        self.f.write_all(s.as_bytes()).unwrap();
    }

    /// Kirim permintaan; kembalikan id-nya.
    fn kirim(&mut self, perintah: &str, data: Option<Value>) -> u64 {
        self.id += 1;
        let mut v = json!({"id": self.id, "perintah": perintah});
        if let Some(d) = data {
            v["data"] = d;
        }
        self.kirim_mentah(&format!("{v}\n"));
        self.id
    }

    /// Ambil semua byte yang sudah tersedia tanpa menunggu.
    fn isap(&mut self) {
        if self.putus {
            return;
        }
        let mut ada = 0u32;
        let ok = unsafe {
            PeekNamedPipe(
                self.f.as_raw_handle() as _,
                std::ptr::null_mut(),
                0,
                std::ptr::null_mut(),
                &mut ada,
                std::ptr::null_mut(),
            )
        };
        if ok == 0 {
            self.putus = true;
            return;
        }
        if ada > 0 {
            let mut buf = vec![0u8; ada as usize];
            match self.f.read(&mut buf) {
                Ok(0) | Err(_) => self.putus = true,
                Ok(n) => self.tampung.extend_from_slice(&buf[..n]),
            }
        }
    }

    /// Satu baris berikutnya; None bila tidak ada sampai `batas` atau putus.
    fn baris(&mut self, batas: Duration) -> Option<String> {
        let sampai = Instant::now() + batas;
        loop {
            if let Some(pos) = self.tampung.iter().position(|b| *b == b'\n') {
                let b: Vec<u8> = self.tampung.drain(..=pos).collect();
                return Some(String::from_utf8(b).unwrap().trim_end().to_string());
            }
            self.isap();
            if self.putus && !self.tampung.contains(&b'\n') {
                return None;
            }
            if Instant::now() > sampai {
                return None;
            }
            if !self.tampung.contains(&b'\n') {
                std::thread::sleep(Duration::from_millis(2));
            }
        }
    }

    fn pesan(&mut self, batas: Duration) -> Option<Value> {
        self.baris(batas).map(|b| serde_json::from_str(&b).unwrap())
    }

    /// Tunggu jawaban ber-id tertentu; kabar yang lewat dikumpulkan.
    fn jawaban(&mut self, id: u64, kabar: &mut Vec<Value>) -> Value {
        loop {
            let v = self.pesan(Duration::from_secs(10)).unwrap_or_else(|| panic!("jawaban {id} tidak datang"));
            if v.get("id").and_then(Value::as_u64) == Some(id) {
                return v;
            }
            kabar.push(v);
        }
    }

    fn halo(&mut self) {
        let j = self.coba_halo();
        assert_eq!(j["status"], "ok", "{j}");
        assert_eq!(j["hasil"]["protokol"], PROTOKOL);
    }

    fn coba_halo(&mut self) -> Value {
        let id = self.kirim("halo", Some(json!({"versi_ui": "uji", "protokol": PROTOKOL})));
        self.jawaban(id, &mut Vec::new())
    }

    /// Tunggu sampai pipe putus (daemon menutup ujungnya).
    fn tunggu_putus(&mut self, batas: Duration) -> bool {
        let sampai = Instant::now() + batas;
        while Instant::now() < sampai {
            self.isap();
            self.tampung.clear();
            if self.putus {
                return true;
            }
            std::thread::sleep(Duration::from_millis(10));
        }
        false
    }
}

fn catat(teks: &str) -> Value {
    json!({"pesan": {"kunci": teks}})
}

#[test]
fn halo_keadaan_dan_kabar_ke_semua_klien() {
    let ak = akhiran("kabar");
    let _d = Daemon::mulai(&ak);
    let mut a = Klien::sambung(&ak);
    let mut b = Klien::sambung(&ak);
    a.halo();
    b.halo();

    let id = a.kirim("keadaan", None);
    let j = a.jawaban(id, &mut Vec::new());
    assert_eq!(j["status"], "ok");
    assert_eq!(j["hasil"]["status"]["web"], "Berhenti");
    assert_eq!(j["hasil"]["profil_aktif"], "");

    let id = a.kirim("nyalakan_web", None);
    let j = a.jawaban(id, &mut Vec::new());
    assert_eq!(j["galat"]["jenis"], "belum_ada");
    assert_eq!(j["galat"]["perintah"], "nyalakan_web");

    // Klien yang pamit tanpa kata tidak boleh mengganggu siaran.
    let mut c = Klien::sambung(&ak);
    c.halo();
    drop(c);

    let id = a.kirim("catat", Some(catat("uji siaran")));
    let mut kabar_a = Vec::new();
    let j = a.jawaban(id, &mut kabar_a);
    assert_eq!(j["status"], "ok");
    // Kabar bisa datang sebelum jawabannya (disiarkan di dalam perintah).
    let ka = if kabar_a.is_empty() { a.pesan(Duration::from_secs(5)).unwrap() } else { kabar_a.remove(0) };
    let kb = b.pesan(Duration::from_secs(5)).expect("klien B tidak menerima kabar");
    assert_eq!(ka["kabar"], "log");
    assert_eq!(ka["data"]["pesan"]["kunci"], "uji siaran");
    assert_eq!(ka, kb, "dua klien harus menerima kabar yang sama persis");
}

#[test]
fn klien_lambat_tidak_menahan_klien_lain() {
    let ak = akhiran("lambat");
    let _d = Daemon::mulai(&ak);
    let mut a = Klien::sambung(&ak);
    let mut b = Klien::sambung(&ak);
    let mut lambat = Klien::sambung(&ak);
    a.halo();
    b.halo();
    lambat.halo();
    // `lambat` sesudah ini tidak pernah membaca.

    let isi = "x".repeat(16 * 1024);
    let jumlah = 400;
    let mut diterima_b = 0;
    let mulai = Instant::now();
    for i in 0..jumlah {
        let id = a.kirim("catat", Some(catat(&format!("{i} {isi}"))));
        let mut lewat = Vec::new();
        a.jawaban(id, &mut lewat);
        while let Some(v) = b.pesan(Duration::from_millis(0)) {
            assert_eq!(v["kabar"], "log");
            diterima_b += 1;
        }
    }
    while diterima_b < jumlah {
        let v = b.pesan(Duration::from_secs(5)).expect("kabar untuk B terhenti");
        assert_eq!(v["kabar"], "log");
        diterima_b += 1;
    }
    // 400 x 16 KiB jauh melebihi penyangga pipe + antrean: kalau siaran
    // menunggu klien lambat, perulangan di atas sudah macet bermenit-menit.
    assert!(mulai.elapsed() < Duration::from_secs(30), "siaran tertahan klien lambat: {:?}", mulai.elapsed());
    assert!(lambat.tunggu_putus(Duration::from_secs(10)), "klien lambat seharusnya diputus");

    // A masih dilayani seperti biasa.
    let id = a.kirim("keadaan", None);
    assert_eq!(a.jawaban(id, &mut Vec::new())["status"], "ok");
}

#[test]
fn halo_wajib_dan_protokol_beda_ditolak() {
    let ak = akhiran("halo");
    let _d = Daemon::mulai(&ak);

    let mut a = Klien::sambung(&ak);
    let id = a.kirim("halo", Some(json!({"versi_ui": "lama", "protokol": 1})));
    let j = a.jawaban(id, &mut Vec::new());
    assert_eq!(j["galat"]["jenis"], "protokol_beda");
    assert_eq!(j["galat"]["daemon"], PROTOKOL);
    assert_eq!(j["galat"]["ui"], 1);
    assert!(a.tunggu_putus(Duration::from_secs(5)));

    let mut b = Klien::sambung(&ak);
    let id = b.kirim("keadaan", None);
    let j = b.jawaban(id, &mut Vec::new());
    assert_eq!(j["status"], "galat");
    assert_eq!(j["galat"]["jenis"], "gagal");
    assert!(b.tunggu_putus(Duration::from_secs(5)));

    // Sesudah Halo, baris rusak dijawab galat tapi sambungan tetap hidup.
    let mut c = Klien::sambung(&ak);
    c.halo();
    c.kirim_mentah("{\"id\":41,\"perintah\":\"tidak_dikenal\"}\n");
    let j = c.jawaban(41, &mut Vec::new());
    assert_eq!(j["galat"]["jenis"], "tidak_terbaca");
    c.kirim_mentah("ngawur\n");
    let j = c.pesan(Duration::from_secs(5)).unwrap();
    assert_eq!(j["id"], 0);
    assert_eq!(j["galat"]["jenis"], "tidak_terbaca");
    let id = c.kirim("keadaan", None);
    assert_eq!(c.jawaban(id, &mut Vec::new())["status"], "ok");
}

#[test]
fn keluar_memamitkan_semua_klien_lalu_proses_berakhir() {
    let ak = akhiran("keluar");
    let mut d = Daemon::mulai_uji(&ak, &[], &[]);
    let mut a = Klien::sambung(&ak);
    let mut b = Klien::sambung(&ak);
    a.halo();
    b.halo();

    let id = a.kirim("keluar", None);
    let mut kabar = Vec::new();
    let j = a.jawaban(id, &mut kabar);
    assert_eq!(j["status"], "ok");
    assert!(kabar.is_empty(), "jawaban keluar harus datang sebelum pamit");
    let mulai = Instant::now();
    let keluar = d.tunggu_keluar(Duration::from_secs(10));
    eprintln!("keluar -> proses berakhir: {:?}", mulai.elapsed());
    // Proses sudah berakhir pun, sisa data di pipe tetap bisa dibaca klien.
    assert_eq!(a.pesan(BATAS_PAMIT).expect("pamit tidak datang")["kabar"], "pamit");
    assert_eq!(b.pesan(BATAS_PAMIT).expect("pamit tidak datang")["kabar"], "pamit");
    assert_eq!(keluar, Some(0), "phorond tidak berhenti");
    assert!(d.ada("sebelum_keluar.txt"), "sebelum_keluar tidak dipanggil");
}

#[test]
fn instans_tunggal_dan_sinyal_keluar_pemasang() {
    let ak = akhiran("sinyal");
    let mut d = Daemon::mulai(&ak);
    let mut a = Klien::sambung(&ak);
    a.halo();

    // Daemon kedua bernama sama langsung keluar dan tidak mengganggu yang pertama.
    let mut kedua = Daemon::mulai(&ak);
    assert_eq!(kedua.tunggu_keluar(Duration::from_secs(10)), Some(0));
    let id = a.kirim("keadaan", None);
    assert_eq!(a.jawaban(id, &mut Vec::new())["status"], "ok");

    let nama = lebar(&format!("Phoron.KeluarSekarang{ak}"));
    let h = unsafe { OpenEventW(EVENT_MODIFY_STATE, 0, nama.as_ptr()) };
    assert!(!h.is_null(), "sinyal keluar tidak ditemukan");
    let mulai = Instant::now();
    unsafe {
        SetEvent(h);
        windows_sys::Win32::Foundation::CloseHandle(h);
    }
    assert_eq!(a.pesan(BATAS_PAMIT).expect("pamit tidak datang")["kabar"], "pamit");
    assert_eq!(d.tunggu_keluar(Duration::from_secs(10)), Some(0));
    eprintln!("sinyal keluar -> proses berakhir: {:?}", mulai.elapsed());
}

fn galat_gagal(j: &Value) -> String {
    assert_eq!(j["status"], "galat", "{j}");
    assert_eq!(j["galat"]["jenis"], "gagal", "{j}");
    j["galat"]["pesan"]["kunci"].as_str().unwrap_or("").to_string()
}

#[test]
fn panik_satu_perintah_jadi_galat_dan_daemon_tetap_hidup() {
    let ak = akhiran("panik");
    let d = Daemon::mulai_uji(&ak, &[], &[]);
    let mut a = Klien::sambung(&ak);
    a.halo();

    let id = a.kirim("catat", Some(catat("uji:panik")));
    let j = a.jawaban(id, &mut Vec::new());
    let kunci = galat_gagal(&j);
    assert!(kunci.contains("galat tak terduga"), "{j}");
    assert_eq!(j["galat"]["pesan"]["args"][0], "catat");

    // Daemon tetap menjawab, juga untuk klien baru.
    let id = a.kirim("keadaan", None);
    assert_eq!(a.jawaban(id, &mut Vec::new())["status"], "ok");
    let mut b = Klien::sambung(&ak);
    b.halo();

    let logs = d.akar.join("logs");
    let crash: Vec<_> = std::fs::read_dir(&logs)
        .map(|r| r.filter_map(|e| e.ok()).filter(|e| e.file_name().to_string_lossy().starts_with("crash-phorond-")).collect())
        .unwrap_or_default();
    assert!(!crash.is_empty(), "berkas crash tidak ditulis di {}", logs.display());
    let isi = std::fs::read_to_string(crash[0].path()).unwrap();
    assert!(isi.contains("panik uji di tangani"), "{isi}");
}

#[test]
fn panik_di_utas_utama_tetap_keluar_rapi() {
    let ak = akhiran("panikutama");
    let mut d = Daemon::mulai_uji(&ak, &[], &[]);
    let mut a = Klien::sambung(&ak);
    a.halo();
    let id = a.kirim("catat", Some(catat("uji:panik-utama")));
    let mut kabar = Vec::new();
    assert_eq!(a.jawaban(id, &mut kabar)["status"], "ok");
    let pamit = kabar.into_iter().chain(std::iter::from_fn(|| a.pesan(BATAS_PAMIT))).find(|v| v["kabar"] == "pamit");
    assert!(pamit.is_some(), "pamit tidak datang sesudah panik utas utama");
    assert_eq!(d.tunggu_keluar(Duration::from_secs(10)), Some(101));
    assert!(d.ada("sebelum_keluar.txt"), "sebelum_keluar tidak dipanggil");
}

#[test]
fn gerbang_keluar_menolak_perintah_dan_sambungan_baru() {
    let ak = akhiran("gerbang");
    // sebelum_keluar ditahan 2 detik supaya ada jendela waktu untuk menguji.
    let mut d = Daemon::mulai_uji(&ak, &[("PHORON_UJI_TAHAN_KELUAR", "2000")], &[]);
    let mut a = Klien::sambung(&ak);
    let mut b = Klien::sambung(&ak);
    a.halo();
    b.halo();

    let id = a.kirim("keluar", None);
    assert_eq!(a.jawaban(id, &mut Vec::new())["status"], "ok");

    let id = b.kirim("nyalakan_web", None);
    assert_eq!(galat_gagal(&b.jawaban(id, &mut Vec::new())), PESAN_DITUTUP);

    let mut c = Klien::sambung(&ak);
    assert_eq!(galat_gagal(&c.coba_halo()), PESAN_DITUTUP);
    assert!(c.tunggu_putus(Duration::from_secs(5)), "klien baru seharusnya diputus");

    assert_eq!(a.pesan(BATAS_PAMIT).expect("pamit tidak datang")["kabar"], "pamit");
    assert_eq!(b.pesan(BATAS_PAMIT).expect("pamit tidak datang")["kabar"], "pamit");
    assert_eq!(d.tunggu_keluar(Duration::from_secs(10)), Some(0));
    assert!(d.ada("sebelum_keluar.txt"));
}

#[test]
fn klien_tanpa_halo_diputus_sesudah_10_detik() {
    let ak = akhiran("diam");
    let _d = Daemon::mulai(&ak);
    let mulai = Instant::now();
    let mut a = Klien::sambung(&ak);
    let mut b = Klien::sambung(&ak);
    b.halo();
    assert!(a.tunggu_putus(Duration::from_secs(20)), "klien diam tidak diputus");
    let lama = mulai.elapsed();
    assert!(lama >= Duration::from_secs(9), "diputus terlalu cepat: {lama:?}");
    // Klien yang sudah halo tidak terkena tenggat.
    let id = b.kirim("keadaan", None);
    assert_eq!(b.jawaban(id, &mut Vec::new())["status"], "ok");
}

#[test]
fn tunggu_pid_lalu_nyalakan_awal() {
    let ak = akhiran("tunggupid");
    // Proses "daemon lama" tiruan: hidup sekitar 2 detik.
    let mut lama = Command::new("ping").args(["-n", "3", "127.0.0.1"]).stdout(std::process::Stdio::null()).spawn().unwrap();
    let pid = lama.id().to_string();
    let mulai = Instant::now();
    let d = Daemon::mulai_uji(&ak, &[], &["--tunggu-pid", &pid, "--nyalakan", "web|db"]);
    let mut a = Klien::sambung(&ak);
    let siap = mulai.elapsed();
    assert!(lama.try_wait().unwrap().is_some(), "daemon tidak menunggu proses lama");
    assert!(siap >= Duration::from_millis(1500), "pipe siap sebelum proses lama berakhir: {siap:?}");
    a.halo();
    assert_eq!(d.tunggu_berkas("nyalakan_awal.txt", Duration::from_secs(10)).as_deref(), Some("web|db"));
    let _ = lama.wait();
}

#[test]
fn pipe_dari_env_phoron_pipe() {
    let ak = akhiran("envpipe");
    let nama = format!("phoron{ak}");
    let _d = Daemon::mulai_dengan(&ak, &[("PHORON_PIPE", &nama)], &[]);
    let mut a = Klien::sambung(&ak);
    a.halo();
    // Mutex dan sinyal ikut berakhiran (Drop memakai sinyal Phoron.KeluarSekarang<ak>).
    let m = unsafe { OpenMutexW(READ_CONTROL, 0, lebar(&format!("Phoron.Daemon{ak}")).as_ptr()) };
    assert!(!m.is_null(), "mutex tidak berakhiran nama pipe");
    unsafe { CloseHandle(m) };
}

fn baca_sddl(h: HANDLE) -> String {
    let bagian = DACL_SECURITY_INFORMATION | LABEL_SECURITY_INFORMATION;
    unsafe {
        let mut psd: PSECURITY_DESCRIPTOR = std::ptr::null_mut();
        let n = std::ptr::null_mut();
        let acl = std::ptr::null_mut();
        assert_eq!(GetSecurityInfo(h, SE_KERNEL_OBJECT, bagian, n, n, acl, acl, &mut psd), 0);
        let mut teks: *mut u16 = std::ptr::null_mut();
        let ok = ConvertSecurityDescriptorToStringSecurityDescriptorW(psd, SDDL_REVISION_1, bagian, &mut teks, std::ptr::null_mut());
        LocalFree(psd);
        assert!(ok != 0);
        let mut len = 0;
        while *teks.add(len) != 0 {
            len += 1;
        }
        let s = String::from_utf16_lossy(std::slice::from_raw_parts(teks, len));
        LocalFree(teks.cast());
        s
    }
}

/// DACL hanya berisi izin untuk SID pengguna, SYSTEM, dan Administrators.
fn periksa_sddl(s: &str, label: &str) {
    let i = s.find("S:").unwrap_or_else(|| panic!("tanpa label: {s}"));
    let (dacl, sacl) = s.split_at(i);
    assert!(dacl.starts_with("D:"), "{s}");
    for ace in dacl.split('(').skip(1) {
        let medan: Vec<&str> = ace.trim_end_matches(')').split(';').collect();
        assert_eq!(medan[0], "A", "{s}");
        let sid = *medan.last().unwrap();
        assert!(sid == "SY" || sid == "BA" || sid.starts_with("S-1-5-21-") || sid.starts_with("S-1-12-"), "SID tak terduga {sid}: {s}");
    }
    assert!(!dacl.contains(";WD)") && !dacl.contains(";AN)"), "{s}");
    assert!(sacl.contains(&format!("(ML;;NW;;;{label})")), "label bukan {label}: {s}");
}

#[test]
fn pipe_mutex_dan_sinyal_berizin_eksplisit() {
    let ak = akhiran("izin");
    let _d = Daemon::mulai(&ak);
    let a = Klien::sambung(&ak);
    periksa_sddl(&baca_sddl(a.f.as_raw_handle() as HANDLE), "ME");

    let m = unsafe { OpenMutexW(READ_CONTROL, 0, lebar(&format!("Phoron.Daemon{ak}")).as_ptr()) };
    assert!(!m.is_null());
    periksa_sddl(&baca_sddl(m), "ME");
    unsafe { CloseHandle(m) };

    let e = unsafe { OpenEventW(READ_CONTROL, 0, lebar(&format!("Phoron.KeluarSekarang{ak}")).as_ptr()) };
    assert!(!e.is_null());
    periksa_sddl(&baca_sddl(e), "LW");
    unsafe { CloseHandle(e) };
}

#[test]
fn wm_close_dari_taskkill_keluar_rapi() {
    let ak = akhiran("taskkill");
    let mut d = Daemon::mulai_uji(&ak, &[], &[]);
    let mut a = Klien::sambung(&ak);
    a.halo();
    let st = Command::new("taskkill")
        .args(["/PID", &d.anak.id().to_string()])
        .stdout(std::process::Stdio::null())
        .stderr(std::process::Stdio::null())
        .status()
        .unwrap();
    assert!(st.success(), "taskkill tanpa /f gagal");
    assert_eq!(a.pesan(BATAS_PAMIT).expect("pamit tidak datang")["kabar"], "pamit");
    assert_eq!(d.tunggu_keluar(Duration::from_secs(10)), Some(0));
    assert!(d.ada("sebelum_keluar.txt"));
}

#[test]
fn sinkron_hosts_tanpa_admin_keluar_dengan_kode_tidak_berhak() {
    if phoron_inti::tulis::is_admin() {
        // Ber-Administrator, pembantu ini SUNGGUH menulis hosts sistem.
        eprintln!("dilewati: uji berjalan sebagai Administrator");
        return;
    }
    let akar = std::env::temp_dir().join(format!("phorond-sinkron-{}", std::process::id()));
    let _ = std::fs::create_dir_all(&akar);
    let mut anak = Command::new(env!("CARGO_BIN_EXE_phorond")).arg("--sinkron-hosts").arg(&akar).spawn().unwrap();
    let sampai = Instant::now() + Duration::from_secs(10);
    let kode = loop {
        if let Some(s) = anak.try_wait().unwrap() {
            break s.code();
        }
        assert!(Instant::now() < sampai, "--sinkron-hosts tidak berakhir");
        std::thread::sleep(Duration::from_millis(20));
    };
    let _ = std::fs::remove_dir_all(&akar);
    assert_eq!(kode, Some(phoron_inti::hosts::KELUAR_TIDAK_BERHAK));
}
