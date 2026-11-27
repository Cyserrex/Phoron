//! Nama objek bernama milik daemon: mutex instans tunggal, pipe, dan sinyal
//! keluar.
//!
//! Untuk uji, ketiganya diberi akhiran (env `PHORON_NAMA_UJI` atau argumen
//! `--pipe-uji <akhiran>`), supaya daemon uji tidak pernah menyentuh Phoron
//! yang sedang dipakai pengguna: tidak merebut mutexnya, tidak menjawab di
//! pipenya, dan tidak ikut keluar saat pemasang memberi sinyal.
//!
//! Nama pipe juga bisa diganti utuh lewat env `PHORON_PIPE` (nama tanpa
//! `\\.\pipe\`, diturunkan KlienDaemon C# saat menjalankan phorond) atau
//! argumen `--pipe <nama>`. Tanpa akhiran uji, nama pipe lain itu juga
//! menurunkan akhiran untuk mutex dan sinyal keluar (lihat [`Nama::dari`]),
//! supaya daemon uji UI tidak bertabrakan dengan mutex Phoron yang dipakai.

use phoron_inti::protokol::NAMA_PIPE;

pub const MUTEX: &str = "Phoron.Daemon";
/// Event AUTO-reset (sama seperti UI WPF lama) yang dipakai pemasang (installer/setup.iss) untuk
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

    /// `akhiran` dari `--pipe-uji`/`PHORON_NAMA_UJI` (boleh kosong), `pipe`
    /// dari `--pipe`/`PHORON_PIPE` (boleh kosong atau "phoron" = bawaan).
    pub fn dari(akhiran: &str, pipe: &str) -> Nama {
        let pipe = pipe.trim();
        let pendek = pipe.strip_prefix(r"\\.\pipe\").unwrap_or(pipe);
        if pendek.is_empty() || pendek.eq_ignore_ascii_case("phoron") {
            return Nama::dengan_akhiran(akhiran);
        }
        let akhiran = if !akhiran.is_empty() {
            akhiran.to_string()
        } else {
            match pendek.get(..6) {
                Some(awal) if awal.eq_ignore_ascii_case("phoron") => pendek[6..].to_string(),
                _ => format!("-{pendek}"),
            }
        };
        let mut n = Nama::dengan_akhiran(&akhiran);
        n.pipe = format!(r"\\.\pipe\{pendek}");
        n
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

    #[test]
    fn pipe_dari_lingkungan() {
        assert_eq!(Nama::dari("", ""), Nama::dengan_akhiran(""));
        assert_eq!(Nama::dari("", "phoron"), Nama::dengan_akhiran(""));
        let n = Nama::dari("", "phoron-uji-abc");
        assert_eq!(n.pipe, r"\\.\pipe\phoron-uji-abc");
        assert_eq!(n.mutex, "Phoron.Daemon-uji-abc");
        assert_eq!(n.sinyal_keluar, "Phoron.KeluarSekarang-uji-abc");
        let n = Nama::dari("", r"\\.\pipe\lain");
        assert_eq!(n.pipe, r"\\.\pipe\lain");
        assert_eq!(n.mutex, "Phoron.Daemon-lain");
        // Akhiran eksplisit menang untuk mutex/sinyal; pipe tetap dari env.
        let n = Nama::dari("-x", "pipa-y");
        assert_eq!((n.pipe.as_str(), n.mutex.as_str()), (r"\\.\pipe\pipa-y", "Phoron.Daemon-x"));
    }
}
