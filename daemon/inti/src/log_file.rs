//! Fitur dari LogFile.cs - log berputar di folder logs.
//!
//! Penulis berkas log (phoron.log, mysql-error.log, apache-error.log, ...):
//! berputar sebelum membesar, dan aman dipanggil dari banyak utas.
//!
//! Daftar periksa (pelajaran dari versi C#):
//! - phoron.log dulu tumbuh tanpa batas (365 KB dalam lima hari,
//!   apache-error.log 1,28 MB) dan tak ada yang sadar karena halaman Log hanya
//!   membaca ekornya. Kini berkas berputar begitu mencapai `BATAS_BYTES`
//!   (diperiksa SEBELUM menulis, jadi berkas boleh lewat satu baris), dan
//!   hanya `SIMPAN_LAMA` berkas lama yang disimpan: `.1` terbaru, `.3` tertua.
//!   Uji: `berputar_saat_batas_tercapai`, `yang_tertua_dibuang`.
//! - Dua penulis berbarengan dulu membuat salah satunya gagal diam-diam dan
//!   barisnya lenyap - justru baris saat sibuk yang paling dibutuhkan. Kini
//!   satu kunci selebar proses, dan pelanggaran berbagi (berkas dipegang
//!   sesaat oleh pembaca) dicoba ulang tiga kali. Uji:
//!   `banyak_utas_tidak_kehilangan_baris`.
//! - Berkas dibuka dengan berbagi baca/tulis supaya halaman Log yang membaca
//!   tiap dua detik tidak terhalang - dan tidak menghalangi. Uji:
//!   `menulis_saat_berkas_dibuka_pembaca`.
//! - Rotasi yang gagal (mis. pembaca memegang berkas tanpa izin hapus) harus
//!   berakhir pada "tetap menulis ke berkas yang besar", tidak pernah pada
//!   baris yang hilang atau galat yang menjalar ke pemanggil. Uji:
//!   `rotasi_gagal_tetap_menulis`.
//! - Galat izin atau jalur salah tidak dicoba ulang: mencoba lagi tidak akan
//!   mengubah apa pun. Folder tidak dibuat di sini (pemanggil memakai
//!   `paths::logs()` yang sudah memastikannya).
//!
//! Format baris: teks apa adanya + CRLF, UTF-8 tanpa BOM - sama dengan
//! `StreamWriter.WriteLine` di C#, jadi berkas buatan keduanya bisa
//! bercampur.

use std::ffi::OsString;
use std::fs::{self, OpenOptions};
use std::io::Write;
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use std::time::Duration;

/// Berkas berputar setelah melewati ukuran ini.
pub const BATAS_BYTES: u64 = 2 * 1024 * 1024;

/// Sebanyak ini berkas lama disimpan: phoron.log.1 sampai .3.
pub const SIMPAN_LAMA: u32 = 3;

/// Kunci selebar proses. Bukan mutex bernama: hanya satu daemon yang
/// berjalan, dan pembantu ber-admin tidak menulis log.
static KUNCI: Mutex<()> = Mutex::new(());

const ERROR_SHARING_VIOLATION: i32 = 32;
const ERROR_LOCK_VIOLATION: i32 = 33;

/// Tambahkan satu baris ke berkas log, memutar berkas lebih dulu bila sudah
/// mencapai batas. Tidak pernah gagal ke pemanggil.
pub fn tambah(path: &Path, baris: &str) {
    if path.as_os_str().is_empty() {
        return;
    }
    let _g = KUNCI.lock().unwrap_or_else(|e| e.into_inner());
    let mut isi = Vec::with_capacity(baris.len() + 2);
    isi.extend_from_slice(baris.as_bytes());
    isi.extend_from_slice(b"\r\n");
    for _ in 0..3 {
        if panjang(path) >= BATAS_BYTES {
            putar(path);
        }
        // Bawaan std di Windows: berbagi baca, tulis, dan hapus.
        let hasil = OpenOptions::new().append(true).create(true).open(path).and_then(|mut f| f.write_all(&isi));
        match hasil {
            Ok(()) => return,
            Err(e) if matches!(e.raw_os_error(), Some(ERROR_SHARING_VIOLATION | ERROR_LOCK_VIOLATION)) => {
                // Dipegang sesaat oleh pembaca lain; sebentar lagi biasanya lepas.
                std::thread::sleep(Duration::from_millis(15));
            }
            Err(_) => return,
        }
    }
}

fn panjang(path: &Path) -> u64 {
    fs::metadata(path).map(|m| m.len()).unwrap_or(0)
}

/// `phoron.log` + `.n`.
fn ke(path: &Path, n: u32) -> PathBuf {
    let mut s = OsString::from(path.as_os_str());
    s.push(format!(".{n}"));
    PathBuf::from(s)
}

/// Geser berkas jadi `.1`, `.1` jadi `.2`, dan seterusnya; yang paling tua
/// dibuang. Tiap langkah dijaga sendiri dan kegagalannya didiamkan.
pub fn putar(path: &Path) {
    let _ = fs::remove_file(ke(path, SIMPAN_LAMA));
    for i in (1..SIMPAN_LAMA).rev() {
        let dari = ke(path, i);
        if dari.is_file() {
            let tuju = ke(path, i + 1);
            let _ = fs::remove_file(&tuju);
            let _ = fs::rename(&dari, &tuju);
        }
    }
    if path.is_file() {
        let tuju = ke(path, 1);
        let _ = fs::remove_file(&tuju);
        let _ = fs::rename(path, &tuju);
    }
}

