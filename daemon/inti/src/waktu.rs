//! Tanggal tanpa crate tambahan: hanya yang dipakai Core.
//!
//! - `cek_terakhir` di phoron.ini memakai format "o" .NET
//!   (`2026-11-27T07:50:29.1234567Z`).
//! - Nama berkas karantina memakai waktu LOKAL `yyyyMMdd-HHmmss`.

use std::time::{Duration, SystemTime, UNIX_EPOCH};

/// Detik sejak 1970 dan sisa ketelitian 100 ns (satuan tick .NET).
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, serde::Serialize, serde::Deserialize)]
pub struct Saat {
    pub detik: i64,
    pub tick: u32,
}

impl Saat {
    pub fn sekarang() -> Self {
        let d = SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or(Duration::ZERO);
        Saat { detik: d.as_secs() as i64, tick: d.subsec_nanos() / 100 }
    }

    /// Format "o" .NET untuk waktu UTC: selalu tujuh digit pecahan dan "Z".
    pub fn ke_o(&self) -> String {
        let (y, mo, d) = sipil_dari_hari(self.detik.div_euclid(86_400));
        let s = self.detik.rem_euclid(86_400);
        format!(
            "{:04}-{:02}-{:02}T{:02}:{:02}:{:02}.{:07}Z",
            y, mo, d, s / 3600, (s / 60) % 60, s % 60, self.tick
        )
    }

    /// Urai bentuk ISO yang ditulis versi C# ("o"): pecahan opsional, akhiran
    /// Z atau ±HH:MM atau tanpa zona (dianggap UTC - versi C# hanya pernah
    /// menyimpan DateTime.UtcNow). None bila bentuknya lain.
    pub fn urai_o(s: &str) -> Option<Saat> {
        let s = s.trim();
        let b = s.as_bytes();
        if b.len() < 19 || b[4] != b'-' || b[7] != b'-' || b[10] != b'T' || b[13] != b':' || b[16] != b':' {
            return None;
        }
        let n = |a: usize, z: usize| s.get(a..z)?.parse::<i64>().ok();
        let (y, mo, d, h, mi, se) = (n(0, 4)?, n(5, 7)?, n(8, 10)?, n(11, 13)?, n(14, 16)?, n(17, 19)?);
        if !(1..=12).contains(&mo) || !(1..=31).contains(&d) || h > 23 || mi > 59 || se > 59 {
            return None;
        }
        let mut i = 19;
        let mut tick: u32 = 0;
        if b.get(i) == Some(&b'.') {
            i += 1;
            let mulai = i;
            while i < b.len() && b[i].is_ascii_digit() {
                i += 1;
            }
            let pecahan = &s[mulai..i];
            if pecahan.is_empty() || pecahan.len() > 7 {
                return None;
            }
            tick = format!("{pecahan:0<7}").parse().ok()?;
        }
        let mut geser: i64 = 0;
        match &s[i..] {
            "" | "Z" => {}
            z if z.len() == 6 && (z.starts_with('+') || z.starts_with('-')) && &z[3..4] == ":" => {
                let jam: i64 = z[1..3].parse().ok()?;
                let menit: i64 = z[4..6].parse().ok()?;
                geser = (jam * 3600 + menit * 60) * if z.starts_with('-') { -1 } else { 1 };
            }
            _ => return None,
        }
        let hari = hari_dari_sipil(y, mo as u32, d as u32);
        Some(Saat { detik: hari * 86_400 + h * 3600 + mi * 60 + se - geser, tick })
    }
}

/// Algoritme Howard Hinnant: hari sejak 1970-01-01 -> (tahun, bulan, tanggal).
pub fn sipil_dari_hari(z: i64) -> (i64, u32, u32) {
    let z = z + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z.rem_euclid(146_097);
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = (doy - (153 * mp + 2) / 5 + 1) as u32;
    let m = if mp < 10 { mp + 3 } else { mp - 9 } as u32;
    (if m <= 2 { y + 1 } else { y }, m, d)
}

pub fn hari_dari_sipil(y: i64, m: u32, d: u32) -> i64 {
    let y = if m <= 2 { y - 1 } else { y };
    let era = y.div_euclid(400);
    let yoe = y.rem_euclid(400);
    let m = m as i64;
    let doy = (153 * (if m > 2 { m - 3 } else { m + 9 }) + 2) / 5 + d as i64 - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    era * 146_097 + doe - 719_468
}

/// `DateTime.Now.ToString("yyyyMMdd-HHmmss")` - waktu lokal, untuk nama berkas.
pub fn cap_lokal_berkas() -> String {
    use windows_sys::Win32::Foundation::SYSTEMTIME;
    use windows_sys::Win32::System::SystemInformation::GetLocalTime;
    let mut t: SYSTEMTIME = unsafe { std::mem::zeroed() };
    unsafe { GetLocalTime(&mut t) };
    format!(
        "{:04}{:02}{:02}-{:02}{:02}{:02}",
        t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute, t.wSecond
    )
}

#[cfg(test)]
mod uji {
    use super::*;

    #[test]
    fn bolak_balik_format_o() {
        let teks = "2026-11-27T07:50:29.1234567Z";
        let s = Saat::urai_o(teks).unwrap();
        assert_eq!(s.ke_o(), teks);
    }

    #[test]
    fn zona_dan_pecahan_pendek() {
        let a = Saat::urai_o("2026-11-27T15:50:29.5+08:00").unwrap();
        assert_eq!(a.ke_o(), "2026-11-27T07:50:29.5000000Z");
        assert!(Saat::urai_o("kemarin").is_none());
        assert!(Saat::urai_o("").is_none());
    }

    #[test]
    fn tanggal_kabisat() {
        let h = hari_dari_sipil(2028, 2, 29);
        assert_eq!(sipil_dari_hari(h), (2028, 2, 29));
        assert_eq!(hari_dari_sipil(1970, 1, 1), 0);
    }
}
