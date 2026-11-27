//! Nama objek bernama milik daemon: mutex instans tunggal, pipe, dan sinyal
//! keluar.
//!
//! Untuk uji, ketiganya diberi akhiran (env `PHORON_NAMA_UJI` atau argumen
//! `--pipe-uji <akhiran>`), supaya daemon uji tidak pernah menyentuh Phoron
//! yang sedang dipakai pengguna: tidak merebut mutexnya, tidak menjawab di
//! pipenya, dan tidak ikut keluar saat pemasang memberi sinyal.

use phoron_inti::protokol::NAMA_PIPE;

pub const MUTEX: &str = "Phoron.Daemon";
/// Event manual-reset yang dipakai pemasang (installer/setup.iss) untuk
/// meminta Phoron menutup diri sebelum berkasnya ditimpa.
pub const SINYAL_KELUAR: &str = "Phoron.KeluarSekarang";

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Nama {
    pub mutex: String,
    pub pipe: String,
    pub sinyal_keluar: String,
    /// Akhiran uji; kosong = nama sungguhan.
    pub akhiran: String,
}

impl Nama {
    pub fn dengan_akhiran(akhiran: &str) -> Nama {
        Nama {
            mutex: format!("{MUTEX}{akhiran}"),
            pipe: format!("{NAMA_PIPE}{akhiran}"),
            sinyal_keluar: format!("{SINYAL_KELUAR}{akhiran}"),
            akhiran: akhiran.to_string(),
        }
    }

    pub fn uji(&self) -> bool {
        !self.akhiran.is_empty()
    }
}

/// UTF-16 berakhiran nol untuk API Win32.
pub fn lebar(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}

#[cfg(test)]
mod uji {
    use super::*;

    #[test]
    fn akhiran_dipasang_di_ketiga_nama() {
        let n = Nama::dengan_akhiran("-uji7");
        assert_eq!(n.mutex, "Phoron.Daemon-uji7");
        assert_eq!(n.pipe, r"\\.\pipe\phoron-uji7");
        assert_eq!(n.sinyal_keluar, "Phoron.KeluarSekarang-uji7");
        assert!(n.uji());
        assert!(!Nama::dengan_akhiran("").uji());
    }
}
