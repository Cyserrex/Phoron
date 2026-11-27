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

// Build release tanpa jendela konsol. Build debug (dipakai uji) tetap
// berkonsol supaya keluaran panik terlihat saat menelusuri masalah.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod baki;
mod nama;
mod pelaksana;
mod penyiar;
mod pipa;

use nama::{lebar, Nama};
use pelaksana::{Pelaksana, PelaksanaSementara};
use penyiar::Penyiar;
use phoron_inti::paths;
use std::path::PathBuf;
use std::sync::Arc;
use windows_sys::Win32::Foundation::{GetLastError, ERROR_ACCESS_DENIED, ERROR_ALREADY_EXISTS, HANDLE};
use windows_sys::Win32::System::Threading::{CreateEventW, CreateMutexW, WaitForSingleObject, INFINITE};
use windows_sys::Win32::Foundation::WAIT_OBJECT_0;

/// Kode keluar bila pipe tidak bisa dibuat (dipegang proses lain).
const KELUAR_PIPE_GAGAL: i32 = 2;
/// Kode keluar sesudah panik (berkas crash sudah ditulis).
const KELUAR_PANIK: i32 = 101;

#[derive(Debug, Default, PartialEq)]
struct Argumen {
    baki: bool,
    akar: Option<PathBuf>,
    akhiran: Option<String>,
}

fn urai_argumen(args: impl IntoIterator<Item = String>) -> Argumen {
    let mut a = Argumen::default();
    let mut it = args.into_iter();
    while let Some(s) = it.next() {
        match s.to_ascii_lowercase().as_str() {
            "--baki" => a.baki = true,
            "--akar" => a.akar = it.next().map(PathBuf::from),
            "--pipe-uji" => a.akhiran = it.next(),
            // Argumen tak dikenal diabaikan: pintasan lama atau versi UI
            // yang lebih baru tidak boleh membuat daemon menolak mulai.
            _ => {}
        }
    }
    a
}

fn pasang_panic_hook() {
    std::panic::set_hook(Box::new(|info| {
        let utas = std::thread::current().name().unwrap_or("?").to_string();
        let isi = format!(
            "phorond {} berhenti karena galat tak terduga\r\nwaktu: {}\r\nutas: {}\r\n{}\r\n\r\n{}\r\n",
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
        baki::hapus_ikon();
        // Keluar sendiri, bukan abort: abort memunculkan kotak dialog
        // Windows Error Reporting di depan pengguna.
        std::process::exit(KELUAR_PANIK);
    }));
}

/// `true` bila daemon ini pemilik mutex instans tunggal.
fn ambil_mutex(nama: &str) -> bool {
    let w = lebar(nama);
    let h = unsafe { CreateMutexW(std::ptr::null(), 1, w.as_ptr()) };
    let e = unsafe { GetLastError() };
    if h.is_null() {
        // ERROR_ACCESS_DENIED: mutexnya ada, milik daemon berhak lain.
        return e != ERROR_ACCESS_DENIED;
    }
    // Pegangan sengaja tidak ditutup: mutex hidup selama proses hidup.
    e != ERROR_ALREADY_EXISTS
}

/// Pengawas sinyal `Phoron.KeluarSekarang` (event manual-reset dari pemasang).
fn awasi_sinyal_keluar(nama: &str) {
    let w = lebar(nama);
    let h: HANDLE = unsafe { CreateEventW(std::ptr::null(), 1, 0, w.as_ptr()) };
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
    if let Some(a) = &arg.akar {
        paths::set_root(a);
    }
    let akhiran = arg
        .akhiran
        .clone()
        .or_else(|| std::env::var("PHORON_NAMA_UJI").ok())
        .unwrap_or_default();
    let nama = Nama::dengan_akhiran(&akhiran);

    if !ambil_mutex(&nama.mutex) {
        // Daemon sudah berjalan. Menjalankan phorond lagi (mis. dari pintasan)
        // artinya "tampilkan Phoron".
        if !arg.baki && !nama.uji() {
            baki::buka_ui();
        }
        return;
    }

    let penyiar = Penyiar::baru();
    // Titik pasang Engine Rust: ganti PelaksanaSementara di sini.
    let pelaksana: Arc<dyn Pelaksana> = Arc::new(PelaksanaSementara::baru(penyiar.clone()));

    if pipa::mulai(&nama.pipe, pelaksana.clone(), penyiar.clone()).is_err() {
        std::process::exit(KELUAR_PIPE_GAGAL);
    }
    awasi_sinyal_keluar(&nama.sinyal_keluar);
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
            Argumen { baki: true, akar: Some(PathBuf::from(r"D:\Phoron")), akhiran: Some("-u1".into()) }
        );
        assert_eq!(a(&["--BAKI"]).baki, true);
    }
}
