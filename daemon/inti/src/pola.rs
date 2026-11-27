//! Pencocok pola kecil pengganti `Regex` .NET yang dipakai pemindai.
//!
//! Polanya sedikit dan sederhana; menarik crate regex (ratusan KB di exe
//! yang hidup sepanjang hari) untuk lima pola tidak sepadan. Tiap fungsi
//! menyebut pola aslinya, dan ujinya membandingkan hasil dengan perilaku
//! `Regex` .NET pada kasus yang sama.
//!
//! Catatan: `\d` di .NET juga cocok dengan angka Unicode lain (mis. angka
//! Arab-India). Di sini hanya 0-9; nama folder paket selalu ASCII.

fn digit(b: &[u8], mut i: usize) -> usize {
    while i < b.len() && b[i].is_ascii_digit() {
        i += 1;
    }
    i
}

/// Coba cocokkan `\d+\.\d+(?:\.\d+)?` TEPAT di posisi `i`; kembalikan ujungnya.
fn versi_di(b: &[u8], i: usize) -> Option<usize> {
    let a = digit(b, i);
    if a == i || b.get(a) != Some(&b'.') {
        return None;
    }
    let c = digit(b, a + 1);
    if c == a + 1 {
        return None;
    }
    if b.get(c) == Some(&b'.') {
        let d = digit(b, c + 1);
        if d > c + 1 {
            return Some(d);
        }
    }
    Some(c)
}

/// `(\d+\.\d+(?:\.\d+)?)` - kecocokan paling kiri.
pub fn versi(s: &str) -> Option<&str> {
    let b = s.as_bytes();
    (0..b.len()).find_map(|i| versi_di(b, i).map(|e| &s[i..e]))
}

/// `v(\d+)` tanpa peka besar-kecil - cadangan untuk "node-v18".
pub fn mayor_saja(s: &str) -> Option<&str> {
    let b = s.as_bytes();
    (0..b.len()).find_map(|i| {
        if b[i] == b'v' || b[i] == b'V' {
            let e = digit(b, i + 1);
            (e > i + 1).then(|| &s[i + 1..e])
        } else {
            None
        }
    })
}

fn huruf_kata(c: Option<char>) -> bool {
    c.is_some_and(|c| c.is_alphanumeric() || c == '_')
}

/// `\b(vc\d{1,2}|vs\d{2})\b` tanpa peka besar-kecil.
pub fn kompilator(s: &str) -> Option<&str> {
    let b = s.as_bytes();
    for i in 0..b.len().saturating_sub(2) {
        let sebelum = s[..i].chars().next_back();
        if huruf_kata(sebelum) || !s.is_char_boundary(i) {
            continue;
        }
        let dua = [b[i].to_ascii_lowercase(), b[i + 1].to_ascii_lowercase()];
        let (min, max) = match &dua {
            b"vc" => (1, 2),
            b"vs" => (2, 2),
            _ => continue,
        };
        let e = digit(b, i + 2);
        let n = e - (i + 2);
        if n >= min && n <= max && !huruf_kata(s[e..].chars().next()) {
            return Some(&s[i..e]);
        }
    }
    None
}

/// `<awalan>\s+(\d+\.\d+(?:\.\d+)?)` (atau tanpa `\s+` bila `spasi` false),
/// awalan tidak peka besar-kecil. Untuk keluaran `php -v`, `httpd -v`, dst.
pub fn versi_setelah<'a>(s: &'a str, awalan: &str, spasi: bool) -> Option<&'a str> {
    let rendah = s.to_ascii_lowercase();
    let aw = awalan.to_ascii_lowercase();
    let b = s.as_bytes();
    let mut dari = 0;
    while let Some(pos) = rendah[dari..].find(&aw) {
        let mut i = dari + pos + aw.len();
        dari += pos + 1;
        if spasi {
            let mulai = i;
            while let Some(c) = s[i..].chars().next().filter(|c| c.is_whitespace()) {
                i += c.len_utf8();
            }
            if i == mulai {
                continue;
            }
        }
        if let Some(e) = versi_di(b, i) {
            return Some(&s[i..e]);
        }
    }
    None
}

