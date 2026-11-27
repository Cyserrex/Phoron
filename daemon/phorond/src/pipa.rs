//! Server named pipe `\\.\pipe\phoron`: banyak klien sekaligus, satu baris
//! JSON per pesan (lihat `phoron_inti::protokol` dan `daemon/PROTOKOL.md`).
//!
//! Tiap klien dilayani dua utas: pembaca (mengurai permintaan dan memanggil
//! Pelaksana, berurutan) dan penulis (menguras antrean jawaban + kabar).
//! Pipe dibuka OVERLAPPED. Bukan demi asinkron - justru karena pada pegangan
//! sinkron Windows menjalankan I/O satu per satu per objek berkas: ReadFile
//! yang menunggu permintaan berikutnya akan menahan WriteFile kabar di utas
//! lain sampai klien mengirim sesuatu. Dengan OVERLAPPED masing-masing utas
//! menunggu I/O-nya sendiri.
//!
//! Keamanan: deskriptor keamanan bawaan (argumen NULL) - hanya pengguna yang
//! sama, SYSTEM, dan Administrators yang bisa membuka pipe; klien jarak jauh
//! ditolak (`PIPE_REJECT_REMOTE_CLIENTS`).

use crate::nama::lebar;
use crate::pelaksana::Pelaksana;
use crate::penyiar::{Keluaran, Penyiar, PANJANG_ANTREAN};
use phoron_inti::protokol::{
    baris, urai, Galat, IsiJawaban, Jawaban, Perintah, Permintaan, Pesan, BATAS_BARIS, VERSI_PROTOKOL,
};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::mpsc::{sync_channel, Receiver, SyncSender};
use std::sync::{Arc, Condvar, Mutex};
use std::time::{Duration, Instant};
use windows_sys::Win32::Foundation::{
    CloseHandle, GetLastError, ERROR_IO_PENDING, ERROR_PIPE_CONNECTED, HANDLE, INVALID_HANDLE_VALUE,
    WAIT_TIMEOUT,
};
use windows_sys::Win32::Storage::FileSystem::{
    ReadFile, WriteFile, FILE_FLAG_FIRST_PIPE_INSTANCE, FILE_FLAG_OVERLAPPED, PIPE_ACCESS_DUPLEX,
};
use windows_sys::Win32::System::Pipes::{
    ConnectNamedPipe, CreateNamedPipeW, PIPE_READMODE_BYTE, PIPE_REJECT_REMOTE_CLIENTS, PIPE_TYPE_BYTE,
    PIPE_UNLIMITED_INSTANCES, PIPE_WAIT,
};
use windows_sys::Win32::System::Threading::{CreateEventW, WaitForSingleObject};
use windows_sys::Win32::System::IO::{CancelIoEx, GetOverlappedResult, OVERLAPPED};

/// Ukuran penyangga pipe di kernel, per arah.
const PENYANGGA: u32 = 64 * 1024;
/// Satu WriteFile yang tidak selesai selama ini berarti klien berhenti
/// membaca; klien itu diputus supaya utas penulisnya tidak tertahan selamanya.
const BATAS_TULIS: Duration = Duration::from_secs(5);

/// Satu ujung pipe milik server untuk satu klien.
pub(crate) struct Pipa {
    h: isize,
    penulis_selesai: Mutex<bool>,
    cv: Condvar,
}

impl Pipa {
    fn h(&self) -> HANDLE {
        self.h as HANDLE
    }

    /// Batalkan semua I/O yang sedang menunggu di pipe ini (dari utas mana
    /// pun): pembaca dan penulis klien ini lalu berhenti sendiri.
    pub(crate) fn putus(&self) {
        unsafe { CancelIoEx(self.h(), std::ptr::null()) };
    }

    fn tandai_penulis_selesai(&self) {
        *self.penulis_selesai.lock().unwrap_or_else(|e| e.into_inner()) = true;
        self.cv.notify_all();
    }

    /// Tunggu utas penulis selesai sampai `sampai`. `true` bila selesai.
    pub(crate) fn tunggu_penulis(&self, sampai: Instant) -> bool {
        let mut selesai = self.penulis_selesai.lock().unwrap_or_else(|e| e.into_inner());
        while !*selesai {
            let sisa = sampai.saturating_duration_since(Instant::now());
            if sisa.is_zero() {
                return false;
            }
            selesai = self.cv.wait_timeout(selesai, sisa).unwrap_or_else(|e| e.into_inner()).0;
        }
        true
    }
}

