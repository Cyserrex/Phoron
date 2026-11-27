//! Deskriptor keamanan eksplisit untuk objek bernama daemon: pipe, mutex
//! instans tunggal, dan sinyal keluar.
//!
//! Kenapa tidak deskriptor bawaan (argumen NULL):
//!
//! 1. DACL bawaan pipe memberi akses BACA kepada Everyone dan anonymous.
//! 2. Daemon yang dinaikkan haknya (fitur "jalankan ulang sebagai
//!    Administrator", atau dijalankan dari pemasang) mendapat DACL bawaan
//!    milik grup Administrators, bukan SID pengguna. Di token berhak biasa
//!    milik orang yang sama grup itu hanya untuk penolakan, jadi UI biasa
//!    tidak bisa menyambung.
//! 3. Objek buatan proses berhak tinggi berlabel integritas High; aturan
//!    "tidak boleh menulis ke atas" menutupnya bagi UI berlabel Medium.
//!
//! Jadi izinnya ditulis sendiri: akses penuh untuk SID pengguna saat ini,
//! SYSTEM, dan Administrators - tanpa Everyone/anonymous - plus label
//! integritas eksplisit. Pipe dan mutex berlabel Medium (UI biasa boleh
//! menulis, proses Low seperti sandbox peramban tidak). Sinyal keluar
//! berlabel Low, sama seperti `ObjekAntarProses.BuatEvent` di UI WPF lama,
//! supaya pemasang dan proses berhak lain tetap bisa memberinya sinyal.
//! Label hanya membawa kebijakan NO_WRITE_UP bawaan; tidak ada NO_READ_UP
//! atau NO_EXECUTE_UP tambahan.

use crate::nama::lebar;
use windows_sys::Win32::Foundation::{CloseHandle, LocalFree, HANDLE};
use windows_sys::Win32::Security::Authorization::{
    ConvertSidToStringSidW, ConvertStringSecurityDescriptorToSecurityDescriptorW, SDDL_REVISION_1,
};
use windows_sys::Win32::Security::{GetTokenInformation, TokenUser, PSECURITY_DESCRIPTOR, SECURITY_ATTRIBUTES, TOKEN_QUERY, TOKEN_USER};
use windows_sys::Win32::System::Threading::{GetCurrentProcess, OpenProcessToken};

/// Tingkat label integritas objek.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Label {
    /// `ME`: pipe dan mutex.
    Sedang,
    /// `LW`: sinyal keluar (seperti ObjekAntarProses di UI WPF).
    Rendah,
}

/// SID pengguna proses ini dalam bentuk teks (`S-1-5-21-...`).
pub fn sid_pengguna() -> Option<String> {
    unsafe {
        let mut token: HANDLE = std::ptr::null_mut();
        if OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &mut token) == 0 {
            return None;
        }
        let mut perlu = 0u32;
        GetTokenInformation(token, TokenUser, std::ptr::null_mut(), 0, &mut perlu);
        if perlu == 0 {
            CloseHandle(token);
            return None;
        }
        // u64 supaya penyangga ter-align untuk TOKEN_USER.
        let mut buf = vec![0u64; (perlu as usize).div_ceil(8)];
        let ok = GetTokenInformation(token, TokenUser, buf.as_mut_ptr().cast(), perlu, &mut perlu);
        CloseHandle(token);
        if ok == 0 {
            return None;
        }
        let tu = &*(buf.as_ptr() as *const TOKEN_USER);
        let mut teks: *mut u16 = std::ptr::null_mut();
        if ConvertSidToStringSidW(tu.User.Sid, &mut teks) == 0 || teks.is_null() {
            return None;
        }
        let s = dari_pwstr(teks);
        LocalFree(teks.cast());
        Some(s)
    }
}

/// Baca string UTF-16 berakhiran nol.
pub(crate) unsafe fn dari_pwstr(p: *const u16) -> String {
    let mut n = 0usize;
    while *p.add(n) != 0 {
        n += 1;
    }
    String::from_utf16_lossy(std::slice::from_raw_parts(p, n))
}