#[cfg(test)]
mod uji {
    use super::*;

    /// Folder sementara sendiri; log tidak memakai akar Phoron jadi tidak
    /// perlu kunci sandbox.
    fn folder(nama: &str) -> (crate::paths::FolderUji, PathBuf) {
        crate::paths::folder_uji(&format!("log-{nama}"))
    }

    #[test]
    fn baris_diakhiri_crlf_utf8_tanpa_bom() {
        let (_f, d) = folder("format");
        let f = d.join("phoron.log");
        tambah(&f, "satu");
        tambah(&f, "Ünik ✓");
        tambah(&f, "");
        assert_eq!(fs::read(&f).unwrap(), "satu\r\nÜnik ✓\r\n\r\n".as_bytes());
        tambah(Path::new(""), "diabaikan");
        let _ = fs::remove_dir_all(&d);
    }

    #[test]
    fn berputar_saat_batas_tercapai() {
        let (_f, d) = folder("putar");
        let f = d.join("phoron.log");
        // Satu byte di bawah batas: baris berikut masih masuk berkas yang sama.
        fs::write(&f, vec![b'a'; BATAS_BYTES as usize - 1]).unwrap();
        tambah(&f, "masih di sini");
        assert!(!ke(&f, 1).exists());
        assert_eq!(panjang(&f), BATAS_BYTES - 1 + 15);
        tambah(&f, "berkas baru");
        assert_eq!(fs::read_to_string(&f).unwrap(), "berkas baru\r\n");
        assert_eq!(panjang(&ke(&f, 1)), BATAS_BYTES + 14);
        let _ = fs::remove_dir_all(&d);
    }

    #[test]
    fn yang_tertua_dibuang() {
        let (_f, d) = folder("tertua");
        let f = d.join("x.log");
        fs::write(&f, "kini").unwrap();
        fs::write(ke(&f, 1), "satu").unwrap();
        fs::write(ke(&f, 3), "tiga").unwrap();
        putar(&f);
        assert!(!f.exists());
        assert_eq!(fs::read_to_string(ke(&f, 1)).unwrap(), "kini");
        assert_eq!(fs::read_to_string(ke(&f, 2)).unwrap(), "satu");
        assert!(!ke(&f, 3).exists(), "lubang .2 membuat .3 lama tetap dibuang");
        fs::write(&f, "baru").unwrap();
        fs::write(ke(&f, 3), "tiga").unwrap();
        putar(&f);
        assert_eq!(fs::read_to_string(ke(&f, 3)).unwrap(), "satu");
        assert!(!ke(&f, 4).exists());
        // Berkas yang belum ada: tidak terjadi apa-apa.
        putar(&d.join("tidak-ada.log"));
        let _ = fs::remove_dir_all(&d);
    }

    #[test]
    fn banyak_utas_tidak_kehilangan_baris() {
        let (_f, d) = folder("utas");
        let f = d.join("phoron.log");
        let utas: Vec<_> = (0..8)
            .map(|u| {
                let f = f.clone();
                std::thread::spawn(move || {
                    for i in 0..200 {
                        tambah(&f, &format!("utas {u} baris {i}"));
                    }
                })
            })
            .collect();
        for t in utas {
            t.join().unwrap();
        }
        let isi = fs::read_to_string(&f).unwrap();
        assert_eq!(isi.lines().count(), 1600);
        assert!(isi.lines().all(|b| b.starts_with("utas ")));
        let _ = fs::remove_dir_all(&d);
    }

    #[test]
    fn menulis_saat_berkas_dibuka_pembaca() {
        use std::os::windows::fs::OpenOptionsExt;
        let (_f, d) = folder("pembaca");
        let f = d.join("phoron.log");
        tambah(&f, "a");
        // Pembaca seperti halaman Log C#: FileShare.ReadWrite.
        let _pembaca = OpenOptions::new().read(true).share_mode(0x1 | 0x2).open(&f).unwrap();
        tambah(&f, "b");
        assert_eq!(fs::read_to_string(&f).unwrap(), "a\r\nb\r\n");
        let _ = fs::remove_dir_all(&d);
    }

    #[test]
    fn rotasi_gagal_tetap_menulis() {
        use std::os::windows::fs::OpenOptionsExt;
        let (_f, d) = folder("gagal");
        let f = d.join("phoron.log");
        fs::write(&f, vec![b'a'; BATAS_BYTES as usize]).unwrap();
        // Pembaca tanpa izin hapus: berkas tidak bisa digeser.
        let pembaca = OpenOptions::new().read(true).share_mode(0x1 | 0x2).open(&f).unwrap();
        tambah(&f, "tetap tercatat");
        assert!(!ke(&f, 1).exists());
        assert_eq!(panjang(&f), BATAS_BYTES + 16);
        drop(pembaca);
        tambah(&f, "sekarang berputar");
        assert_eq!(fs::read_to_string(&f).unwrap(), "sekarang berputar\r\n");
        let _ = fs::remove_dir_all(&d);
    }
}
