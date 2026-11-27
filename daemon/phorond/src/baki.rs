//! Ikon baki, jendela pesan tersembunyi, loop pesan utas utama, dan jalur
//! keluar tunggal daemon.
//!
//! Jendelanya jendela tingkat atas biasa yang tidak pernah ditampilkan, BUKAN
//! jendela khusus pesan (`HWND_MESSAGE`): jendela khusus pesan tidak menerima
//! siaran `TaskbarCreated`, padahal hanya dari situ daemon tahu Explorer baru
//! dimulai ulang dan ikonnya harus dipasang lagi. Jendela tingkat atas juga
//! menerima `WM_ENDSESSION` (log off/matikan komputer) dan `WM_CLOSE`
//! (`taskkill` tanpa `/f`), yang keduanya diarahkan ke jalur keluar rapi.
//!
//! Prosedur jendela membungkus isinya dengan `catch_unwind`: panik tidak boleh
//! menyeberang ke DispatchMessageW (itu abort tanpa ampun). Panik di sana
//! menempuh jalur keluar rapi dengan kode `KELUAR_PANIK`.

use crate::nama::{lebar, Nama};
use crate::pelaksana::{aman, tangani_aman, ItemMenu, Pelaksana};
use crate::penyiar::Penyiar;
use crate::siklus::{self, Alasan};
use phoron_inti::protokol::{Kabar, Perintah};
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, AtomicIsize, AtomicU32, Ordering};
use std::sync::{Arc, OnceLock};
use std::time::Duration;
use windows_sys::Win32::Foundation::{HWND, LPARAM, LRESULT, POINT, WPARAM};
use windows_sys::Win32::System::LibraryLoader::GetModuleHandleW;
use windows_sys::Win32::UI::Shell::{
    ExtractIconExW, Shell_NotifyIconW, NIF_ICON, NIF_MESSAGE, NIF_TIP, NIM_ADD, NIM_DELETE, NOTIFYICONDATAW,
};
use windows_sys::Win32::UI::WindowsAndMessaging::{
    AppendMenuW, ChangeWindowMessageFilterEx, CreatePopupMenu, CreateWindowExW, DefWindowProcW, DestroyMenu,
    DispatchMessageW, GetCursorPos, GetMessageW, LoadIconW, PostMessageW, RegisterClassW,
    RegisterWindowMessageW, SetForegroundWindow, SetMenuDefaultItem, TrackPopupMenu, TranslateMessage, HICON,
    HMENU, IDI_APPLICATION, MF_CHECKED, MF_GRAYED, MF_POPUP, MF_SEPARATOR, MF_STRING, MSG, MSGFLT_ALLOW,
    TPM_NONOTIFY, TPM_RETURNCMD, TPM_RIGHTBUTTON, WM_APP, WM_CLOSE, WM_CONTEXTMENU, WM_ENDSESSION,
    WM_LBUTTONDBLCLK, WM_NULL, WM_RBUTTONUP, WNDCLASSW, WS_OVERLAPPED,
};

/// Pesan balik dari ikon baki.
const WM_IKON: u32 = WM_APP + 1;
/// Permintaan keluar dari utas lain (pipe, sinyal pemasang).
const WM_KELUAR: u32 = WM_APP + 2;
/// Hanya uji: buat utas utama panik di dalam prosedur jendela.
const WM_UJI_PANIK: u32 = WM_APP + 3;

const ID_IKON: u32 = 1;
const ID_BUKA: usize = 1;
const ID_KELUAR: usize = 2;
/// Butir dari Pelaksana diberi id mulai dari sini, menurut urutan tampilnya.
const ID_PELAKSANA: usize = 100;

/// Batas tunggu klien menerima `Kabar::Pamit` sebelum proses keluar.
const BATAS_PAMIT: Duration = Duration::from_secs(2);

/// Yang dibutuhkan baki dari bagian lain daemon.
pub struct Bersama {
    pub pelaksana: Arc<dyn Pelaksana>,
    pub penyiar: Penyiar,
    pub nama: Nama,
}

static BERSAMA: OnceLock<Bersama> = OnceLock::new();
static JENDELA: AtomicIsize = AtomicIsize::new(0);
static IKON: AtomicIsize = AtomicIsize::new(0);
static TASKBAR_CREATED: AtomicU32 = AtomicU32::new(0);
static KELUAR_DIMINTA: AtomicBool = AtomicBool::new(false);
static SEDANG_KELUAR: AtomicBool = AtomicBool::new(false);
static IKON_TERPASANG: AtomicBool = AtomicBool::new(false);

/// Minta daemon keluar lewat jalur rapi. Aman dipanggil dari utas mana pun,
/// juga sebelum jendela baki ada (permintaannya diingat). Gerbang keluar
/// langsung ditutup: perintah baru tidak sampai lagi ke Pelaksana.
pub fn minta_keluar() {
    minta_keluar_karena(Alasan::Biasa);
}

