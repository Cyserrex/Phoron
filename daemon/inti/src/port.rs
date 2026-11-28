//! Fitur: port dipakai siapa.
//!
//! Keluhan paling sering pada perkakas semacam ini adalah "Apache tidak mau
//! nyala" padahal Laragon/XAMPP/IIS masih memegang port 80, dan pesan bawaan
//! Apache tidak menyebut proses mana yang bersalah.
//!
//! Daftar periksa:
//! - Port yang sedang didengar (IPv4 dan IPv6) dianggap terpakai.
//! - Pemegang port disebut dengan nama proses, PID, dan jalur exe-nya - jalur
//!   itulah yang membedakan httpd milik Phoron dari httpd milik Laragon.
//! - Port layanan yang TIDAK dipakai profil tidak diperiksa (profil tanpa MySQL
//!   tidak boleh mengeluh port 3306 dipegang XAMPP orang itu sendiri).
//! - Port layanan yang SEDANG JALAN tidak diperiksa: ia memegang portnya
//!   sendiri, dan dulu Phoron mengeluhkan mysqld miliknya sendiri.
//!
//! Beda dari versi C#: PID diambil dari `GetExtendedTcpTable`, tabel yang
//! sama yang dipakai `netstat`, tanpa menjalankan proses anak.

use crate::model::Profile;
use crate::protokol::Pesan;
use serde::{Deserialize, Serialize};
use windows_sys::Win32::Foundation::{CloseHandle, ERROR_INSUFFICIENT_BUFFER, NO_ERROR};
use windows_sys::Win32::NetworkManagement::IpHelper::{
    GetExtendedTcpTable, MIB_TCP6ROW_OWNER_PID, MIB_TCPROW_OWNER_PID, TCP_TABLE_OWNER_PID_LISTENER,
};
use windows_sys::Win32::Networking::WinSock::{AF_INET, AF_INET6};
use windows_sys::Win32::System::Threading::{
    OpenProcess, QueryFullProcessImageNameW, PROCESS_QUERY_LIMITED_INFORMATION,
};

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize, Default)]
pub struct Usage {
    pub port: u16,
    pub in_use: bool,
    pub pid: u32,
    pub process_name: String,
    pub jalur: String,
}

impl Usage {
    pub fn describe(&self) -> Pesan {
        let p = self.port.to_string();
        if !self.in_use {
            return Pesan::dengan("Port {0} bebas.", &[&p]);
        }
        let siapa = if self.process_name.is_empty() { "proses tak dikenal" } else { &self.process_name };
        if self.pid > 0 {
            Pesan::dengan("Port {0} sedang dipakai {1} (PID {2}).", &[&p, siapa, &self.pid.to_string()])
        } else {
            Pesan::dengan("Port {0} sedang dipakai {1}.", &[&p, siapa])
        }
    }
}

/// Isi tabel listener untuk satu keluarga alamat: (port, pid).
fn tabel(af: u32) -> Vec<(u16, u32)> {
    let mut ukuran: u32 = 0;
    let mut buf: Vec<u8> = Vec::new();
    for _ in 0..4 {
        let r = unsafe {
            GetExtendedTcpTable(
                if buf.is_empty() { std::ptr::null_mut() } else { buf.as_mut_ptr().cast() },
                &mut ukuran,
                0,
                af,
                TCP_TABLE_OWNER_PID_LISTENER,
                0,
            )
        };
        if r == ERROR_INSUFFICIENT_BUFFER {
            // Tabel bisa tumbuh di antara dua panggilan; diberi kelonggaran.
            buf = vec![0u8; ukuran as usize + 1024];
            ukuran = buf.len() as u32;
            continue;
        }
        if r != NO_ERROR || buf.is_empty() {
            return Vec::new();
        }
        let n = u32::from_ne_bytes(buf[0..4].try_into().unwrap()) as usize;
        // Baris dimulai setelah dwNumEntries, sejajar dengan ukuran baris.
        let mut hasil = Vec::with_capacity(n);
        unsafe {
            if af == AF_INET as u32 {
                let awal = buf.as_ptr().add(std::mem::size_of::<u32>()) as *const MIB_TCPROW_OWNER_PID;
                for i in 0..n {
                    let row = std::ptr::read_unaligned(awal.add(i));
                    hasil.push((u16::from_be(row.dwLocalPort as u16), row.dwOwningPid));
                }
            } else {
                let awal = buf.as_ptr().add(std::mem::size_of::<u32>()) as *const MIB_TCP6ROW_OWNER_PID;
                for i in 0..n {
                    let row = std::ptr::read_unaligned(awal.add(i));
                    hasil.push((u16::from_be(row.dwLocalPort as u16), row.dwOwningPid));
                }
            }
        }
        return hasil;
    }
    Vec::new()
}

fn pemegang(port: u16) -> Option<u32> {
    tabel(AF_INET as u32)
        .into_iter()
        .chain(tabel(AF_INET6 as u32))
        .find(|(p, _)| *p == port)
        .map(|(_, pid)| pid)
}

pub fn is_free(port: u16) -> bool {
    pemegang(port).is_none()
}

