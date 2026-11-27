//! Penyiar: mengirim `Kabar` ke semua klien yang sudah ber-Halo.
//!
//! Setiap klien punya utas penulis sendiri dengan antrean terbatas. `siar`
//! hanya memasukkan baris ke antrean masing-masing (`try_send`) dan tidak
//! pernah menunggu: klien yang lambat atau macet tidak bisa menahan klien
//! lain, apalagi menahan Engine yang menyiarkan. Klien yang antreannya penuh
//! diputus - kabar yang terlewat membuat tampilannya salah, dan UI memang
//! menyambung ulang lalu meminta `Keadaan` lengkap.

use crate::pipa::Pipa;
use phoron_inti::protokol::{baris, Kabar};
use std::collections::HashMap;
use std::sync::mpsc::{SyncSender, TrySendError};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

/// Isi antrean penulis satu klien.
pub(crate) enum Keluaran {
    /// Satu baris JSON lengkap dengan `\n`. Dibagi bersama antarklien supaya
    /// satu kabar diserialisasi sekali saja.
    Baris(Arc<str>),
    /// Tulis sisa antrean lalu berhenti (daemon akan keluar).
    Akhir,
}

/// Panjang antrean per klien. Kabar datang paling banyak puluhan per detik
/// (baris log, keluaran Node); klien sehat menguras antrean jauh lebih cepat.
pub(crate) const PANJANG_ANTREAN: usize = 256;

struct Penerima {
    tx: SyncSender<Keluaran>,
    pipa: Arc<Pipa>,
}

#[derive(Clone, Default)]
pub struct Penyiar {
    klien: Arc<Mutex<HashMap<u64, Penerima>>>,
}

impl Penyiar {
    pub fn baru() -> Self {
        Penyiar::default()
    }

    /// Kirim kabar ke semua klien yang tersambung. Tidak pernah memblokir.
    pub fn siar(&self, kabar: &Kabar) {
        let teks: Arc<str> = baris(kabar).into();
        let mut peta = self.klien.lock().unwrap_or_else(|e| e.into_inner());
        let mut buang = Vec::new();
        for (id, p) in peta.iter() {
            match p.tx.try_send(Keluaran::Baris(teks.clone())) {
                Ok(()) => {}
                Err(TrySendError::Full(_)) | Err(TrySendError::Disconnected(_)) => buang.push(*id),
            }
        }
        for id in buang {
            if let Some(p) = peta.remove(&id) {
                p.pipa.putus();
            }
        }
    }

    /// Jumlah klien yang menerima kabar (untuk Engine, mis. menunda kerja
    /// yang hanya berguna bila ada UI terbuka).
    #[allow(dead_code)]
    pub fn jumlah_klien(&self) -> usize {
        self.klien.lock().unwrap_or_else(|e| e.into_inner()).len()
    }

    pub(crate) fn daftar(&self, id: u64, tx: SyncSender<Keluaran>, pipa: Arc<Pipa>) {
        self.klien.lock().unwrap_or_else(|e| e.into_inner()).insert(id, Penerima { tx, pipa });
    }

    pub(crate) fn lepas(&self, id: u64) {
        self.klien.lock().unwrap_or_else(|e| e.into_inner()).remove(&id);
    }

    /// Dipakai jalur keluar sesudah `Kabar::Pamit`: minta semua penulis
    /// menuntaskan antreannya, lalu tunggu paling lama `batas`. Klien yang
    /// tidak membaca tidak boleh menunda keluarnya daemon lebih dari itu.
    pub(crate) fn tutup_semua(&self, batas: Duration) {
        let semua: Vec<Penerima> = {
            let mut peta = self.klien.lock().unwrap_or_else(|e| e.into_inner());
            peta.drain().map(|(_, p)| p).collect()
        };
        for p in &semua {
            if p.tx.try_send(Keluaran::Akhir).is_err() {
                p.pipa.putus();
            }
        }
        let sampai = Instant::now() + batas;
        for p in &semua {
            if !p.pipa.tunggu_penulis(sampai) {
                p.pipa.putus();
            }
        }
    }
}