/// `"((?:[^"\\]|\\.)*)"\s*:` - semua kunci objek JSON dalam teks, berurutan.
pub fn kunci_json(isi: &str) -> Vec<String> {
    let cs: Vec<char> = isi.chars().collect();
    let mut hasil = Vec::new();
    let mut i = 0;
    while i < cs.len() {
        if cs[i] != '"' {
            i += 1;
            continue;
        }
        // Isi string sampai kutip penutup.
        let mut j = i + 1;
        let mut s = String::new();
        let mut tutup = None;
        while j < cs.len() {
            match cs[j] {
                '\\' if j + 1 < cs.len() => {
                    s.push('\\');
                    s.push(cs[j + 1]);
                    j += 2;
                }
                '"' => {
                    tutup = Some(j);
                    break;
                }
                c => {
                    s.push(c);
                    j += 1;
                }
            }
        }
        let Some(t) = tutup else { break };
        let mut k = t + 1;
        while k < cs.len() && cs[k].is_whitespace() {
            k += 1;
        }
        if k < cs.len() && cs[k] == ':' {
            hasil.push(s);
            i = k + 1;
        } else {
            // Bukan kunci: Regex .NET mencoba lagi dari kutip PENUTUP ini, yang
            // bisa saja awal sebuah kunci. t > i, jadi putaran selalu maju.
            i = t;
        }
    }
    hasil
}

#[cfg(test)]
mod uji {
    use super::*;

    #[test]
    fn versi_paling_kiri() {
        assert_eq!(versi("php-8.3.12-Win32-vs16-x64"), Some("8.3.12"));
        assert_eq!(versi("httpd-2.4.54-win64-VS16"), Some("2.4.54"));
        assert_eq!(versi("mysql-8.0.30-winx64"), Some("8.0.30"));
        assert_eq!(versi("php8.1"), Some("8.1"));
        assert_eq!(versi("a12x3.4"), Some("3.4"));
        assert_eq!(versi("1.2.3.4"), Some("1.2.3"));
        assert_eq!(versi("php"), None);
        assert_eq!(versi("7."), None);
    }

    #[test]
    fn mayor() {
        assert_eq!(mayor_saja("node-v18"), Some("18"));
        assert_eq!(mayor_saja("V20-x64"), Some("20"));
        assert_eq!(mayor_saja("node"), None);
    }

    #[test]
    fn toolset() {
        assert_eq!(kompilator("php-8.3.12-Win32-vs16-x64"), Some("vs16"));
        assert_eq!(kompilator("httpd-2.4.38-win64-VC11"), Some("VC11"));
        assert_eq!(kompilator("php-5.6-VC9-x86"), Some("VC9"));
        assert_eq!(kompilator("abcvc11"), None);
        assert_eq!(kompilator("vc123"), None);
        assert_eq!(kompilator("vs1"), None);
        assert_eq!(kompilator("x-vs17"), Some("vs17"));
    }

    #[test]
    fn versi_dari_keluaran_program() {
        assert_eq!(versi_setelah("PHP 8.3.12 (cli) (built: ...)", "PHP", true), Some("8.3.12"));
        assert_eq!(versi_setelah("Server version: Apache/2.4.54 (Win64)", "Apache/", false), Some("2.4.54"));
        assert_eq!(
            versi_setelah("mysqld  Ver 8.0.30 for Win64 on x86_64", "Ver", true),
            Some("8.0.30")
        );
        assert_eq!(versi_setelah("PHPx 1.2", "PHP", true), None);
        assert_eq!(versi_setelah("Ver x Ver 10.4.32-MariaDB", "ver", true), Some("10.4.32"));
    }

    #[test]
    fn kunci_package_json() {
        let isi = r#" "dev": "next dev", "build": "astro check && astro build", "x\"y": "a:b", "lint" : "z" "#;
        assert_eq!(kunci_json(isi), vec!["dev", "build", "x\\\"y", "lint"]);
    }
}