/// SDDL deskriptor untuk SID pengguna `sid` dan label `label`.
pub fn sddl(sid: &str, label: Label) -> String {
    let tingkat = match label {
        Label::Sedang => "ME",
        Label::Rendah => "LW",
    };
    // D:P = DACL terlindung (tidak mewarisi). GA = akses penuh.
    // S:(ML;;NW;;;..) = label integritas dengan kebijakan NO_WRITE_UP saja.
    format!("D:P(A;;GA;;;{sid})(A;;GA;;;SY)(A;;GA;;;BA)S:(ML;;NW;;;{tingkat})")
}

/// Deskriptor keamanan siap pakai. Hidup selama objeknya dibuat; boleh
/// dibuang sesudahnya (kernel menyalin isinya).
pub struct Deskriptor {
    psd: PSECURITY_DESCRIPTOR,
    sa: SECURITY_ATTRIBUTES,
}

impl Deskriptor {
    /// `None` bila SID pengguna tidak terbaca atau SDDL ditolak; pemanggil
    /// lalu jatuh ke deskriptor bawaan (lebih baik daemon jalan daripada tidak).
    pub fn baru(label: Label) -> Option<Deskriptor> {
        let sid = sid_pengguna()?;
        let w = lebar(&sddl(&sid, label));
        let mut psd: PSECURITY_DESCRIPTOR = std::ptr::null_mut();
        let ok = unsafe {
            ConvertStringSecurityDescriptorToSecurityDescriptorW(w.as_ptr(), SDDL_REVISION_1, &mut psd, std::ptr::null_mut())
        };
        if ok == 0 || psd.is_null() {
            return None;
        }
        let sa = SECURITY_ATTRIBUTES {
            nLength: std::mem::size_of::<SECURITY_ATTRIBUTES>() as u32,
            lpSecurityDescriptor: psd,
            bInheritHandle: 0,
        };
        Some(Deskriptor { psd, sa })
    }

    pub fn sa(&self) -> *const SECURITY_ATTRIBUTES {
        &self.sa
    }
}

impl Drop for Deskriptor {
    fn drop(&mut self) {
        unsafe { LocalFree(self.psd) };
    }
}

/// Penunjuk SECURITY_ATTRIBUTES dari deskriptor opsional (NULL = bawaan).
pub fn sa_atau_null(d: &Option<Deskriptor>) -> *const SECURITY_ATTRIBUTES {
    d.as_ref().map_or(std::ptr::null(), |d| d.sa())
}

/// SDDL DACL + label milik objek yang pegangannya punya hak READ_CONTROL.
/// Untuk uji dan penelusuran masalah.
#[cfg(test)]
pub fn baca_sddl(h: HANDLE) -> Option<String> {
    use windows_sys::Win32::Security::Authorization::{
        ConvertSecurityDescriptorToStringSecurityDescriptorW, GetSecurityInfo, SE_KERNEL_OBJECT,
    };
    use windows_sys::Win32::Security::{DACL_SECURITY_INFORMATION, LABEL_SECURITY_INFORMATION};
    let bagian = DACL_SECURITY_INFORMATION | LABEL_SECURITY_INFORMATION;
    unsafe {
        let mut psd: PSECURITY_DESCRIPTOR = std::ptr::null_mut();
        let n = std::ptr::null_mut();
        let acl = std::ptr::null_mut();
        if GetSecurityInfo(h, SE_KERNEL_OBJECT, bagian, n, n, acl, acl, &mut psd) != 0 {
            return None;
        }
        let mut teks: *mut u16 = std::ptr::null_mut();
        let ok = ConvertSecurityDescriptorToStringSecurityDescriptorW(psd, SDDL_REVISION_1, bagian, &mut teks, std::ptr::null_mut());
        LocalFree(psd);
        if ok == 0 || teks.is_null() {
            return None;
        }
        let s = dari_pwstr(teks);
        LocalFree(teks.cast());
        Some(s)
    }
}