impl Drop for Pipa {
    fn drop(&mut self) {
        // Sengaja tanpa DisconnectNamedPipe: itu membuang data yang belum
        // dibaca klien (mis. jawaban `protokol_beda` atau `pamit` terakhir).
        // CloseHandle membiarkan klien membaca sisanya dulu.
        unsafe { CloseHandle(self.h()) };
    }
}

/// Event manual-reset untuk satu OVERLAPPED.
struct Acara(HANDLE);

impl Acara {
    fn baru() -> Option<Acara> {
        let h = unsafe { CreateEventW(std::ptr::null(), 1, 0, std::ptr::null()) };
        if h.is_null() { None } else { Some(Acara(h)) }
    }
}

impl Drop for Acara {
    fn drop(&mut self) {
        unsafe { CloseHandle(self.0) };
    }
}

/// Tuntaskan satu operasi overlapped yang baru dimulai. `mulai` = nilai
/// kembali ReadFile/WriteFile/ConnectNamedPipe. `Err` berisi kode galat Win32
/// (`WAIT_TIMEOUT` bila melewati `batas`).
fn tuntaskan(h: HANDLE, ov: &mut OVERLAPPED, mulai: i32, batas: Option<Duration>) -> Result<u32, u32> {
    if mulai == 0 {
        let e = unsafe { GetLastError() };
        if e != ERROR_IO_PENDING {
            return Err(e);
        }
    }
    if let Some(b) = batas {
        let ms = b.as_millis().min(u32::MAX as u128 - 1) as u32;
        if unsafe { WaitForSingleObject(ov.hEvent, ms) } == WAIT_TIMEOUT {
            unsafe { CancelIoEx(h, ov) };
            let mut n = 0u32;
            unsafe { GetOverlappedResult(h, ov, &mut n, 1) };
            return Err(WAIT_TIMEOUT);
        }
    }
    let mut n = 0u32;
    if unsafe { GetOverlappedResult(h, ov, &mut n, 1) } == 0 {
        return Err(unsafe { GetLastError() });
    }
    Ok(n)
}

fn ov_baru(acara: &Acara) -> OVERLAPPED {
    let mut ov: OVERLAPPED = unsafe { std::mem::zeroed() };
    ov.hEvent = acara.0;
    ov
}

fn buat_instans(nama: &[u16], pertama: bool) -> Result<HANDLE, u32> {
    let mut mode = PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED;
    if pertama {
        // Bila pipe bernama sama sudah ada (milik proses lain), gagal di sini
        // dan bukan diam-diam menjadi instans kesekian milik orang lain.
        mode |= FILE_FLAG_FIRST_PIPE_INSTANCE;
    }
    let h = unsafe {
        CreateNamedPipeW(
            nama.as_ptr(),
            mode,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
            PIPE_UNLIMITED_INSTANCES,
            PENYANGGA,
            PENYANGGA,
            0,
            std::ptr::null(),
        )
    };
    if h == INVALID_HANDLE_VALUE { Err(unsafe { GetLastError() }) } else { Ok(h) }
}

/// Mulai server. Instans pertama dibuat di utas pemanggil supaya kegagalan
/// (pipe sudah dipegang proses lain) langsung terlapor; menerima sambungan
/// berjalan di utas latar sampai proses keluar.
pub fn mulai(nama_pipe: &str, pelaksana: Arc<dyn Pelaksana>, penyiar: Penyiar) -> Result<(), u32> {
    let nama = lebar(nama_pipe);
    let pertama = buat_instans(&nama, true)?;
    let pertama = pertama as isize;
    std::thread::Builder::new()
        .name("pipa-terima".into())
        .spawn(move || terima_terus(nama, pertama as HANDLE, pelaksana, penyiar))
        .map_err(|_| 0u32)?;
    Ok(())
}

