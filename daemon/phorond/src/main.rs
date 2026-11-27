//! phorond: daemon Phoron. Memegang ikon baki dan (nanti) semua layanan;
//! UI WPF hanya klien named pipe yang hidup selama jendelanya terbuka.
//! Lihat `daemon/RENCANA.md` dan `daemon/PROTOKOL.md`.
//!
//! Argumen:
//! - `--baki`            mulai tanpa membuka UI (autostart Windows memakai ini);
//!                       tanpa argumen = mulai lalu buka UI.
//! - `--akar <folder>`   akar instalasi Phoron (`paths::set_root`).
//! - `--pipe-uji <akhiran>` beri akhiran pada nama mutex, pipe, dan sinyal
//!                       keluar, sama seperti env `PHORON_NAMA_UJI`. Hanya
//!                       untuk uji: daemon uji tidak boleh bertabrakan dengan
//!                       Phoron yang sedang dipakai.
//! - `--pipe <nama>`     nama pipe lain (tanpa `\\.\pipe\`), sama seperti env
//!                       `PHORON_PIPE` yang diturunkan KlienDaemon C#.
//! - `--tunggu-pid <pid>` tunggu proses itu berakhir (maks 30 detik) sebelum
//!                       mengambil mutex: dipakai daemon pengganti
//!                       ber-Administrator (`MulaiUlangAdmin`).
//! - `--nyalakan <a|b|..>` daftar layanan untuk `Pelaksana::nyalakan_awal`.
//! - `--sinkron-hosts [<akar>]` pembantu ber-Administrator: tulis blok hosts
//!                       semua situs lalu keluar dengan kode
//!                       `phoron_inti::hosts::KELUAR_*`. Tanpa mutex, baki,
//!                       maupun pipe (padanan `Phoron.exe --sinkron-hosts`).

// Build release tanpa jendela konsol. Build debug (dipakai uji) tetap
// berkonsol supaya keluaran panik terlihat saat menelusuri masalah.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod admin;
mod baki;
mod keamanan;
mod nama;
mod pelaksana;
mod pelaksana_engine;
mod penyiar;
mod pipa;
mod siklus;

use keamanan::{sa_atau_null, Deskriptor, Label};
use nama::{lebar, Nama};
use pelaksana::{aman, Pelaksana, PelaksanaSementara, PelaksanaUji};
use penyiar::Penyiar;
use phoron_inti::paths;
use std::path::PathBuf;
use std::sync::Arc;
use windows_sys::Win32::Foundation::WAIT_OBJECT_0;
use windows_sys::Win32::Foundation::{GetLastError, ERROR_ACCESS_DENIED, ERROR_ALREADY_EXISTS, HANDLE};
use windows_sys::Win32::System::Threading::{CreateEventW, CreateMutexW, WaitForSingleObject, INFINITE};

#[derive(Debug, Default, PartialEq)]
pub(crate) struct Argumen {
    pub baki: bool,
    pub akar: Option<PathBuf>,
    pub akhiran: Option<String>,
    pub pipe: Option<String>,
    pub tunggu_pid: Option<u32>,
    pub nyalakan: Vec<String>,
    /// `Some(akar opsional)` = mode pembantu `--sinkron-hosts`.
    pub sinkron_hosts: Option<Option<PathBuf>>,
}

pub(crate) fn urai_argumen(args: impl IntoIterator<Item = String>) -> Argumen {
    let mut a = Argumen::default();
    let mut it = args.into_iter().peekable();
    while let Some(s) = it.next() {
        match s.to_ascii_lowercase().as_str() {
            "--baki" => a.baki = true,
            "--akar" => a.akar = it.next().map(PathBuf::from),
            "--pipe-uji" => a.akhiran = it.next(),
            "--pipe" => a.pipe = it.next(),
            "--tunggu-pid" => a.tunggu_pid = it.next().and_then(|p| p.trim().parse().ok()),
            "--nyalakan" => {
                if let Some(d) = it.next() {
                    a.nyalakan.extend(d.split('|').filter(|s| !s.is_empty()).map(String::from));
                }
            }
            "--sinkron-hosts" => {
                let akar = match it.peek() {
                    Some(n) if !n.starts_with("--") => it.next().map(PathBuf::from),
                    _ => None,
                };
                a.sinkron_hosts = Some(akar);
            }
            // Argumen tak dikenal diabaikan: pintasan lama atau versi UI
            // yang lebih baru tidak boleh membuat daemon menolak mulai.
            _ => {}
        }
    }
    a
}