/// Periksa SDDL hasil baca: hanya pengguna/SYSTEM/Administrators yang
/// diizinkan, tanpa Everyone/anonymous, dan labelnya sesuai. `Err` berisi
/// alasannya. Dipakai uji unit dan uji integrasi (salinan di tests/).
#[cfg(test)]
pub fn periksa_sddl(s: &str, sid: &str, label: Label) -> Result<(), String> {
    let (dacl, sacl) = match s.find("S:") {
        Some(i) => (&s[..i], &s[i..]),
        None => return Err(format!("tanpa label: {s}")),
    };
    let ace: Vec<&str> = dacl.split('(').skip(1).map(|a| a.trim_end_matches(')')).collect();
    if ace.is_empty() {
        return Err(format!("DACL kosong: {s}"));
    }
    let mut ada_pengguna = false;
    for a in &ace {
        let medan: Vec<&str> = a.split(';').collect();
        let pemilik = medan.last().copied().unwrap_or("");
        if medan.first() != Some(&"A") {
            return Err(format!("ACE bukan izin: {a}"));
        }
        match pemilik {
            "SY" | "BA" => {}
            p if p == sid => ada_pengguna = true,
            lain => return Err(format!("SID tak terduga {lain} di {s}")),
        }
    }
    if !ada_pengguna {
        return Err(format!("SID pengguna tidak ada: {s}"));
    }
    let tingkat = match label {
        Label::Sedang => "ME",
        Label::Rendah => "LW",
    };
    if !sacl.contains(&format!("(ML;;NW;;;{tingkat})")) {
        return Err(format!("label bukan {tingkat} NW: {s}"));
    }
    Ok(())
}

#[cfg(test)]
mod uji {
    use super::*;
    use windows_sys::Win32::Foundation::INVALID_HANDLE_VALUE;
    use windows_sys::Win32::Storage::FileSystem::{FILE_FLAG_OVERLAPPED, PIPE_ACCESS_DUPLEX};
    use windows_sys::Win32::System::Pipes::{CreateNamedPipeW, PIPE_REJECT_REMOTE_CLIENTS, PIPE_TYPE_BYTE};
    use windows_sys::Win32::System::Threading::{CreateEventW, CreateMutexW};

    fn nama(jenis: &str) -> Vec<u16> {
        lebar(&format!("Phoron.UjiKeamanan.{jenis}-{}", std::process::id()))
    }

    #[test]
    fn sddl_tanpa_everyone() {
        let s = sddl("S-1-5-21-1-2-3-1001", Label::Sedang);
        assert!(!s.contains(";WD)") && !s.contains(";AN)"));
        assert!(s.ends_with("S:(ML;;NW;;;ME)"));
        assert!(sddl("S-1", Label::Rendah).ends_with("(ML;;NW;;;LW)"));
    }

    #[test]
    fn mutex_event_dan_pipe_berizin_eksplisit() {
        let sid = sid_pengguna().expect("SID pengguna");
        assert!(sid.starts_with("S-1-"), "{sid}");

        let d = Deskriptor::baru(Label::Sedang).expect("deskriptor");
        let m = unsafe { CreateMutexW(d.sa(), 0, nama("mutex").as_ptr()) };
        assert!(!m.is_null());
        let s = baca_sddl(m).expect("SDDL mutex");
        periksa_sddl(&s, &sid, Label::Sedang).unwrap();
        unsafe { CloseHandle(m) };

        let pipa = lebar(&format!(r"\\.\pipe\phoron-uji-keamanan-{}", std::process::id()));
        let p = unsafe {
            CreateNamedPipeW(
                pipa.as_ptr(),
                PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
                PIPE_TYPE_BYTE | PIPE_REJECT_REMOTE_CLIENTS,
                1,
                4096,
                4096,
                0,
                d.sa(),
            )
        };
        assert!(p != INVALID_HANDLE_VALUE);
        let s = baca_sddl(p).expect("SDDL pipe");
        periksa_sddl(&s, &sid, Label::Sedang).unwrap();
        unsafe { CloseHandle(p) };

        let r = Deskriptor::baru(Label::Rendah).expect("deskriptor rendah");
        let e = unsafe { CreateEventW(r.sa(), 0, 0, nama("event").as_ptr()) };
        assert!(!e.is_null());
        let s = baca_sddl(e).expect("SDDL event");
        periksa_sddl(&s, &sid, Label::Rendah).unwrap();
        unsafe { CloseHandle(e) };
    }

    #[test]
    fn periksa_menolak_everyone() {
        assert!(periksa_sddl("D:(A;;GA;;;WD)S:(ML;;NW;;;ME)", "S-1", Label::Sedang).is_err());
        assert!(periksa_sddl("D:(A;;GA;;;S-1)", "S-1", Label::Sedang).is_err());
        assert!(periksa_sddl("D:(A;;GA;;;S-1)(A;;GA;;;SY)S:(ML;;NW;;;ME)", "S-1", Label::Sedang).is_ok());
    }
}
