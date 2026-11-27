//! Padanan `Ini.cs`: pembaca/penulis INI untuk phoron.ini dan berkas profil.
//!
//! Sengaja bukan JSON: berkas ini harus enak disunting tangan.
//!
//! Pembacaannya memaafkan - satu baris nyasar tidak boleh mematikan
//! aplikasi - tapi MELAPORKAN keadaannya. Dulu phoron.ini yang terpotong
//! terbaca sebagai sah, seluruh nilai kembali ke bawaan, dan penyimpanan
//! berikutnya menuliskan bawaan itu kembali: sisa setelan yang selamat ikut
//! hilang.

use crate::atomic_file;
use crate::teks::sama_abaikan_besar;
use std::io;
use std::path::Path;

#[derive(Debug, Clone, Default, PartialEq)]
pub struct Ini {
    // Urutan seksi dan kunci dipertahankan supaya berkas yang ditulis ulang
    // tidak berantakan dibanding versi yang disunting pengguna.
    seksi: Vec<(String, Vec<(String, String)>)>,
}

/// Hasil pembacaan berikut keterangan tentang keadaan berkasnya.
#[derive(Debug, Default)]
pub struct HasilBaca {
    pub isi: Ini,
    /// Berkasnya ada. Berkas yang tidak ada BUKAN berkas rusak.
    pub ada: bool,
    /// Gagal dibaca sama sekali: izin, terkunci, drive putus.
    pub gagal_baca: bool,
    /// Terbaca, tapi isinya ada yang tidak masuk akal.
    pub rusak: bool,
    pub sebab: Option<String>,
    pub keluhan: Vec<String>,
    pub baris_terbaca: usize,
    pub baris_dibuang: usize,
}

impl Ini {
    pub fn baru() -> Self {
        Self::default()
    }

    pub fn sections(&self) -> impl Iterator<Item = &str> {
        self.seksi.iter().map(|(n, _)| n.as_str())
    }

    /// Baca sambil melaporkan keadaan berkasnya; tidak pernah gagal.
    pub fn baca(path: &Path) -> HasilBaca {
        let mut h = HasilBaca::default();
        if !path.is_file() {
            return h;
        }
        h.ada = true;
        let teks = match baca_teks(path) {
            Ok(t) => t,
            Err(e) => {
                h.gagal_baca = true;
                h.keluhan.push(format!("tidak bisa dibaca: {e}"));
                h.sebab = Some(e.to_string());
                return h;
            }
        };
        urai(&teks, &mut h);
        h
    }

    /// Perilaku lama: berkas yang tidak ada menghasilkan Ini kosong, dan
    /// kegagalan baca dikembalikan sebagai galat.
    pub fn load(path: &Path) -> io::Result<Ini> {
        if !path.is_file() {
            return Ok(Ini::default());
        }
        let teks = baca_teks(path)?;
        let mut h = HasilBaca::default();
        urai(&teks, &mut h);
        Ok(h.isi)
    }

    fn pastikan_seksi(&mut self, nama: &str) -> usize {
        if let Some(i) = self.seksi.iter().position(|(n, _)| sama_abaikan_besar(n, nama)) {
            return i;
        }
        self.seksi.push((nama.to_string(), Vec::new()));
        self.seksi.len() - 1
    }

    fn cari_seksi(&self, nama: &str) -> Option<&Vec<(String, String)>> {
        self.seksi
            .iter()
            .find(|(n, _)| sama_abaikan_besar(n, nama))
            .map(|(_, v)| v)
    }

    pub fn set(&mut self, section: &str, key: &str, value: &str) {
        let i = self.pastikan_seksi(section);
        let daftar = &mut self.seksi[i].1;
        // Nama kunci yang SUDAH ada dipertahankan ejaannya; hanya nilainya diganti.
        if let Some(kv) = daftar.iter_mut().find(|(k, _)| sama_abaikan_besar(k, key)) {
            kv.1 = value.to_string();
            return;
        }
        daftar.push((key.to_string(), value.to_string()));
    }

    pub fn get(&self, section: &str, key: &str) -> Option<&str> {
        self.cari_seksi(section)?
            .iter()
            .find(|(k, _)| sama_abaikan_besar(k, key))
            .map(|(_, v)| v.as_str())
    }

    pub fn get_or<'a>(&'a self, section: &str, key: &str, fallback: &'a str) -> &'a str {
        self.get(section, key).unwrap_or(fallback)
    }

    /// `int.TryParse`: spasi di tepi dan tanda +/- diterima.
    pub fn get_int(&self, section: &str, key: &str, fallback: i32) -> i32 {
        self.get(section, key)
            .and_then(|s| s.trim().parse::<i32>().ok())
            .unwrap_or(fallback)
    }

    pub fn get_bool(&self, section: &str, key: &str, fallback: bool) -> bool {
        match self.get(section, key) {
            None | Some("") => fallback,
            Some(s) => matches!(s.trim().to_lowercase().as_str(), "1" | "true" | "yes" | "on"),
        }
    }