pub fn minta_keluar_karena(alasan: Alasan) {
    siklus::tutup_gerbang(alasan);
    KELUAR_DIMINTA.store(true, Ordering::SeqCst);
    let h = JENDELA.load(Ordering::SeqCst);
    if h != 0 {
        unsafe { PostMessageW(h as HWND, WM_KELUAR, 0, 0) };
    }
}

/// Jalankan Phoron.exe dari folder yang sama dengan phorond.exe. Bila UI
/// sudah terbuka, instans keduanya sendiri yang memajukan jendela lalu keluar.
pub fn buka_ui() {
    let Some(folder) = std::env::current_exe().ok().and_then(|e| e.parent().map(PathBuf::from)) else {
        return;
    };
    let exe = folder.join("Phoron.exe");
    if exe.is_file() {
        let _ = std::process::Command::new(&exe).current_dir(&folder).spawn();
    }
}

/// Hapus ikon dari baki. Dipakai juga oleh panic hook, supaya tidak tersisa
/// ikon hantu yang baru hilang saat disentuh tetikus.
pub fn hapus_ikon() {
    if !IKON_TERPASANG.swap(false, Ordering::SeqCst) {
        return;
    }
    let mut d: NOTIFYICONDATAW = unsafe { std::mem::zeroed() };
    d.cbSize = std::mem::size_of::<NOTIFYICONDATAW>() as u32;
    d.hWnd = JENDELA.load(Ordering::SeqCst) as HWND;
    d.uID = ID_IKON;
    unsafe { Shell_NotifyIconW(NIM_DELETE, &d) };
}

/// Hanya uji (`PelaksanaUji`): minta utas utama panik di prosedur jendela.
pub fn uji_panik_utama() {
    let h = JENDELA.load(Ordering::SeqCst);
    if h != 0 {
        unsafe { PostMessageW(h as HWND, WM_UJI_PANIK, 0, 0) };
    }
}

/// Dipakai `main` sesudah panik di utas utama di luar prosedur jendela:
/// tempuh jalur keluar rapi bila baki sudah berjalan, lalu keluar dengan
/// `KELUAR_PANIK`.
pub fn keluar_sesudah_panik() -> ! {
    siklus::tutup_gerbang(Alasan::Panik);
    if BERSAMA.get().is_some() {
        keluar_rapi();
    }
    hapus_ikon();
    std::process::exit(siklus::KELUAR_PANIK);
}

fn bersama() -> &'static Bersama {
    BERSAMA.get().expect("baki::jalankan belum dipanggil")
}

fn muat_ikon() -> HICON {
    let mut kecil: HICON = std::ptr::null_mut();
    if let Some(exe) = std::env::current_exe().ok().and_then(|e| e.parent().map(|p| p.join("Phoron.exe"))) {
        if exe.is_file() {
            let w = lebar(&exe.to_string_lossy());
            unsafe { ExtractIconExW(w.as_ptr(), 0, std::ptr::null_mut(), &mut kecil, 1) };
        }
    }
    if kecil.is_null() {
        kecil = unsafe { LoadIconW(std::ptr::null_mut(), IDI_APPLICATION) };
    }
    kecil
}

fn pasang_ikon(hwnd: HWND) {
    let b = bersama();
    let mut d: NOTIFYICONDATAW = unsafe { std::mem::zeroed() };
    d.cbSize = std::mem::size_of::<NOTIFYICONDATAW>() as u32;
    d.hWnd = hwnd;
    d.uID = ID_IKON;
    d.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
    d.uCallbackMessage = WM_IKON;
    d.hIcon = IKON.load(Ordering::SeqCst) as HICON;
    let tip = if b.nama.uji() { format!("Phoron (uji{})", b.nama.akhiran) } else { "Phoron".to_string() };
    for (i, c) in tip.encode_utf16().take(d.szTip.len() - 1).enumerate() {
        d.szTip[i] = c;
    }
    // Bisa gagal bila Explorer belum siap (autostart sangat awal); ikon
    // dipasang lagi saat TaskbarCreated datang.
    if unsafe { Shell_NotifyIconW(NIM_ADD, &d) } != 0 {
        IKON_TERPASANG.store(true, Ordering::SeqCst);
    }
}

