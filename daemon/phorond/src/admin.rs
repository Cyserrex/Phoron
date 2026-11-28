//! `MulaiUlangAdmin`: daemon menjalankan penggantinya sendiri dengan hak
//! Administrator, lalu keluar.
//!
//! Urutannya:
//! 1. Pelaksana ditanya layanan apa yang sedang hidup (`layanan_hidup`).
//! 2. phorond.exe yang sama dijalankan lewat `ShellExecuteExW` verb "runas"
//!    (dialog UAC) dengan `--baki --tunggu-pid <pid ini> --nyalakan <daftar>`,
//!    ditambah `--akar`, `--pipe-uji`, dan `--pipe` bila daemon ini memakainya.
//!    Lingkungan TIDAK diturunkan ke proses yang dinaikkan haknya (AppInfo
//!    membuat lingkungan baru), jadi nama uji dari env harus lewat argumen.
//! 3. UAC dibatalkan: jawab `Galat::Gagal` "Hak Administrator tidak
//!    diberikan.", daemon ini jalan terus seperti biasa.
//! 4. Berhasil: jawab ok, lalu jalur keluar rapi TANPA `Kabar::Pamit`
//!    (`siklus::Alasan::MulaiUlang`); `sebelum_keluar` mematikan layanan.
//! 5. Daemon baru menunggu proses lama berakhir (maks 30 detik) sebelum
//!    mengambil mutex, lalu memanggil `Pelaksana::nyalakan_awal(daftar)`
//!    sesudah pipenya siap. UI menyambung ulang sendiri.

use crate::nama::lebar;
use crate::pelaksana::{aman, Pelaksana};
use phoron_inti::protokol::Pesan;
use std::path::PathBuf;
use std::sync::OnceLock;
use std::time::Duration;
use windows_sys::Win32::Foundation::{CloseHandle, GetLastError, ERROR_CANCELLED, WAIT_TIMEOUT};
use windows_sys::Win32::System::Com::{CoInitializeEx, CoUninitialize, COINIT_APARTMENTTHREADED, COINIT_DISABLE_OLE1DDE};
use windows_sys::Win32::System::Threading::{OpenProcess, WaitForSingleObject, PROCESS_SYNCHRONIZE};
use windows_sys::Win32::UI::Shell::{
    ShellExecuteExW, SEE_MASK_FLAG_NO_UI, SEE_MASK_NOASYNC, SEE_MASK_NOCLOSEPROCESS, SHELLEXECUTEINFOW,
};

/// Batas tunggu daemon baru atas daemon lama.
///
/// Harus lebih lama dari penutupan daemon lama yang terburuk: `Engine::tutup`
/// menunggu start/ganti profil yang berjalan sampai `BATAS_TUTUP` (60 dtk),
/// lalu MySQL dimatikan rapi (mysqladmin 15 dtk + tunggu keluar 12 dtk). Dulu
/// 30 dtk: restart admin yang ditekan selagi MySQL masih menyala membuat
/// daemon baru menemukan mutex masih dipegang, lalu keluar diam-diam - tidak
/// ada daemon sama sekali dan layanan yang tadi hidup tidak dinyalakan lagi.
pub const BATAS_TUNGGU_PID: Duration =
    Duration::from_secs(phoron_inti::engine::BATAS_TUTUP.as_secs() + 60);

pub const PESAN_UAC_BATAL: &str = "Hak Administrator tidak diberikan.";
pub const PESAN_RUNAS_GAGAL: &str = "Phoron tidak bisa dijalankan ulang sebagai Administrator (kode {0}).";

/// Bagian argumen daemon ini yang harus ikut ke penggantinya.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct ArgUlang {
    pub akar: Option<PathBuf>,
    /// Akhiran uji efektif (dari argumen atau env); kosong = nama sungguhan.
    pub akhiran: String,
    /// Nama pipe pengganti (dari `--pipe`/`PHORON_PIPE`); kosong = bawaan.
    pub pipe: String,
}

