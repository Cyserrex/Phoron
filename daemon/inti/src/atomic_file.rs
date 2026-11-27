//! Padanan `AtomicFile.cs`: tulis-lalu-ganti, sehingga berkas tujuan tidak
//! pernah ada dalam keadaan setengah tertulis.
//!
//! Penulisan biasa memotong berkas tujuan lebih dulu, lalu mengisinya. Kalau
//! proses mati, listrik padam, atau antivirus menyela di celah itu, yang
//! tertinggal berkas nol byte - untuk phoron.ini, profil, dan terutama berkas
//! hosts Windows, itu berarti setelan lenyap tanpa satu pun galat.

use std::fs::{self, OpenOptions};
use std::io::{self, Write};
use std::os::windows::ffi::OsStrExt;
use std::os::windows::fs::OpenOptionsExt;
use std::path::{Path, PathBuf};
use windows_sys::Win32::Storage::FileSystem::{
    GetFileAttributesW, ReplaceFileW, SetFileAttributesW, FILE_ATTRIBUTE_HIDDEN,
    FILE_ATTRIBUTE_NORMAL, FILE_ATTRIBUTE_READONLY, FILE_ATTRIBUTE_SYSTEM,
    FILE_FLAG_WRITE_THROUGH, INVALID_FILE_ATTRIBUTES, REPLACEFILE_IGNORE_ACL_ERRORS,
    REPLACEFILE_IGNORE_MERGE_ERRORS,
};

/// Akhiran berkas sementara, memuat PID: Phoron bisa punya dua proses
/// sekaligus (utama dan pembantu ber-Administrator untuk hosts), dan tanpa
/// PID keduanya akan berebut berkas sementara yang sama. Formatnya sama
/// dengan C# (".ptmp" + PID), jadi sisa berkas sementara dari versi mana pun
/// dikenali dengan cara yang sama.
fn akhiran_tmp() -> String {
    format!(".ptmp{}", std::process::id())
}

pub fn write_all_text(path: &Path, isi: &str) -> io::Result<()> {
    write_all_bytes(path, isi.as_bytes())
}

pub(crate) fn lebar(p: &Path) -> Vec<u16> {
    p.as_os_str().encode_wide().chain(Some(0)).collect()
}

pub fn write_all_bytes(path: &Path, bytes: &[u8]) -> io::Result<()> {
    if path.as_os_str().is_empty() {
        return Err(io::Error::new(io::ErrorKind::InvalidInput, "path kosong"));
    }
    if let Some(dir) = path.parent().filter(|d| !d.as_os_str().is_empty()) {
        fs::create_dir_all(dir)?;
    }

    // Berkas sementara sengaja di direktori YANG SAMA: ReplaceFile menolak
    // bekerja lintas volume, dan %TEMP% kerap ada di drive lain.
    let mut tmp = path.as_os_str().to_owned();
    tmp.push(akhiran_tmp());
    let tmp = PathBuf::from(tmp);

    let mut atribut: Option<u32> = None;
    let hasil = (|| -> io::Result<()> {
        {
            // WRITE_THROUGH + sync_all inilah yang menurunkan isinya ke
            // piringan; tanpa itu yang dijamin hanya selamat dari matinya
            // proses, bukan matinya listrik.
            let mut f = OpenOptions::new()
                .write(true)
                .create(true)
                .truncate(true)
                .custom_flags(FILE_FLAG_WRITE_THROUGH)
                .share_mode(0)
                .open(&tmp)?;
            f.write_all(bytes)?;
            f.sync_all()?;
        }

        // Tujuan belum ada: rename di dalam satu direktori juga satu langkah.
        if !path.exists() {
            return fs::rename(&tmp, path);
        }

        let tujuan = lebar(path);
        let a = unsafe { GetFileAttributesW(tujuan.as_ptr()) };
        if a != INVALID_FILE_ATTRIBUTES {
            atribut = Some(a);
            // Berkas hosts Windows hampir selalu ber-ReadOnly, dan ReplaceFile
            // menolak tujuan yang read-only.
            if a & (FILE_ATTRIBUTE_READONLY | FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM) != 0 {
                unsafe { SetFileAttributesW(tujuan.as_ptr(), FILE_ATTRIBUTE_NORMAL) };
            }
        }

        let sumber = lebar(&tmp);
        // Sama dengan File.Replace(..., ignoreMetadataErrors: true): berkas
        // hosts punya ACL yang tidak lazim, dan penyalinan metadatanya bisa
        // gagal walau penukaran datanya berhasil.
        let ok = unsafe {
            ReplaceFileW(
                tujuan.as_ptr(),
                sumber.as_ptr(),
                std::ptr::null(),
                REPLACEFILE_IGNORE_MERGE_ERRORS | REPLACEFILE_IGNORE_ACL_ERRORS,
                std::ptr::null(),
                std::ptr::null(),
            )
        };
        if ok == 0 {
            // Sistem berkas tanpa penukaran (FAT32, berbagi jaringan). Tidak
            // atomik, tapi persis perilaku sebelum AtomicFile ada.
            fs::copy(&tmp, path)?;
        }
        Ok(())
    })();

    if let Some(a) = atribut {
        if path.exists() {
            let t = lebar(path);
            unsafe { SetFileAttributesW(t.as_ptr(), a) };
        }
    }
    if tmp.exists() {
        let _ = fs::remove_file(&tmp);
    }
    hasil
}