/// Panic hook: tulis berkas crash untuk SETIAP panik, termasuk yang nanti
/// ditangkap (`pelaksana::tangani_aman`). Tidak mengakhiri proses: panik yang
/// tertangkap jadi `Galat::Gagal`, panik di utas utama menempuh jalur keluar
/// rapi (`baki::keluar_sesudah_panik` / prosedur jendela).
fn pasang_panic_hook() {
    std::panic::set_hook(Box::new(|info| {
        let utas = std::thread::current().name().unwrap_or("?").to_string();
        let isi = format!(
            "phorond {} menemui galat tak terduga\r\nwaktu: {}\r\nutas: {}\r\n{}\r\n\r\n{}\r\n",
            phoron_inti::VERSI,
            phoron_inti::waktu::Saat::sekarang().ke_o(),
            utas,
            info,
            std::backtrace::Backtrace::force_capture(),
        );
        // Sengaja tidak lewat paths::logs(): itu memegang kunci singgahan yang
        // bisa saja sedang dipegang utas yang panik.
        let folder = paths::root().join("logs");
        let _ = std::fs::create_dir_all(&folder);
        let jalur = folder.join(format!("crash-phorond-{}.txt", phoron_inti::waktu::cap_lokal_berkas()));
        let _ = std::fs::write(&jalur, isi);
        eprintln!("{info}");
    }));
}

/// `true` bila daemon ini pemilik mutex instans tunggal.
fn ambil_mutex(nama: &str) -> bool {
    let w = lebar(nama);
    let d = Deskriptor::baru(Label::Sedang);
    let h = unsafe { CreateMutexW(sa_atau_null(&d), 1, w.as_ptr()) };
    let e = unsafe { GetLastError() };
    if h.is_null() {
        // ERROR_ACCESS_DENIED: mutexnya ada, milik daemon berhak lain.
        return e != ERROR_ACCESS_DENIED;
    }
    // Pegangan sengaja tidak ditutup: mutex hidup selama proses hidup.
    e != ERROR_ALREADY_EXISTS
}

/// Pengawas sinyal `Phoron.KeluarSekarang`. Event AUTO-reset, sama seperti
/// yang dibuat UI WPF lama (`ObjekAntarProses.BuatEvent`): jenisnya ditentukan
/// pembuat pertama, dan manual-reset yang tersinyal akan tetap tersinyal
/// selama ada pemegang pegangan - phorond baru yang dijalankan pemasang
/// sesudah pembaruan lalu langsung keluar lagi. Berlabel Low dengan SID
/// pengguna, supaya pemasang dan proses berhak lain tetap bisa memberi sinyal.
fn awasi_sinyal_keluar(nama: &str) {
    let w = lebar(nama);
    let d = Deskriptor::baru(Label::Rendah);
    let h: HANDLE = unsafe { CreateEventW(sa_atau_null(&d), 0, 0, w.as_ptr()) };
    if h.is_null() {
        return;
    }
    let h = h as isize;
    let _ = std::thread::Builder::new()
        .name("sinyal-keluar".into())
        .stack_size(64 * 1024)
        .spawn(move || {
            if unsafe { WaitForSingleObject(h as HANDLE, INFINITE) } == WAIT_OBJECT_0 {
                baki::minta_keluar();
            }
        });
}

fn main() {
    pasang_panic_hook();
    let arg = urai_argumen(std::env::args().skip(1));
    if let Some(akar) = &arg.sinkron_hosts {
        if let Some(a) = akar.as_ref().or(arg.akar.as_ref()) {
            paths::set_root(a);
        }
        let kode = aman(phoron_inti::hosts::sinkron_hosts_saja).unwrap_or(phoron_inti::hosts::KELUAR_GALAT);
        std::process::exit(kode);
    }
    // Panik di utas utama di luar prosedur jendela (prosedur menangkap
    // paniknya sendiri) tetap menempuh jalur keluar rapi.
    if std::panic::catch_unwind(move || jalankan(arg)).is_err() {
        baki::keluar_sesudah_panik();
    }
}

