//! Fitur: peringatan HSTS dari server pengembangan di localhost (HstsPeriksa.cs).
//!
//! Phoron tidak bisa memperbaikinya sendiri - headernya milik proyek pengguna
//! dan catatannya tersimpan di browser - tapi bisa MENJELASKANNYA, sebab
//! akibatnya muncul jauh dari sebabnya: satu proyek Node ber-HTTPS membuat
//! situs PHP di port 80 tidak bisa dibuka, lama sesudah server Node dimatikan.
//! HSTS melekat pada NAMA HOST dan mengabaikan port.
//!
//! Daftar periksa:
//! - Hanya https yang berlaku: header HSTS lewat HTTP polos diabaikan browser.
//! - Alamat IP telanjang tidak berlaku (browser tidak menyimpan HSTS untuk
//!   IP); yang berbahaya justru "localhost" dan "*.localhost".
//! - Server pengembangan mencetak alamatnya sebelum siap menerima sambungan:
//!   dicoba 3 kali berjeda 700 md, tiap percobaan berbatas 4 detik.
//! - Pengalihan TIDAK diikuti: yang ditanyakan header alamat ini sendiri.
//! - Proxy DIMATIKAN: di mesin berproxy, bawaan sistem (WPAD) meneruskan
//!   permintaan ke proxy dan jawabannya bukan dari server lokal.
//! - Sertifikat diabaikan PER PERMINTAAN, tidak pernah untuk seluruh proses
//!   (itu ikut mematikan pemeriksaan sertifikat pengunduh dan pembaruan).
//! - 404/405 tetap membawa header - server yang menolak HEAD sudah menjawab.
//! - Tidak pernah gagal keras: ini pemeriksaan penyedap.
//! - max-age dibulatkan ke bawah ke hari; 0 bila tidak terbaca.

use crate::protokol::Pesan;
use std::time::Duration;

pub const NAMA_HEADER: &str = "Strict-Transport-Security";

/// Bagian alamat yang dibutuhkan: host, port, dan jalur permintaan.
#[derive(Debug, Clone, PartialEq, Eq)]
struct Alamat {
    aman: bool,
    host: String,
    port: u16,
    jalur: String,
}

fn label_dns(l: &str) -> bool {
    !l.is_empty() && l.len() <= 63 && l.chars().all(|c| c.is_ascii_alphanumeric() || c == '-' || c == '_')
}

/// Urai alamat absolut http/https. Host IPv6 berkurung tetap diurai (untuk
/// [`berlaku`] cukup tahu bahwa itu bukan nama DNS).
fn urai(url: &str) -> Option<(Alamat, bool)> {
    let url = url.trim();
    let (skema, sisa) = url.split_once("://")?;
    let aman = match skema.to_ascii_lowercase().as_str() {
        "https" => true,
        "http" => false,
        _ => return None,
    };
    let akhir = sisa.find(['/', '?', '#']).unwrap_or(sisa.len());
    let (otoritas, jalur) = sisa.split_at(akhir);
    let otoritas = otoritas.rsplit_once('@').map_or(otoritas, |(_, h)| h);
    let (host, port, dns) = if let Some(r) = otoritas.strip_prefix('[') {
        let (h, p) = r.split_once(']')?;
        (h.to_string(), p, false)
    } else {
        let (h, p) = match otoritas.rfind(':') {
            Some(i) => (&otoritas[..i], &otoritas[i..]),
            None => (otoritas, ""),
        };
        let ip = h.parse::<std::net::Ipv4Addr>().is_ok();
        (h.to_ascii_lowercase(), p, !ip)
    };
    if host.is_empty() {
        return None;
    }
    let port = match port.strip_prefix(':') {
        None if port.is_empty() => if aman { 443 } else { 80 },
        Some("") => if aman { 443 } else { 80 },
        Some(p) => p.parse().ok()?,
        None => return None,
    };
    let dns = dns && host.trim_end_matches('.').split('.').all(label_dns);
    let jalur = jalur.split('#').next().unwrap_or("");
    let jalur = if jalur.is_empty() || jalur.starts_with('?') { format!("/{jalur}") } else { jalur.to_string() };
    Some((Alamat { aman, host, port, jalur }, dns))
}

/// Apakah alamat ini bisa membuat browser menyimpan catatan HSTS.
pub fn berlaku(url: &str) -> bool {
    match urai(url) {
        Some((a, dns)) => a.aman && dns && (a.host == "localhost" || a.host.ends_with(".localhost")),
        None => false,
    }
}