/// Pindahkan berkas yang isinya tidak masuk akal ke samping, dan kembalikan
/// nama barunya. Dipakai sebelum menimpa berkas yang terbaca cacat: menimpanya
/// begitu saja membuang sisa setelan yang masih selamat.
pub fn karantina(path: &Path, sebab: &str) -> Option<PathBuf> {
    if !path.is_file() {
        return None;
    }
    let sebab = if sebab.trim().is_empty() { "rusak" } else { sebab.trim() };
    let mut dasar = path.as_os_str().to_owned();
    dasar.push(format!(".{}-{}", sebab, crate::waktu::cap_lokal_berkas()));
    let dasar = PathBuf::from(dasar);
    // Dua kali dalam satu detik tetap harus menghasilkan dua berkas.
    let mut calon = dasar.clone();
    let mut n = 1;
    while calon.exists() {
        n += 1;
        let mut s = dasar.as_os_str().to_owned();
        s.push(format!("-{n}"));
        calon = PathBuf::from(s);
    }
    fs::rename(path, &calon).ok()?;
    Some(calon)
}

#[cfg(test)]
mod uji {
    use super::*;

    fn sandbox(nama: &str) -> PathBuf {
        let d = std::env::temp_dir().join(format!("phoron-rs-{}-{}", nama, std::process::id()));
        let _ = fs::remove_dir_all(&d);
        fs::create_dir_all(&d).unwrap();
        d
    }

    #[test]
    fn menulis_baru_dan_mengganti() {
        let d = sandbox("atomik");
        let f = d.join("a.ini");
        write_all_text(&f, "satu").unwrap();
        write_all_text(&f, "dua").unwrap();
        assert_eq!(fs::read_to_string(&f).unwrap(), "dua");
        // Tidak ada sisa berkas sementara.
        assert_eq!(fs::read_dir(&d).unwrap().count(), 1);
        fs::remove_dir_all(&d).unwrap();
    }

    #[test]
    fn tujuan_readonly_tetap_diganti_dan_atributnya_kembali() {
        let d = sandbox("ro");
        let f = d.join("hosts");
        fs::write(&f, "lama").unwrap();
        let mut izin = fs::metadata(&f).unwrap().permissions();
        izin.set_readonly(true);
        fs::set_permissions(&f, izin).unwrap();

        write_all_text(&f, "baru").unwrap();
        assert_eq!(fs::read_to_string(&f).unwrap(), "baru");
        assert!(fs::metadata(&f).unwrap().permissions().readonly());

        let mut izin = fs::metadata(&f).unwrap().permissions();
        #[allow(clippy::permissions_set_readonly_false)]
        izin.set_readonly(false);
        fs::set_permissions(&f, izin).unwrap();
        fs::remove_dir_all(&d).unwrap();
    }

    #[test]
    fn karantina_dua_kali_tidak_bertabrakan() {
        let d = sandbox("karantina");
        let f = d.join("phoron.ini");
        fs::write(&f, "x").unwrap();
        let a = karantina(&f, "rusak").unwrap();
        fs::write(&f, "y").unwrap();
        let b = karantina(&f, "rusak").unwrap();
        assert_ne!(a, b);
        assert!(!f.exists());
        fs::remove_dir_all(&d).unwrap();
    }
}