fn jalankan(arg: Argumen) {
    if let Some(a) = &arg.akar {
        paths::set_root(a);
    }
    let akhiran = arg
        .akhiran
        .clone()
        .or_else(|| std::env::var("PHORON_NAMA_UJI").ok())
        .unwrap_or_default();
    let pipe = arg.pipe.clone().or_else(|| std::env::var("PHORON_PIPE").ok()).unwrap_or_default();
    let nama = Nama::dari(&akhiran, &pipe);
    admin::simpan_argumen(admin::ArgUlang { akar: arg.akar.clone(), akhiran: nama.akhiran.clone(), pipe });

    if let Some(pid) = arg.tunggu_pid {
        // Daemon lama masih menjalankan sebelum_keluar; mutex dan pipenya baru
        // lepas saat prosesnya berakhir.
        admin::tunggu_pid(pid, admin::BATAS_TUNGGU_PID);
    }

    if !ambil_mutex(&nama.mutex) {
        // Daemon sudah berjalan. Menjalankan phorond lagi (mis. dari pintasan)
        // artinya "tampilkan Phoron".
        if !arg.baki && !nama.uji() {
            baki::buka_ui();
        }
        return;
    }

    let penyiar = Penyiar::baru();
    // Engine Rust sungguhan. Daemon uji (nama berakhiran) memakai pelaksana
    // sementara kecuali diminta lain: Engine memindai folder bin sungguhan dan
    // uji siklus hidup tidak membutuhkannya.
    let pakai_engine = !nama.uji() || std::env::var("PHORON_UJI_ENGINE").as_deref() == Ok("1");
    let mut nyala_otomatis = false;
    let dasar: Box<dyn Pelaksana> = if pakai_engine {
        let e = pelaksana_engine::PelaksanaEngine::baru(penyiar.clone());
        nyala_otomatis = e.mulai_otomatis();
        Box::new(e)
    } else {
        Box::new(PelaksanaSementara::baru(penyiar.clone()))
    };
    let pelaksana: Arc<dyn Pelaksana> = if nama.uji() && std::env::var("PHORON_UJI_PELAKSANA").as_deref() == Ok("1") {
        Arc::new(PelaksanaUji::bungkus(dasar))
    } else {
        Arc::from(dasar)
    };

    if pipa::mulai(&nama.pipe, pelaksana.clone(), penyiar.clone()).is_err() {
        std::process::exit(siklus::KELUAR_PIPE_GAGAL);
    }
    awasi_sinyal_keluar(&nama.sinyal_keluar);
    if !arg.nyalakan.is_empty() {
        let p = pelaksana.clone();
        let daftar = arg.nyalakan.clone();
        let _ = std::thread::Builder::new().name("nyalakan-awal".into()).spawn(move || {
            if !siklus::gerbang_tertutup() {
                let _ = aman(|| p.nyalakan_awal(&daftar));
            }
        });
    }
    // Setelan "nyalakan layanan saat Phoron dibuka". Tidak dipakai bila daemon
    // ini pengganti ber-Administrator: daftar --nyalakan sudah menyebut persis
    // apa yang tadi hidup.
    if nyala_otomatis && arg.nyalakan.is_empty() {
        let p = pelaksana.clone();
        let _ = std::thread::Builder::new().name("nyala-otomatis".into()).spawn(move || {
            if !siklus::gerbang_tertutup() {
                let _ = pelaksana::tangani_aman(&*p, phoron_inti::protokol::Perintah::NyalakanSemua);
            }
        });
    }
    let buka = !arg.baki && !nama.uji();
    baki::jalankan(baki::Bersama { pelaksana, penyiar, nama }, buka);
}

#[cfg(test)]
mod uji {
    use super::*;

    fn a(s: &[&str]) -> Argumen {
        urai_argumen(s.iter().map(|x| x.to_string()))
    }

    #[test]
    fn argumen_dikenali() {
        assert_eq!(a(&[]), Argumen::default());
        assert_eq!(
            a(&["--baki", "--akar", r"D:\Phoron", "--pipe-uji", "-u1", "--lain"]),
            Argumen {
                baki: true,
                akar: Some(PathBuf::from(r"D:\Phoron")),
                akhiran: Some("-u1".into()),
                ..Argumen::default()
            }
        );
        assert_eq!(a(&["--BAKI"]).baki, true);
    }

    #[test]
    fn argumen_mulai_ulang_dan_pembantu() {
        let x = a(&["--baki", "--tunggu-pid", "123", "--nyalakan", "web|db||node:D:\\a", "--pipe", "p-1"]);
        assert_eq!(x.tunggu_pid, Some(123));
        assert_eq!(x.nyalakan, vec!["web", "db", "node:D:\\a"]);
        assert_eq!(x.pipe.as_deref(), Some("p-1"));
        assert_eq!(a(&["--tunggu-pid", "bukan"]).tunggu_pid, None);

        assert_eq!(a(&["--sinkron-hosts"]).sinkron_hosts, Some(None));
        assert_eq!(a(&["--sinkron-hosts", r"C:\Phoron"]).sinkron_hosts, Some(Some(PathBuf::from(r"C:\Phoron"))));
        let y = a(&["--sinkron-hosts", "--akar", r"D:\x"]);
        assert_eq!(y.sinkron_hosts, Some(None));
        assert_eq!(y.akar, Some(PathBuf::from(r"D:\x")));
    }
}