/// Nilai header HSTS yang dikirim alamat ini, atau None. Tidak pernah gagal keras.
pub fn baca_header(url: &str) -> Option<String> {
    if !berlaku(url) {
        return None;
    }
    let (a, _) = urai(url)?;
    for coba in 0..3 {
        if let Some(v) = winhttp::header(&a.host, a.port, &a.jalur, a.aman, NAMA_HEADER) {
            return Some(v);
        }
        if coba < 2 {
            std::thread::sleep(Duration::from_millis(700));
        }
    }
    None
}

mod winhttp {
    use windows_sys::Win32::Networking::WinHttp::*;

    fn w(s: &str) -> Vec<u16> {
        s.encode_utf16().chain(Some(0)).collect()
    }

    struct Pegangan(*mut core::ffi::c_void);
    impl Drop for Pegangan {
        fn drop(&mut self) {
            if !self.0.is_null() {
                unsafe { WinHttpCloseHandle(self.0) };
            }
        }
    }

    /// Satu permintaan HEAD; header bernama `nama` dari respons apa pun
    /// (termasuk 404/405), atau None bila tidak ada / tidak tersambung.
    pub fn header(host: &str, port: u16, jalur: &str, aman: bool, nama: &str) -> Option<String> {
        unsafe {
            let agen = w("Phoron");
            let sesi = Pegangan(WinHttpOpen(agen.as_ptr(), WINHTTP_ACCESS_TYPE_NO_PROXY, std::ptr::null(), std::ptr::null(), 0));
            if sesi.0.is_null() {
                return None;
            }
            WinHttpSetTimeouts(sesi.0, 4000, 4000, 4000, 4000);
            let h = w(host);
            let sambung = Pegangan(WinHttpConnect(sesi.0, h.as_ptr(), port, 0));
            if sambung.0.is_null() {
                return None;
            }
            let verb = w("HEAD");
            let obj = w(jalur);
            let flag = if aman { WINHTTP_FLAG_SECURE } else { 0 };
            let minta = Pegangan(WinHttpOpenRequest(
                sambung.0,
                verb.as_ptr(),
                obj.as_ptr(),
                std::ptr::null(),
                std::ptr::null(),
                std::ptr::null(),
                flag,
            ));
            if minta.0.is_null() {
                return None;
            }
            let atur = |opsi: u32, nilai: u32| {
                WinHttpSetOption(minta.0, opsi, (&nilai as *const u32).cast(), 4);
            };
            atur(WINHTTP_OPTION_REDIRECT_POLICY, WINHTTP_OPTION_REDIRECT_POLICY_NEVER);
            if aman {
                atur(
                    WINHTTP_OPTION_SECURITY_FLAGS,
                    SECURITY_FLAG_IGNORE_UNKNOWN_CA
                        | SECURITY_FLAG_IGNORE_CERT_DATE_INVALID
                        | SECURITY_FLAG_IGNORE_CERT_CN_INVALID
                        | SECURITY_FLAG_IGNORE_CERT_WRONG_USAGE,
                );
            }
            if WinHttpSendRequest(minta.0, std::ptr::null(), 0, std::ptr::null(), 0, 0, 0) == 0 {
                return None;
            }
            if WinHttpReceiveResponse(minta.0, std::ptr::null_mut()) == 0 {
                return None;
            }
            let n = w(nama);
            let mut buf = vec![0u16; 2048];
            let mut len = (buf.len() * 2) as u32;
            if WinHttpQueryHeaders(minta.0, WINHTTP_QUERY_CUSTOM, n.as_ptr(), buf.as_mut_ptr().cast(), &mut len, std::ptr::null_mut()) == 0 {
                return None;
            }
            Some(String::from_utf16_lossy(&buf[..len as usize / 2]))
        }
    }
}

/// max-age dalam hari, dibulatkan ke bawah; 0 bila tidak terbaca.
pub fn hari_dari_max_age(nilai: &str) -> i64 {
    for bagian in nilai.split(';') {
        let b = bagian.trim();
        if b.len() < 7 || !b[..7].eq_ignore_ascii_case("max-age") {
            continue;
        }
        let Some(i) = b.find('=') else { continue };
        if let Ok(detik) = b[i + 1..].trim().trim_matches('"').parse::<i64>() {
            if detik > 0 {
                return detik / 86400;
            }
        }
    }
    0
}