/// Jalur keluar tunggal: menu Keluar, perintah `keluar`, sinyal pemasang,
/// WM_CLOSE, akhir sesi Windows, mulai ulang sebagai Administrator, dan panik
/// di utas utama semuanya berakhir di sini. Tidak kembali, KECUALI bila jalur
/// keluar sudah berjalan: itu terjadi bila `sebelum_keluar` memompa pesan
/// (SendMessage lintas utas, COM STA, dialog) dan WM_KELUAR/WM_CLOSE yang
/// masih antre di-dispatch lagi di utas utama. Pemanggil lalu cukup kembali
/// ke loop pesan; jalur yang pertama yang akan mengakhiri proses.
fn keluar_rapi() {
    if SEDANG_KELUAR.swap(true, Ordering::SeqCst) {
        return;
    }
    siklus::tutup_gerbang(Alasan::Biasa);
    let b = bersama();
    if aman(|| b.pelaksana.sebelum_keluar()).is_none() {
        siklus::tutup_gerbang(Alasan::Panik);
    }
    // Saat digantikan daemon ber-Administrator, UI tidak boleh menerima Pamit
    // (UI ikut keluar karenanya); sambungan putus biasa membuatnya menyambung
    // ulang ke daemon yang baru.
    if !siklus::tanpa_pamit() {
        b.penyiar.siar(&Kabar::Pamit);
    }
    b.penyiar.tutup_semua(BATAS_PAMIT);
    hapus_ikon();
    std::process::exit(siklus::kode_keluar());
}

fn tambah_butir(menu: HMENU, butir: &[ItemMenu], perintah: &mut Vec<Perintah>) {
    for b in butir {
        match b {
            ItemMenu::Pemisah => unsafe {
                AppendMenuW(menu, MF_SEPARATOR, 0, std::ptr::null());
            },
            ItemMenu::Perintah { teks, perintah: p, aktif, tercentang } => {
                let mut f = MF_STRING;
                if !*aktif {
                    f |= MF_GRAYED;
                }
                if *tercentang {
                    f |= MF_CHECKED;
                }
                let id = ID_PELAKSANA + perintah.len();
                perintah.push(p.clone());
                let w = lebar(teks);
                unsafe { AppendMenuW(menu, f, id, w.as_ptr()) };
            }
            ItemMenu::Sub { teks, isi } => {
                let sub = unsafe { CreatePopupMenu() };
                if sub.is_null() {
                    continue;
                }
                tambah_butir(sub, isi, perintah);
                let w = lebar(teks);
                // Sub-menu ikut dihancurkan bersama menu induknya.
                unsafe { AppendMenuW(menu, MF_POPUP, sub as usize, w.as_ptr()) };
            }
        }
    }
}

/// Bangun menu dari nol tiap kali dibuka, tampilkan, lalu jalankan pilihannya.
fn tampilkan_menu(hwnd: HWND) {
    let b = bersama();
    let menu = unsafe { CreatePopupMenu() };
    if menu.is_null() {
        return;
    }
    let mut perintah = Vec::new();
    let buka = lebar("Buka Phoron");
    let keluar = lebar("Keluar");
    unsafe {
        AppendMenuW(menu, MF_STRING, ID_BUKA, buka.as_ptr());
        SetMenuDefaultItem(menu, ID_BUKA as u32, 0);
        AppendMenuW(menu, MF_SEPARATOR, 0, std::ptr::null());
    }
    let butir = aman(|| b.pelaksana.menu_baki()).unwrap_or_default();
    if !butir.is_empty() {
        tambah_butir(menu, &butir, &mut perintah);
        unsafe { AppendMenuW(menu, MF_SEPARATOR, 0, std::ptr::null()) };
    }
    unsafe { AppendMenuW(menu, MF_STRING, ID_KELUAR, keluar.as_ptr()) };

    let mut titik = POINT { x: 0, y: 0 };
    unsafe { GetCursorPos(&mut titik) };
    // Tanpa SetForegroundWindow menu tidak tertutup saat mengeklik di luar;
    // WM_NULL sesudahnya adalah resep resmi untuk masalah yang sama.
    let pilihan = unsafe {
        SetForegroundWindow(hwnd);
        let p = TrackPopupMenu(
            menu,
            TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY,
            titik.x,
            titik.y,
            0,
            hwnd,
            std::ptr::null(),
        );
        PostMessageW(hwnd, WM_NULL, 0, 0);
        DestroyMenu(menu);
        p as usize
    };
    match pilihan {
        0 => {}
        ID_BUKA => buka_ui(),
        ID_KELUAR => keluar_rapi(),
        // Sesudah gerbang keluar ditutup butir menu diabaikan (menu bisa saja
        // sedang terbuka saat sinyal keluar datang).
        id if id >= ID_PELAKSANA && !siklus::gerbang_tertutup() => {
            if let Some(p) = perintah.get(id - ID_PELAKSANA).cloned() {
                // Menyalakan layanan bisa makan detik; loop pesan (dan ikon
                // baki) tidak boleh beku selama itu.
                let pelaksana = b.pelaksana.clone();
                let _ = std::thread::Builder::new().name("menu-baki".into()).spawn(move || {
                    let _ = tangani_aman(&*pelaksana, p);
                });
            }
        }
        _ => {}
    }
}