    pub fn items(&self, section: &str) -> &[(String, String)] {
        self.cari_seksi(section).map(|v| v.as_slice()).unwrap_or(&[])
    }

    pub fn remove(&mut self, section: &str, key: &str) {
        if let Some((_, daftar)) = self
            .seksi
            .iter_mut()
            .find(|(n, _)| sama_abaikan_besar(n, section))
        {
            daftar.retain(|(k, _)| !sama_abaikan_besar(k, key));
        }
    }

    /// Teks berkas persis seperti `Ini.Save` versi C#: CRLF, satu baris kosong
    /// sesudah tiap seksi, kepala berawalan "; ".
    pub fn ke_teks(&self, header: Option<&str>) -> String {
        let mut sb = String::new();
        if let Some(h) = header.filter(|h| !h.is_empty()) {
            for l in h.split('\n') {
                sb.push_str("; ");
                sb.push_str(l.trim_end_matches('\r'));
                sb.push_str("\r\n");
            }
        }
        for (nama, daftar) in &self.seksi {
            if !nama.is_empty() {
                sb.push('[');
                sb.push_str(nama);
                sb.push_str("]\r\n");
            }
            for (k, v) in daftar {
                sb.push_str(k);
                sb.push('=');
                sb.push_str(v);
                sb.push_str("\r\n");
            }
            sb.push_str("\r\n");
        }
        sb
    }

    /// Lewat AtomicFile: penulisan langsung memotong berkasnya lebih dulu, dan
    /// gangguan di celah itu menyisakan berkas nol byte.
    pub fn save(&self, path: &Path, header: Option<&str>) -> io::Result<()> {
        atomic_file::write_all_text(path, &self.ke_teks(header))
    }
}

/// `File.ReadAllLines`: BOM UTF-8/UTF-16 dikenali, selain itu UTF-8 dengan
/// pengganti untuk byte yang tidak sah.
pub fn baca_teks(path: &Path) -> io::Result<String> {
    let b = std::fs::read(path)?;
    Ok(dekode(&b))
}

pub fn dekode(b: &[u8]) -> String {
    if let Some(r) = b.strip_prefix(&[0xEF, 0xBB, 0xBF]) {
        return String::from_utf8_lossy(r).into_owned();
    }
    if let Some(r) = b.strip_prefix(&[0xFF, 0xFE]) {
        let u: Vec<u16> = r.as_chunks::<2>().0.iter().map(|c| u16::from_le_bytes([c[0], c[1]])).collect();
        return String::from_utf16_lossy(&u);
    }
    if let Some(r) = b.strip_prefix(&[0xFE, 0xFF]) {
        let u: Vec<u16> = r.as_chunks::<2>().0.iter().map(|c| u16::from_be_bytes([c[0], c[1]])).collect();
        return String::from_utf16_lossy(&u);
    }
    String::from_utf8_lossy(b).into_owned()
}

/// Pemisah baris .NET: \r\n, \n, atau \r sendirian.
fn baris(teks: &str) -> Vec<&str> {
    let mut hasil = Vec::new();
    let b = teks.as_bytes();
    let (mut awal, mut i) = (0, 0);
    while i < b.len() {
        match b[i] {
            b'\r' => {
                hasil.push(&teks[awal..i]);
                i += if b.get(i + 1) == Some(&b'\n') { 2 } else { 1 };
                awal = i;
            }
            b'\n' => {
                hasil.push(&teks[awal..i]);
                i += 1;
                awal = i;
            }
            _ => i += 1,
        }
    }
    if awal < b.len() {
        hasil.push(&teks[awal..]);
    }
    hasil
}

fn urai(teks: &str, h: &mut HasilBaca) {
    let semua = baris(teks);

    // Deretan byte NUL adalah tanda khas berkas yang terpotong saat listrik
    // mati: NTFS sudah mencatat panjangnya, tapi isinya belum sempat turun
    // ke piringan.
    if semua.iter().any(|b| b.contains('\0')) {
        h.rusak = true;
        h.keluhan
            .push("berisi byte NUL - berkasnya kemungkinan terpotong".into());
    }

    let mut section = String::new();
    for (i, mentah) in semua.iter().enumerate() {
        // NUL dibuang, bukan sekadar dilaporkan: tanpa dibersihkan, sampah itu
        // ikut tertulis ke berkas yang baru dan terbaca cacat lagi selamanya.
        let bersih = mentah.replace('\0', "");
        let line = bersih.trim();
        if line.is_empty() || line.starts_with(';') || line.starts_with('#') {
            continue;
        }
        if line.starts_with('[') {
            if line.ends_with(']') && line.len() >= 2 {
                section = line[1..line.len() - 1].trim().to_string();
                h.isi.pastikan_seksi(&section);
                h.baris_terbaca += 1;
                continue;
            }
            // Dulu baris ini lenyap tanpa suara, dan seluruh kunci sesudahnya
            // masuk ke seksi yang salah.
            h.rusak = true;
            h.baris_dibuang += 1;
            h.keluhan.push(format!(
                "baris {}: seksi tidak ditutup - {}",
                i + 1,
                potong(line)
            ));
            continue;
        }
        let Some(eq) = line.find('=') else {
            h.rusak = true;
            h.baris_dibuang += 1;
            h.keluhan.push(format!(
                "baris {}: bukan kunci=nilai - {}",
                i + 1,
                potong(line)
            ));
            continue;
        };
        let kunci = line[..eq].trim();
        if kunci.is_empty() {
            h.rusak = true;
            h.keluhan.push(format!("baris {}: kunci kosong", i + 1));
        }
        h.isi.set(&section, kunci, line[eq + 1..].trim());
        h.baris_terbaca += 1;
    }
}