/// Kalimat peringatan untuk catatan aktivitas, atau None bila tidak
/// bermasalah. Tiga kalimat, ditampilkan berurutan dipisah spasi (di C#
/// tiga `Lang.T` yang disambung). `nama` = nama proyeknya.
pub fn keluhan(nama: &str, _url: &str, nilai_header: Option<&str>) -> Option<Vec<Pesan>> {
    let nilai = nilai_header.filter(|v| !v.is_empty())?;
    let hari = hari_dari_max_age(nilai).to_string();
    Some(vec![
        Pesan::dengan(
            "PERINGATAN HSTS: {0} mengirim header Strict-Transport-Security lewat HTTPS di localhost.",
            &[nama],
        ),
        Pesan::dengan(
            "Browser akan memaksa SEMUA alamat localhost ke HTTPS - termasuk situs PHP di port 80 - selama {0} hari, bahkan sesudah server ini dimatikan.",
            &[&hari],
        ),
        Pesan::baru(
            "Matikan header itu saat pengembangan, lalu buang catatannya lewat Riwayat > klik kanan situs > Lupakan Situs Ini.",
        ),
    ])
}

#[cfg(test)]
mod uji {
    use super::*;
    use std::io::{Read, Write};

    #[test]
    fn hanya_https_dan_nama_localhost() {
        assert!(berlaku("https://localhost:3000/"));
        assert!(berlaku("HTTPS://LocalHost"));
        assert!(berlaku("https://app.localhost:5173/x?y"));
        assert!(!berlaku("http://localhost:3000"));
        assert!(!berlaku("https://127.0.0.1:3000"));
        assert!(!berlaku("https://[::1]:3000"));
        assert!(!berlaku("https://contoh.test"));
        assert!(!berlaku("https://notlocalhost"));
        assert!(!berlaku("localhost:3000"));
        assert!(!berlaku("https://localhost:abc"));
        assert!(!berlaku(""));
    }

    #[test]
    fn urai_port_dan_jalur() {
        let (a, dns) = urai("https://user@app.localhost:8443/api?x=1#y").unwrap();
        assert_eq!(a, Alamat { aman: true, host: "app.localhost".into(), port: 8443, jalur: "/api?x=1".into() });
        assert!(dns);
        assert_eq!(urai("https://localhost").unwrap().0.port, 443);
        assert_eq!(urai("http://localhost?q").unwrap().0.jalur, "/?q");
    }

    #[test]
    fn max_age_ke_hari() {
        assert_eq!(hari_dari_max_age("max-age=31536000; includeSubDomains"), 365);
        assert_eq!(hari_dari_max_age("includeSubDomains; MAX-AGE = \"172800\""), 2);
        assert_eq!(hari_dari_max_age("max-age=0"), 0);
        assert_eq!(hari_dari_max_age("max-age=abc"), 0);
        assert_eq!(hari_dari_max_age(""), 0);
    }

    #[test]
    fn keluhan_tiga_kalimat() {
        assert!(keluhan("web", "https://localhost:3000", None).is_none());
        assert!(keluhan("web", "https://localhost:3000", Some("")).is_none());
        let k = keluhan("web", "https://localhost:3000", Some("max-age=86400")).unwrap();
        assert_eq!(k.len(), 3);
        assert_eq!(k[0].args, vec!["web".to_string()]);
        assert_eq!(k[1].args, vec!["1".to_string()]);
    }

    #[test]
    fn bukan_https_tidak_ditanyai() {
        assert_eq!(baca_header("http://localhost:1"), None);
    }

    /// Server HTTP polos satu kali jawab: membuktikan header terbaca dari
    /// respons 405 dan pengalihan tidak diikuti.
    #[test]
    fn header_terbaca_dari_405_tanpa_mengikuti_pengalihan() {
        let l = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
        let port = l.local_addr().unwrap().port();
        let t = std::thread::spawn(move || {
            for _ in 0..2 {
                let (mut s, _) = l.accept().unwrap();
                let mut b = [0u8; 1024];
                let n = s.read(&mut b).unwrap();
                let minta = String::from_utf8_lossy(&b[..n]).to_string();
                let jawab = if minta.starts_with("HEAD /pindah") {
                    "HTTP/1.1 302 Found\r\nLocation: /lain\r\nContent-Length: 0\r\n\r\n"
                } else {
                    "HTTP/1.1 405 Method Not Allowed\r\nStrict-Transport-Security: max-age=600\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                };
                s.write_all(jawab.as_bytes()).unwrap();
            }
        });
        assert_eq!(winhttp::header("127.0.0.1", port, "/", false, NAMA_HEADER).as_deref(), Some("max-age=600"));
        assert_eq!(winhttp::header("127.0.0.1", port, "/pindah", false, NAMA_HEADER), None);
        t.join().unwrap();
    }
}