fn terima_terus(nama: Vec<u16>, mut h: HANDLE, pelaksana: Arc<dyn Pelaksana>, penyiar: Penyiar) {
    static NOMOR: AtomicU64 = AtomicU64::new(1);
    let acara = match Acara::baru() {
        Some(a) => a,
        None => return,
    };
    loop {
        let mut ov = ov_baru(&acara);
        let mulai = unsafe { ConnectNamedPipe(h, &mut ov) };
        let hasil = match tuntaskan(h, &mut ov, mulai, None) {
            Ok(_) => true,
            // Klien sudah tersambung di antara CreateNamedPipe dan Connect.
            Err(e) => e == ERROR_PIPE_CONNECTED,
        };
        if hasil {
            let pipa = Arc::new(Pipa { h: h as isize, penulis_selesai: Mutex::new(false), cv: Condvar::new() });
            let id = NOMOR.fetch_add(1, Ordering::Relaxed);
            layani(id, pipa, pelaksana.clone(), penyiar.clone());
        } else {
            unsafe { CloseHandle(h) };
        }
        // Instans berikutnya. Bila gagal (sumber daya habis), coba lagi pelan.
        h = loop {
            match buat_instans(&nama, false) {
                Ok(b) => break b,
                Err(_) => std::thread::sleep(Duration::from_secs(1)),
            }
        };
    }
}

fn layani(id: u64, pipa: Arc<Pipa>, pelaksana: Arc<dyn Pelaksana>, penyiar: Penyiar) {
    let (tx, rx) = sync_channel::<Keluaran>(PANJANG_ANTREAN);
    let p_tulis = pipa.clone();
    let ok_tulis = std::thread::Builder::new()
        .name(format!("pipa-tulis-{id}"))
        .stack_size(64 * 1024)
        .spawn(move || utas_penulis(p_tulis, rx));
    if ok_tulis.is_err() {
        return;
    }
    let _ = std::thread::Builder::new()
        .name(format!("pipa-baca-{id}"))
        .spawn(move || {
            utas_pembaca(id, &pipa, &tx, &*pelaksana, &penyiar);
            penyiar.lepas(id);
            // Penulis menuntaskan sisa antrean lalu berhenti saat semua
            // pengirim (tx di sini dan di Penyiar) sudah dilepas.
        });
}

fn utas_penulis(pipa: Arc<Pipa>, rx: Receiver<Keluaran>) {
    if let Some(acara) = Acara::baru() {
        while let Ok(k) = rx.recv() {
            let teks = match k {
                Keluaran::Baris(t) => t,
                Keluaran::Akhir => break,
            };
            if !tulis_semua(&pipa, &acara, teks.as_bytes()) {
                pipa.putus();
                break;
            }
        }
    }
    pipa.tandai_penulis_selesai();
}

fn tulis_semua(pipa: &Pipa, acara: &Acara, mut data: &[u8]) -> bool {
    while !data.is_empty() {
        let mut ov = ov_baru(acara);
        let n = data.len().min(u32::MAX as usize) as u32;
        let mulai = unsafe { WriteFile(pipa.h(), data.as_ptr(), n, std::ptr::null_mut(), &mut ov) };
        match tuntaskan(pipa.h(), &mut ov, mulai, Some(BATAS_TULIS)) {
            Ok(0) | Err(_) => return false,
            Ok(t) => data = &data[t as usize..],
        }
    }
    true
}

/// Lanjut melayani atau putuskan klien.
enum Arah {
    Lanjut,
    Putus,
}

fn utas_pembaca(id: u64, pipa: &Arc<Pipa>, tx: &SyncSender<Keluaran>, pelaksana: &dyn Pelaksana, penyiar: &Penyiar) {
    let acara = match Acara::baru() {
        Some(a) => a,
        None => return,
    };
    let mut keadaan = KeadaanKlien { id, sudah_halo: false };
    let mut tampung: Vec<u8> = Vec::new();
    let mut potong = vec![0u8; 16 * 1024];
    // Posisi yang sudah diperiksa tidak berisi '\n'; baris panjang yang
    // datang sepotong-sepotong tidak dipindai ulang dari awal.
    let mut periksa_dari = 0usize;
    loop {
        let mut ov = ov_baru(&acara);
        let mulai = unsafe {
            ReadFile(pipa.h(), potong.as_mut_ptr(), potong.len() as u32, std::ptr::null_mut(), &mut ov)
        };
        let n = match tuntaskan(pipa.h(), &mut ov, mulai, None) {
            Ok(0) | Err(_) => return,
            Ok(n) => n as usize,
        };
        tampung.extend_from_slice(&potong[..n]);
        while let Some(pos) = tampung[periksa_dari..].iter().position(|b| *b == b'\n') {
            let akhir = periksa_dari + pos;
            let arah = proses_baris(&tampung[..akhir], &mut keadaan, tx, pelaksana, penyiar, pipa);
            tampung.drain(..=akhir);
            periksa_dari = 0;
            if let Arah::Putus = arah {
                return;
            }
        }
        periksa_dari = tampung.len();
        if tampung.len() > BATAS_BARIS {
            let g = Galat::TidakTerbaca { sebab: format!("baris melebihi {} byte", BATAS_BARIS) };
            kirim(tx, 0, IsiJawaban::Galat { galat: g });
            return;
        }
        // Penyangga yang pernah membesar untuk satu baris raksasa dikembalikan.
        if tampung.is_empty() && tampung.capacity() > 1024 * 1024 {
            tampung = Vec::new();
        }
    }
}