fn potong(teks: &str) -> String {
    // Panjang dihitung dalam satuan UTF-16 seperti .NET; untuk teks ASCII sama.
    if teks.encode_utf16().count() <= 40 {
        return teks.to_string();
    }
    let u: Vec<u16> = teks.encode_utf16().take(40).collect();
    String::from_utf16_lossy(&u) + "..."
}

#[cfg(test)]
mod uji {
    use super::*;

    fn urai_teks(t: &str) -> HasilBaca {
        let mut h = HasilBaca::default();
        urai(t, &mut h);
        h
    }

    #[test]
    fn membaca_dan_menulis_ulang_identik() {
        let asli = "; kepala\r\n[umum]\r\nbahasa=id\r\nauto_start=0\r\n\r\n[basisdata]\r\npengguna=root\r\n\r\n";
        let h = urai_teks(asli);
        assert!(!h.rusak);
        assert_eq!(h.isi.ke_teks(Some("kepala")), asli);
    }

    #[test]
    fn kunci_tidak_peka_besar_kecil_dan_ejaan_lama_dipertahankan() {
        let mut ini = urai_teks("[Umum]\nBahasa=id\n").isi;
        ini.set("umum", "bahasa", "en");
        assert_eq!(ini.get("UMUM", "BAHASA"), Some("en"));
        assert_eq!(ini.ke_teks(None), "[Umum]\r\nBahasa=en\r\n\r\n");
    }

    #[test]
    fn nul_dilaporkan_dan_dibuang() {
        let h = urai_teks("[umum]\r\ntema=gel\0\0\0\0");
        assert!(h.rusak);
        assert_eq!(h.isi.get("umum", "tema"), Some("gel"));
    }

    #[test]
    fn seksi_tak_tertutup_tidak_menyeret_kunci() {
        let h = urai_teks("[umum]\na=1\n[rusak\nb=2\n");
        assert!(h.rusak);
        assert_eq!(h.baris_dibuang, 1);
        // Kunci sesudahnya tetap di seksi terakhir yang sah, seperti C#.
        assert_eq!(h.isi.get("umum", "b"), Some("2"));
    }

    #[test]
    fn baris_tanpa_sama_dengan_dibuang() {
        let h = urai_teks("[umum]\nngawur\na=1");
        assert!(h.rusak);
        assert_eq!(h.baris_dibuang, 1);
        assert_eq!(h.isi.get("umum", "a"), Some("1"));
    }

    #[test]
    fn nilai_boleh_memuat_sama_dengan() {
        let h = urai_teks("[php.ini]\nerror_reporting = E_ALL & ~E_NOTICE=x\n");
        assert_eq!(h.isi.get("php.ini", "error_reporting"), Some("E_ALL & ~E_NOTICE=x"));
    }

    #[test]
    fn angka_dan_boolean() {
        let ini = urai_teks("[p]\nport= 8080 \nx=abc\nb=Yes\nc=0").isi;
        assert_eq!(ini.get_int("p", "port", 80), 8080);
        assert_eq!(ini.get_int("p", "x", 80), 80);
        assert_eq!(ini.get_int("p", "tidak_ada", 80), 80);
        assert!(ini.get_bool("p", "b", false));
        assert!(!ini.get_bool("p", "c", true));
        assert!(ini.get_bool("p", "tidak_ada", true));
    }

    #[test]
    fn bom_utf8_dan_cr_tunggal() {
        let teks = dekode(b"\xEF\xBB\xBF[a]\rk=v\r");
        let h = urai_teks(&teks);
        assert_eq!(h.isi.get("a", "k"), Some("v"));
    }

    #[test]
    fn hapus_kunci() {
        let mut ini = urai_teks("[umum]\ntoken_github=x\na=1").isi;
        ini.remove("umum", "token_github");
        assert_eq!(ini.ke_teks(None), "[umum]\r\na=1\r\n\r\n");
    }
}