static ARG_ULANG: OnceLock<ArgUlang> = OnceLock::new();

pub fn simpan_argumen(a: ArgUlang) {
    let _ = ARG_ULANG.set(a);
}

/// Kutip satu argumen menurut aturan CommandLineToArgvW: garis miring
/// terbalik hanya istimewa tepat sebelum tanda kutip.
pub fn kutip(s: &str) -> String {
    if !s.is_empty() && !s.contains([' ', '\t', '"']) {
        return s.to_string();
    }
    let mut hasil = String::from("\"");
    let mut miring = 0usize;
    for c in s.chars() {
        match c {
            '\\' => miring += 1,
            '"' => {
                hasil.extend(std::iter::repeat_n('\\', miring * 2 + 1));
                hasil.push('"');
                miring = 0;
            }
            lain => {
                hasil.extend(std::iter::repeat_n('\\', miring));
                hasil.push(lain);
                miring = 0;
            }
        }
    }
    hasil.extend(std::iter::repeat_n('\\', miring * 2));
    hasil.push('"');
    hasil
}

/// Baris parameter untuk daemon pengganti.
pub fn susun_parameter(pid: u32, daftar: &[String], a: &ArgUlang) -> String {
    let mut bagian = vec!["--baki".to_string(), "--tunggu-pid".into(), pid.to_string()];
    if !daftar.is_empty() {
        bagian.push("--nyalakan".into());
        bagian.push(kutip(&daftar.join("|")));
    }
    if let Some(akar) = &a.akar {
        bagian.push("--akar".into());
        bagian.push(kutip(&akar.to_string_lossy()));
    }
    if !a.akhiran.is_empty() {
        bagian.push("--pipe-uji".into());
        bagian.push(kutip(&a.akhiran));
    }
    if !a.pipe.is_empty() {
        bagian.push("--pipe".into());
        bagian.push(kutip(&a.pipe));
    }
    bagian.join(" ")
}

/// Jalankan pengganti ber-Administrator. `Ok` = proses baru sudah berjalan;
/// pemanggil lalu menjawab ok dan menempuh jalur keluar.
pub fn mulai_ulang(p: &dyn Pelaksana) -> Result<(), Pesan> {
    let daftar = aman(|| p.layanan_hidup()).unwrap_or_default();
    let exe = std::env::current_exe().map_err(|_| Pesan::dengan(PESAN_RUNAS_GAGAL, &["exe"]))?;
    let arg = ARG_ULANG.get().cloned().unwrap_or_default();
    let param = susun_parameter(std::process::id(), &daftar, &arg);
    let folder = exe.parent().map(PathBuf::from).unwrap_or_default();
    // ShellExecuteEx sebaiknya dipanggil dari utas STA (bisa memuat ekstensi
    // shell); utas klien pipe bukan STA, jadi pakai utas sendiri.
    let hasil = std::thread::Builder::new()
        .name("runas".into())
        .spawn(move || jalankan_runas(&exe, &param, &folder))
        .map_err(|_| Pesan::dengan(PESAN_RUNAS_GAGAL, &["utas"]))?
        .join()
        .unwrap_or(Err(0));
    match hasil {
        Ok(()) => Ok(()),
        Err(ERROR_CANCELLED) => Err(Pesan::baru(PESAN_UAC_BATAL)),
        Err(e) => Err(Pesan::dengan(PESAN_RUNAS_GAGAL, &[&e.to_string()])),
    }
}