struct KeadaanKlien {
    id: u64,
    sudah_halo: bool,
}

fn kirim(tx: &SyncSender<Keluaran>, id: u64, isi: IsiJawaban) -> bool {
    tx.send(Keluaran::Baris(baris(&Jawaban { id, isi }).into())).is_ok()
}

fn gagal(kunci: &str) -> IsiJawaban {
    IsiJawaban::Galat { galat: Galat::Gagal { pesan: Pesan::baru(kunci) } }
}

fn proses_baris(
    mentah: &[u8],
    k: &mut KeadaanKlien,
    tx: &SyncSender<Keluaran>,
    pelaksana: &dyn Pelaksana,
    penyiar: &Penyiar,
    pipa: &Arc<Pipa>,
) -> Arah {
    let teks = match std::str::from_utf8(mentah) {
        Ok(t) => t.trim_end_matches('\r'),
        Err(_) => {
            let g = Galat::TidakTerbaca { sebab: "bukan UTF-8".into() };
            kirim(tx, 0, IsiJawaban::Galat { galat: g });
            return if k.sudah_halo { Arah::Lanjut } else { Arah::Putus };
        }
    };
    if teks.trim().is_empty() {
        return Arah::Lanjut;
    }
    let p: Permintaan = match urai(teks) {
        Ok(p) => p,
        Err(g) => {
            // Jawab dengan id-nya bila masih bisa dibaca, supaya UI tahu
            // permintaan mana yang ditolak.
            let id = serde_json::from_str::<serde_json::Value>(teks)
                .ok()
                .and_then(|v| v.get("id").and_then(|i| i.as_u64()))
                .unwrap_or(0);
            if !kirim(tx, id, IsiJawaban::Galat { galat: g }) {
                return Arah::Putus;
            }
            return if k.sudah_halo { Arah::Lanjut } else { Arah::Putus };
        }
    };

    match p.perintah {
        Perintah::Halo { protokol, .. } if protokol != VERSI_PROTOKOL => {
            let g = Galat::ProtokolBeda { daemon: VERSI_PROTOKOL, ui: protokol };
            kirim(tx, p.id, IsiJawaban::Galat { galat: g });
            Arah::Putus
        }
        Perintah::Halo { .. } => {
            let isi = pelaksana.tangani(p.perintah);
            let berhasil = matches!(isi, IsiJawaban::Ok { .. });
            if !kirim(tx, p.id, isi) {
                return Arah::Putus;
            }
            if !berhasil {
                return Arah::Putus;
            }
            if !k.sudah_halo {
                k.sudah_halo = true;
                penyiar.daftar(k.id, tx.clone(), pipa.clone());
            }
            Arah::Lanjut
        }
        _ if !k.sudah_halo => {
            kirim(tx, p.id, gagal("Pesan pertama harus halo"));
            Arah::Putus
        }
        Perintah::Keluar => {
            // Jawaban masuk antrean SEBELUM Pamit (antreannya sama), jadi UI
            // menerima "ok" lalu "pamit", berurutan.
            kirim(tx, p.id, IsiJawaban::Ok { hasil: serde_json::Value::Null, peringatan: Vec::new() });
            crate::baki::minta_keluar();
            Arah::Lanjut
        }
        perintah => {
            let isi = pelaksana.tangani(perintah);
            if kirim(tx, p.id, isi) { Arah::Lanjut } else { Arah::Putus }
        }
    }
}

