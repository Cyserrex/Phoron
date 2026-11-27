//! Padanan perilaku string .NET yang dipakai Core.
//!
//! Beberapa perbandingan di C# memakai aturan yang tidak dimiliki Rust secara
//! bawaan (OrdinalIgnoreCase, urutan budaya). Dikumpulkan di sini supaya
//! perbedaannya tercatat di satu tempat, bukan tersebar dan terlupa.

use std::cmp::Ordering;

/// `StringComparison.OrdinalIgnoreCase`: dibandingkan setelah huruf besar
/// sederhana per aksara. Kunci INI, nama seksi, dan jalur Windows memakai ini.
pub fn sama_abaikan_besar(a: &str, b: &str) -> bool {
    if a.len() == b.len() && a.eq_ignore_ascii_case(b) {
        return true;
    }
    let mut x = a.chars().map(besar);
    let mut y = b.chars().map(besar);
    loop {
        match (x.next(), y.next()) {
            (None, None) => return true,
            (Some(p), Some(q)) if p == q => continue,
            _ => return false,
        }
    }
}

/// Huruf besar satu-ke-satu seperti `char.ToUpperInvariant` - aksara yang
/// menjadi lebih dari satu aksara (ß -> SS) dibiarkan, sama seperti .NET.
fn besar(c: char) -> char {
    let mut u = c.to_uppercase();
    match (u.next(), u.next()) {
        (Some(h), None) => h,
        _ => c,
    }
}

/// `string.TrimEnd(c)`.
pub fn potong_akhir(s: &str, c: char) -> &str {
    s.trim_end_matches(c)
}

/// Perkiraan urutan `OrderBy(x => x)` milik .NET (perbandingan budaya).
///
/// Perbandingan budaya Windows mengabaikan tanda hubung dan apostrof pada
/// putaran pertama ("word sort") dan tidak peka besar-kecil kecuali seri.
/// Urutan berkas profil bergantung padanya: profil pertama dipakai saat
/// `profil_aktif` kosong, jadi urutannya harus sama dengan versi C#.
/// Ini PERKIRAAN, bukan salinan utuh aturan ICU/NLS - cukup untuk nama berkas
/// ASCII yang dibuat `Slug`, dan dicatat di uji setara.
pub fn urut_budaya(a: &str, b: &str) -> Ordering {
    let kunci = |s: &str| -> String {
        s.chars()
            .filter(|c| *c != '-' && *c != '\'')
            .flat_map(|c| c.to_lowercase())
            .collect()
    };
    kunci(a)
        .cmp(&kunci(b))
        .then_with(|| a.to_lowercase().cmp(&b.to_lowercase()))
        // Seri tanpa beda huruf: huruf kecil lebih dulu, seperti .NET.
        .then_with(|| b.cmp(a))
}

/// `char.IsLetterOrDigit`. Rust tidak punya kategori Unicode `Nd` tersendiri;
/// `is_numeric` juga menerima angka romawi dan pecahan (`Nl`, `No`). Bedanya
/// hanya muncul pada nama profil beraksara angka khusus, yang tetap
/// menghasilkan slug sah.
pub fn huruf_atau_angka(c: char) -> bool {
    c.is_alphabetic() || c.is_numeric()
}

#[cfg(test)]
mod uji {
    use super::*;

    #[test]
    fn abaikan_besar() {
        assert!(sama_abaikan_besar("Profil", "PROFIL"));
        assert!(sama_abaikan_besar("ümlaut", "ÜMLAUT"));
        assert!(!sama_abaikan_besar("a", "ab"));
    }

    #[test]
    fn urutan_mengabaikan_tanda_hubung() {
        // Ordinal: "php-8" < "php5" ('-' = 0x2D < '5'). Budaya: tanda hubung
        // diabaikan, jadi "php5..." vs "php8..." yang menentukan.
        assert_eq!(urut_budaya("php-8", "php5"), Ordering::Greater);
        assert_eq!(urut_budaya("a", "B"), Ordering::Less);
    }
}
