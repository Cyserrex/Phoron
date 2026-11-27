//! Padanan `Rahasia.cs`: sandi MySQL disimpan tersandi lewat DPAPI dengan
//! cakupan pengguna, sebab phoron.ini ikut tersalin ke mana-mana.
//!
//! Harus SEPADAN dengan versi C#: sandi yang ditutup C# bisa dibuka di sini
//! dan sebaliknya. Entropinya sama ("Phoron/rahasia/v1"), dan keluarannya
//! base64 standar seperti `Convert.ToBase64String`.

use windows_sys::Win32::Foundation::LocalFree;
use windows_sys::Win32::Security::Cryptography::{
    CryptProtectData, CryptUnprotectData, CRYPTPROTECT_UI_FORBIDDEN, CRYPT_INTEGER_BLOB,
};

const BUMBU: &[u8] = b"Phoron/rahasia/v1";

fn blob(b: &[u8]) -> CRYPT_INTEGER_BLOB {
    CRYPT_INTEGER_BLOB { cbData: b.len() as u32, pbData: b.as_ptr() as *mut u8 }
}

fn ambil(out: &CRYPT_INTEGER_BLOB) -> Vec<u8> {
    let v = unsafe { std::slice::from_raw_parts(out.pbData, out.cbData as usize) }.to_vec();
    unsafe { LocalFree(out.pbData as _) };
    v
}

/// Tutup teks; "" bila DPAPI gagal - lebih baik tidak menyimpan apa pun
/// daripada menyimpannya terbuka.
pub fn tutup(teks: &str) -> String {
    if teks.is_empty() {
        return String::new();
    }
    let mut data = teks.as_bytes().to_vec();
    let masuk = blob(&data);
    let bumbu = blob(BUMBU);
    let mut keluar = CRYPT_INTEGER_BLOB { cbData: 0, pbData: std::ptr::null_mut() };
    // Deskripsi kosong seperti .NET Framework (ProtectedData memberi "").
    let kosong: [u16; 1] = [0];
    let ok = unsafe {
        CryptProtectData(
            &masuk,
            kosong.as_ptr(),
            &bumbu,
            std::ptr::null(),
            std::ptr::null(),
            CRYPTPROTECT_UI_FORBIDDEN,
            &mut keluar,
        )
    };
    data.iter_mut().for_each(|b| *b = 0);
    if ok == 0 {
        return String::new();
    }
    base64(&ambil(&keluar))
}

/// Buka sandi tersandi; "" bila kosong, bukan base64, atau milik pengguna lain.
pub fn buka(tersandi: &str) -> String {
    if tersandi.is_empty() {
        return String::new();
    }
    let Some(acak) = dari_base64(tersandi) else { return String::new() };
    let masuk = blob(&acak);
    let bumbu = blob(BUMBU);
    let mut keluar = CRYPT_INTEGER_BLOB { cbData: 0, pbData: std::ptr::null_mut() };
    let ok = unsafe {
        CryptUnprotectData(
            &masuk,
            std::ptr::null_mut(),
            &bumbu,
            std::ptr::null(),
            std::ptr::null(),
            CRYPTPROTECT_UI_FORBIDDEN,
            &mut keluar,
        )
    };
    if ok == 0 {
        return String::new();
    }
    String::from_utf8_lossy(&ambil(&keluar)).into_owned()
}

/// Nilai yang ditulis ke phoron.ini.
///
/// DPAPI menghasilkan teks BERBEDA tiap kali walau sandinya sama. Kalau
/// sandi tidak berubah sejak dimuat, bentuk tersandi lamanya dipakai lagi -
/// berkasnya tidak berubah tanpa alasan. Dan kalau penyandian gagal padahal
/// ada sandi, nilai lama dipertahankan, bukan dihapus.
pub fn tutup_untuk_simpan(teks: &str, tersandi_saat_muat: &str, terbuka_saat_muat: &str) -> String {
    if !tersandi_saat_muat.is_empty() && teks == terbuka_saat_muat {
        return tersandi_saat_muat.to_string();
    }
    let baru = tutup(teks);
    if baru.is_empty() && !teks.is_empty() {
        return tersandi_saat_muat.to_string();
    }
    baru
}

const ABJAD: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

pub fn base64(b: &[u8]) -> String {
    let mut s = String::with_capacity(b.len().div_ceil(3) * 4);
    for c in b.chunks(3) {
        let n = (c[0] as u32) << 16 | (*c.get(1).unwrap_or(&0) as u32) << 8 | *c.get(2).unwrap_or(&0) as u32;
        s.push(ABJAD[(n >> 18) as usize & 63] as char);
        s.push(ABJAD[(n >> 12) as usize & 63] as char);
        s.push(if c.len() > 1 { ABJAD[(n >> 6) as usize & 63] as char } else { '=' });
        s.push(if c.len() > 2 { ABJAD[n as usize & 63] as char } else { '=' });
    }
    s
}

pub fn dari_base64(s: &str) -> Option<Vec<u8>> {
    // Convert.FromBase64String mengabaikan spasi; selain itu ketat.
    let bersih: Vec<u8> = s.bytes().filter(|c| !c.is_ascii_whitespace()).collect();
    if !bersih.len().is_multiple_of(4) {
        return None;
    }
    let nilai = |c: u8| ABJAD.iter().position(|&a| a == c).map(|p| p as u32);
    let mut hasil = Vec::with_capacity(bersih.len() / 4 * 3);
    for (i, k) in bersih.chunks(4).enumerate() {
        let akhir = i == bersih.len() / 4 - 1;
        let pad = k.iter().rev().take_while(|&&c| c == b'=').count();
        if pad > 2 || (pad > 0 && !akhir) {
            return None;
        }
        let mut n = 0u32;
        for &c in &k[..4 - pad] {
            n = n << 6 | nilai(c)?;
        }
        n <<= 6 * pad as u32;
        hasil.push((n >> 16) as u8);
        if pad < 2 {
            hasil.push((n >> 8) as u8);
        }
        if pad < 1 {
            hasil.push(n as u8);
        }
    }
    Some(hasil)
}

#[cfg(test)]
mod uji {
    use super::*;

    #[test]
    fn base64_bolak_balik() {
        for t in [&b""[..], b"f", b"fo", b"foo", b"foob", b"fooba", b"foobar"] {
            assert_eq!(dari_base64(&base64(t)).unwrap(), t);
        }
        assert_eq!(base64(b"foobar"), "Zm9vYmFy");
        assert!(dari_base64("bukan base64!").is_none());
    }

    #[test]
    fn dpapi_bolak_balik() {
        let t = tutup("sandi-rahasia");
        assert!(!t.is_empty());
        assert_ne!(t, "sandi-rahasia");
        assert_eq!(buka(&t), "sandi-rahasia");
        assert_eq!(buka("ngawur"), "");
    }

    #[test]
    fn sandi_tak_berubah_tidak_disandi_ulang() {
        let lama = tutup("abc");
        assert_eq!(tutup_untuk_simpan("abc", &lama, "abc"), lama);
        assert_eq!(tutup_untuk_simpan("", &lama, "abc"), "");
    }
}
