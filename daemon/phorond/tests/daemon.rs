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
use std::time::{Duration, Instant};
use windows_sys::Win32::System::Pipes::PeekNamedPipe;
use windows_sys::Win32::System::Threading::{OpenEventW, SetEvent, EVENT_MODIFY_STATE};

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

impl Daemon {
    fn mulai(akhiran: &str) -> Daemon {
        let akar = std::env::temp_dir().join(format!("phorond{akhiran}"));
        let _ = std::fs::create_dir_all(&akar);
        let anak = Command::new(env!("CARGO_BIN_EXE_phorond"))
            .args(["--baki", "--pipe-uji", akhiran, "--akar"])
            .arg(&akar)
            .spawn()
            .expect("phorond tidak bisa dijalankan");
        Daemon { anak, akar, akhiran: akhiran.to_string() }
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
        let id = self.kirim("halo", Some(json!({"versi_ui": "uji", "protokol": PROTOKOL})));
        let j = self.jawaban(id, &mut Vec::new());
        assert_eq!(j["status"], "ok", "{j}");
        assert_eq!(j["hasil"]["protokol"], PROTOKOL);
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
    let mut d = Daemon::mulai(&ak);
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