unsafe extern "system" fn prosedur(hwnd: HWND, msg: u32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    match std::panic::catch_unwind(|| prosedur_isi(hwnd, msg, wparam, lparam)) {
        Ok(r) => r,
        Err(_) => {
            // Berkas crash sudah ditulis panic hook.
            siklus::tutup_gerbang(Alasan::Panik);
            if SEDANG_KELUAR.load(Ordering::SeqCst) {
                // Panik DI DALAM jalur keluar (mis. saat menyiarkan Pamit):
                // jalur itu tidak akan selesai lagi, jadi akhiri di sini.
                hapus_ikon();
                std::process::exit(siklus::KELUAR_PANIK);
            }
            keluar_rapi();
            0
        }
    }
}

fn prosedur_isi(hwnd: HWND, msg: u32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    match msg {
        WM_IKON => {
            match lparam as u32 {
                WM_LBUTTONDBLCLK => buka_ui(),
                WM_RBUTTONUP | WM_CONTEXTMENU => tampilkan_menu(hwnd),
                _ => {}
            }
            0
        }
        WM_KELUAR | WM_CLOSE => {
            keluar_rapi();
            0
        }
        WM_ENDSESSION => {
            if wparam != 0 {
                keluar_rapi();
            }
            0
        }
        WM_UJI_PANIK if bersama().nama.uji() => panic!("panik uji di utas utama"),
        m if m != 0 && m == TASKBAR_CREATED.load(Ordering::SeqCst) => {
            // Explorer dimulai ulang: semua ikon baki lenyap bersamanya.
            IKON_TERPASANG.store(false, Ordering::SeqCst);
            pasang_ikon(hwnd);
            0
        }
        _ => unsafe { DefWindowProcW(hwnd, msg, wparam, lparam) },
    }
}

/// Pasang ikon dan jalankan loop pesan di utas pemanggil (utas utama) sampai
/// daemon keluar. `buka` = langsung jalankan UI sesudah ikon terpasang.
pub fn jalankan(b: Bersama, buka: bool) -> ! {
    if BERSAMA.set(b).is_err() {
        panic!("baki::jalankan dipanggil dua kali");
    }
    let hinst = unsafe { GetModuleHandleW(std::ptr::null()) };
    let kelas = lebar("PhoronDaemonBaki");
    let judul = lebar("Phoron Daemon");
    let wc = WNDCLASSW {
        style: 0,
        lpfnWndProc: Some(prosedur),
        cbClsExtra: 0,
        cbWndExtra: 0,
        hInstance: hinst,
        hIcon: std::ptr::null_mut(),
        hCursor: std::ptr::null_mut(),
        hbrBackground: std::ptr::null_mut(),
        lpszMenuName: std::ptr::null(),
        lpszClassName: kelas.as_ptr(),
    };
    unsafe { RegisterClassW(&wc) };
    let tc = lebar("TaskbarCreated");
    TASKBAR_CREATED.store(unsafe { RegisterWindowMessageW(tc.as_ptr()) }, Ordering::SeqCst);
    let hwnd = unsafe {
        CreateWindowExW(
            0,
            kelas.as_ptr(),
            judul.as_ptr(),
            WS_OVERLAPPED,
            0,
            0,
            0,
            0,
            std::ptr::null_mut(),
            std::ptr::null_mut(),
            hinst,
            std::ptr::null(),
        )
    };
    if hwnd.is_null() {
        panic!("jendela baki tidak bisa dibuat");
    }
    // Daemon yang dinaikkan haknya tetap harus menerima TaskbarCreated dari
    // Explorer yang berhak biasa (UIPI menyaringnya tanpa ini).
    let m = TASKBAR_CREATED.load(Ordering::SeqCst);
    if m != 0 {
        unsafe { ChangeWindowMessageFilterEx(hwnd, m, MSGFLT_ALLOW, std::ptr::null_mut()) };
    }
    JENDELA.store(hwnd as isize, Ordering::SeqCst);
    IKON.store(muat_ikon() as isize, Ordering::SeqCst);
    pasang_ikon(hwnd);
    if KELUAR_DIMINTA.load(Ordering::SeqCst) {
        unsafe { PostMessageW(hwnd, WM_KELUAR, 0, 0) };
    }
    if buka {
        buka_ui();
    }

    let mut msg: MSG = unsafe { std::mem::zeroed() };
    loop {
        let r = unsafe { GetMessageW(&mut msg, std::ptr::null_mut(), 0, 0) };
        if r <= 0 {
            break;
        }
        unsafe {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }
    keluar_rapi();
    std::process::exit(siklus::kode_keluar());
}