/// Jalur exe sebuah proses; "" untuk proses yang tidak boleh kita buka.
fn jalur_proses(pid: u32) -> String {
    unsafe {
        let h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid);
        if h.is_null() {
            return String::new();
        }
        let mut buf = [0u16; 1024];
        let mut n = buf.len() as u32;
        let ok = QueryFullProcessImageNameW(h, 0, buf.as_mut_ptr(), &mut n);
        CloseHandle(h);
        if ok == 0 {
            return String::new();
        }
        String::from_utf16_lossy(&buf[..n as usize])
    }
}

/// Nama proses dari daftar proses sistem - tetap ada walau jalurnya tertutup
/// (proses milik SYSTEM atau berhak lebih tinggi).
fn nama_proses(pid: u32) -> String {
    use windows_sys::Win32::System::Diagnostics::ToolHelp::{
        CreateToolhelp32Snapshot, Process32FirstW, Process32NextW, PROCESSENTRY32W, TH32CS_SNAPPROCESS,
    };
    unsafe {
        let snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if snap.is_null() || snap as isize == -1 {
            return String::new();
        }
        let mut e: PROCESSENTRY32W = std::mem::zeroed();
        e.dwSize = std::mem::size_of::<PROCESSENTRY32W>() as u32;
        let mut hasil = String::new();
        let mut ada = Process32FirstW(snap, &mut e) != 0;
        while ada {
            if e.th32ProcessID == pid {
                let n = e.szExeFile.iter().position(|&c| c == 0).unwrap_or(e.szExeFile.len());
                hasil = String::from_utf16_lossy(&e.szExeFile[..n]);
                break;
            }
            ada = Process32NextW(snap, &mut e) != 0;
        }
        CloseHandle(snap);
        // Seperti Process.ProcessName: tanpa ".exe".
        // get(i..), bukan [i..]: potongan di tengah karakter multibyte (nama
        // exe beraksara non-Latin) membuat [i..] panik.
        match hasil.len().checked_sub(4) {
            Some(i) if hasil.get(i..).is_some_and(|a| a.eq_ignore_ascii_case(".exe")) => hasil[..i].to_string(),
            _ => hasil,
        }
    }
}

pub fn check(port: u16) -> Usage {
    let mut u = Usage { port, ..Usage::default() };
    let Some(pid) = pemegang(port) else { return u };
    u.in_use = true;
    u.pid = pid;
    if pid > 0 {
        u.process_name = nama_proses(pid);
        u.jalur = jalur_proses(pid);
    }
    u
}

/// Port profil yang bermasalah. Layanan yang tidak dipakai profil atau yang
/// sedang jalan dilewati (lihat daftar periksa di atas).
pub fn conflicts(p: &Profile, https: bool, periksa_web: bool, periksa_db: bool) -> Vec<Usage> {
    let mut ports: Vec<i32> = Vec::new();
    if p.pakai_web() && periksa_web {
        ports.push(p.http_port);
        if https {
            ports.push(p.https_port);
        }
    }
    if p.pakai_mysql() && periksa_db {
        ports.push(p.mysql_port);
    }
    let mut unik: Vec<u16> = Vec::new();
    for x in ports.into_iter().filter_map(|x| u16::try_from(x).ok()) {
        if !unik.contains(&x) {
            unik.push(x);
        }
    }
    unik.into_iter().map(check).filter(|u| u.in_use).collect()
}

/// Port bebas pertama mulai dari `mulai` (untuk "Jalankan di port N").
pub fn cari_bebas(mulai: u16) -> Option<u16> {
    (mulai..=u16::MAX).find(|p| is_free(*p))
}

#[cfg(test)]
mod uji {
    use super::*;
    use std::net::TcpListener;

    #[test]
    fn listener_sendiri_terdeteksi_dengan_pid_dan_nama() {
        let l = TcpListener::bind("127.0.0.1:0").unwrap();
        let port = l.local_addr().unwrap().port();
        let u = check(port);
        assert!(u.in_use);
        assert_eq!(u.pid, std::process::id());
        let diri = std::env::current_exe().unwrap();
        assert_eq!(u.jalur.to_lowercase(), diri.to_string_lossy().to_lowercase());
        assert!(!u.process_name.is_empty() && !u.process_name.to_lowercase().ends_with(".exe"));
        drop(l);
        assert!(is_free(port));
    }

    #[test]
    fn listener_ipv6_juga_terhitung() {
        let Ok(l) = TcpListener::bind("[::1]:0") else { return };
        let port = l.local_addr().unwrap().port();
        assert!(!is_free(port));
    }

    #[test]
    fn profil_tanpa_mysql_tidak_memeriksa_3306() {
        let l = TcpListener::bind("127.0.0.1:0").unwrap();
        let port = l.local_addr().unwrap().port() as i32;
        let p = Profile { apache_id: "httpd".into(), http_port: 1, mysql_port: port, ..Profile::default() };
        assert!(conflicts(&p, false, true, true).iter().all(|u| u.port as i32 != port));
        let q = Profile { mysql_id: "mysql".into(), ..p.clone() };
        assert!(conflicts(&q, false, true, true).iter().any(|u| u.port as i32 == port));
        assert!(conflicts(&q, false, true, false).iter().all(|u| u.port as i32 != port));
    }
}