fn jalankan_runas(exe: &std::path::Path, param: &str, folder: &std::path::Path) -> Result<(), u32> {
    let verb = lebar("runas");
    let file = lebar(&exe.to_string_lossy());
    let param = lebar(param);
    let folder = lebar(&folder.to_string_lossy());
    unsafe {
        let com = CoInitializeEx(std::ptr::null(), (COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE) as u32);
        let mut info: SHELLEXECUTEINFOW = std::mem::zeroed();
        info.cbSize = std::mem::size_of::<SHELLEXECUTEINFOW>() as u32;
        info.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_NOASYNC | SEE_MASK_FLAG_NO_UI;
        info.lpVerb = verb.as_ptr();
        info.lpFile = file.as_ptr();
        info.lpParameters = param.as_ptr();
        info.lpDirectory = folder.as_ptr();
        info.nShow = 0; // SW_HIDE: daemon tidak punya jendela yang tampil.
        let ok = ShellExecuteExW(&mut info);
        let e = GetLastError();
        if !info.hProcess.is_null() {
            CloseHandle(info.hProcess);
        }
        if com >= 0 {
            CoUninitialize();
        }
        if ok != 0 { Ok(()) } else { Err(e) }
    }
}

/// Tunggu proses `pid` berakhir, paling lama `batas`. `true` bila sudah
/// berakhir (atau memang tidak ada).
pub fn tunggu_pid(pid: u32, batas: Duration) -> bool {
    let h = unsafe { OpenProcess(PROCESS_SYNCHRONIZE, 0, pid) };
    if h.is_null() {
        // Sudah berakhir (atau tidak bisa dibuka): jangan menunggu buta.
        return true;
    }
    let ms = batas.as_millis().min(u32::MAX as u128 - 1) as u32;
    let r = unsafe { WaitForSingleObject(h, ms) };
    unsafe { CloseHandle(h) };
    r != WAIT_TIMEOUT
}

/// Pecah baris perintah dengan aturan Windows sungguhan (untuk uji).
#[cfg(test)]
fn pecah(baris: &str) -> Vec<String> {
    use crate::keamanan::dari_pwstr;
    use windows_sys::Win32::Foundation::LocalFree;
    use windows_sys::Win32::UI::Shell::CommandLineToArgvW;
    // Elemen pertama dianggap nama program; beri satu supaya aturannya sama.
    let w = lebar(&format!("phorond.exe {baris}"));
    let mut n = 0i32;
    let p = unsafe { CommandLineToArgvW(w.as_ptr(), &mut n) };
    assert!(!p.is_null());
    let v = (1..n as usize).map(|i| unsafe { dari_pwstr(*p.add(i)) }).collect();
    unsafe { LocalFree(p.cast()) };
    v
}

#[cfg(test)]
mod uji {
    use super::*;

    #[test]
    fn kutip_bolak_balik() {
        for s in [r"D:\Phoron", r"C:\Program Files\Phoron\", "a\"b", "", r"x\\", "web|db"] {
            assert_eq!(pecah(&kutip(s)), vec![s.to_string()], "{s}");
        }
    }

    #[test]
    fn parameter_pengganti_terurai_kembali() {
        let a = ArgUlang {
            akar: Some(PathBuf::from(r"C:\Program Files\Phoron\")),
            akhiran: "-uji 1".into(),
            pipe: "phoron-uji-x".into(),
        };
        let daftar = vec!["web".to_string(), "db".into(), r"node:D:\web\toko saya".into()];
        let param = susun_parameter(4242, &daftar, &a);
        let arg = crate::urai_argumen(pecah(&param));
        assert!(arg.baki);
        assert_eq!(arg.tunggu_pid, Some(4242));
        assert_eq!(arg.nyalakan, daftar);
        assert_eq!(arg.akar, a.akar);
        assert_eq!(arg.akhiran.as_deref(), Some("-uji 1"));
        assert_eq!(arg.pipe.as_deref(), Some("phoron-uji-x"));

        let polos = susun_parameter(7, &[], &ArgUlang::default());
        assert_eq!(polos, "--baki --tunggu-pid 7");
    }

    #[test]
    fn tunggu_pid_yang_sudah_berakhir() {
        let mut anak = std::process::Command::new("cmd").args(["/c", "exit"]).spawn().unwrap();
        let pid = anak.id();
        anak.wait().unwrap();
        assert!(tunggu_pid(pid, Duration::from_secs(5)));
        // Proses ini sendiri tidak berakhir dalam 50 ms.
        assert!(!tunggu_pid(std::process::id(), Duration::from_millis(50)));
    }
}
