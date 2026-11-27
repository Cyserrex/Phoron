//! Padanan `Settings.cs`: setelan global di `<akar>\phoron.ini`.
//!
//! Nama kunci, bawaan, dan urutan penulisan SAMA dengan versi C#.

use crate::atomic_file;
use crate::ini::Ini;
use crate::paths;
use crate::rahasia;
use crate::waktu::Saat;
use serde::{Deserialize, Serialize};
use std::io;
use std::sync::Mutex;

/// Banyaknya situs terakhir yang diingat.
pub const BATAS_SITUS_TERAKHIR: usize = 5;

/// Kode bahasa yang dikenal UI. Daemon tidak menerjemahkan apa pun, tapi
/// harus menolak kode asing persis seperti `Lang.Sah` supaya phoron.ini yang
/// ditulisnya sama dengan buatan C#.
pub const BAHASA_SAH: &[&str] = &["id", "en", "jv", "bjn"];

const KEPALA: &str =
    "Setelan Phoron. Berkas ini juga jadi penanda akar instalasi -\njangan dipindah dari folder Phoron.";

/// Dari mana setelan ini berasal - menentukan apakah aman ditimpa.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, Default)]
pub enum Sumber {
    #[default]
    Baru,
    Terbaca,
    Rusak,
    TidakTerbaca,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Settings {
    /// Folder-folder yang dipindai untuk mencari versi.
    pub bin_roots: Vec<String>,
    /// Folder situs PHP yang terakhir dibuka lewat Phoron, terbaru dulu.
    pub situs_terakhir: Vec<String>,
    pub active_profile: String,
    pub auto_start_services: bool,
    pub minimize_to_tray: bool,
    /// Bawaan MATI: alamat .test butuh hosts, dan hosts butuh Administrator.
    pub auto_vhost: bool,
    /// Bawaan HIDUP, dan sengaja tidak ikut daftar ekstensi profil.
    pub opcache: bool,
    pub manage_hosts: bool,
    pub php_ini_ke_folder_php: bool,
    pub log_rinci: bool,
    pub beranda_di_akar: bool,
    pub tema: String,
    pub jendela_lebar: i32,
    pub jendela_tinggi: i32,
    pub bahasa: String,
    pub cek_pembaruan: bool,
    /// Kapan terakhir kali GitHub ditanya; None = belum pernah.
    pub cek_terakhir: Option<Saat>,
    pub terminal: String,
    pub editor: String,
    pub db_pengguna: String,
    pub heidi_sql: String,
    /// Sandi MySQL dalam bentuk terbuka. Di phoron.ini selalu tersandi.
    #[serde(skip)]
    pub db_sandi: String,

    #[serde(skip)]
    sandi_tersandi_saat_muat: String,
    #[serde(skip)]
    sandi_terbuka_saat_muat: String,
    #[serde(skip)]
    pub asal_muat: Sumber,
}

impl Default for Settings {
    fn default() -> Self {
        Settings {
            bin_roots: Vec::new(),
            situs_terakhir: Vec::new(),
            active_profile: String::new(),
            auto_start_services: false,
            minimize_to_tray: true,
            auto_vhost: false,
            opcache: true,
            manage_hosts: true,
            php_ini_ke_folder_php: false,
            log_rinci: false,
            beranda_di_akar: true,
            tema: "sistem".into(),
            jendela_lebar: 0,
            jendela_tinggi: 0,
            bahasa: "id".into(),
            cek_pembaruan: true,
            cek_terakhir: None,
            terminal: "cmd".into(),
            editor: String::new(),
            db_pengguna: "root".into(),
            heidi_sql: String::new(),
            db_sandi: String::new(),
            sandi_tersandi_saat_muat: String::new(),
            sandi_terbuka_saat_muat: String::new(),
            asal_muat: Sumber::Baru,
        }
    }
}

#[derive(Debug)]
pub struct HasilMuat {
    pub setelan: Settings,
    pub asal: Sumber,
    pub keluhan: Vec<String>,
}

/// Save() adalah baca-ubah-tulis dan dipanggil dari beberapa tempat; tanpa
/// kunci, dua yang berpapasan membuat salah satu suntingan hilang.
static KUNCI_SIMPAN: Mutex<()> = Mutex::new(());

/// Terisi bila berkas cacat terpaksa dikarantina saat menyimpan.
pub static KELUHAN_TERAKHIR: Mutex<String> = Mutex::new(String::new());

fn pisah(s: &str, pemisah: char) -> Vec<String> {
    s.split(pemisah).map(str::trim).filter(|x| !x.is_empty()).map(String::from).collect()
}

impl Settings {
    pub fn load() -> Settings {
        Self::muat().setelan
    }

    /// Muat setelan sambil melaporkan keadaan berkasnya. "Berkasnya tidak
    /// ada" dan "berkasnya ada tapi cacat" HARUS dibedakan: yang kedua tidak
    /// boleh ditimpa bawaan diam-diam.
    pub fn muat() -> HasilMuat {
        let baca = Ini::baca(&paths::settings_file());
        let asal = if !baca.ada {
            Sumber::Baru
        } else if baca.gagal_baca {
            Sumber::TidakTerbaca
        } else if baca.rusak {
            Sumber::Rusak
        } else {
            Sumber::Terbaca
        };
        let mut s = Self::muat_dari(&baca.isi);
        s.asal_muat = asal;
        HasilMuat { setelan: s, asal, keluhan: baca.keluhan }
    }

    fn muat_dari(ini: &Ini) -> Settings {
        let mut s = Settings::default();
        s.bin_roots = pisah(ini.get_or("umum", "bin_roots", ""), ';');
        if s.bin_roots.is_empty() {
            s.bin_roots = default_bin_roots();
        }
        s.active_profile = ini.get_or("umum", "profil_aktif", "").to_string();
        // "|" sebagai pemisah: satu-satunya aksara yang mustahil ada di jalur Windows.
        s.situs_terakhir = pisah(ini.get_or("umum", "situs_terakhir", ""), '|');
        s.situs_terakhir.truncate(BATAS_SITUS_TERAKHIR);
        s.auto_start_services = ini.get_bool("umum", "auto_start", false);
        s.minimize_to_tray = ini.get_bool("umum", "minimize_ke_tray", true);
        s.auto_vhost = ini.get_bool("umum", "auto_vhost", false);
        s.opcache = ini.get_bool("umum", "opcache", true);
        s.manage_hosts = ini.get_bool("umum", "kelola_hosts", true);
        s.php_ini_ke_folder_php = ini.get_bool("umum", "php_ini_ke_folder_php", false);
        s.log_rinci = ini.get_bool("umum", "log_rinci", false);
        s.beranda_di_akar = ini.get_bool("umum", "beranda_di_akar", true);
        s.tema = ini.get_or("umum", "tema", "sistem").to_string();
        s.jendela_lebar = ini.get_int("umum", "jendela_lebar", 0).max(0);
        s.jendela_tinggi = ini.get_int("umum", "jendela_tinggi", 0).max(0);
        s.bahasa = ini.get_or("umum", "bahasa", "id").to_string();
        if !BAHASA_SAH.contains(&s.bahasa.as_str()) {
            s.bahasa = "id".into();
        }
        s.cek_pembaruan = ini.get_bool("umum", "cek_pembaruan", true);
        s.cek_terakhir = Saat::urai_o(ini.get_or("umum", "cek_terakhir", ""));
        s.terminal = ini.get_or("umum", "terminal", "cmd").to_string();
        s.editor = ini.get_or("umum", "editor", "").to_string();
        s.db_pengguna = ini.get_or("basisdata", "pengguna", "root").to_string();
        s.heidi_sql = ini.get_or("basisdata", "heidisql", "").to_string();
        s.sandi_tersandi_saat_muat = ini.get_or("basisdata", "sandi", "").to_string();
        s.db_sandi = rahasia::buka(&s.sandi_tersandi_saat_muat);
        s.sandi_terbuka_saat_muat = s.db_sandi.clone();
        s
    }

    pub fn save(&mut self) -> io::Result<()> {
        let _g = KUNCI_SIMPAN.lock().unwrap_or_else(|e| e.into_inner());
        self.simpan_inti()
    }

    fn simpan_inti(&mut self) -> io::Result<()> {
        let berkas = paths::settings_file();
        // Berkas cacat disingkirkan dulu supaya sisa setelannya masih bisa
        // dilihat. Menolak menulis sama sekali lebih buruk: cek_terakhir tidak
        // pernah tersimpan, dan Phoron menanyai GitHub tiap kali start.
        if matches!(self.asal_muat, Sumber::Rusak | Sumber::TidakTerbaca) {
            let salinan = atomic_file::karantina(&berkas, "rusak");
            *KELUHAN_TERAKHIR.lock().unwrap() = match salinan {
                Some(p) => format!(
                    "phoron.ini tidak terbaca utuh; salinannya disimpan sebagai {} sebelum ditulis ulang.",
                    p.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default()
                ),
                None => "phoron.ini tidak terbaca utuh dan tidak bisa dikarantina; setelan ditulis ulang dari awal."
                    .into(),
            };
            self.asal_muat = Sumber::Baru; // sekali saja, bukan tiap penyimpanan
        }

        let mut ini = Ini::load(&berkas)?;
        let b = |v: bool| if v { "1" } else { "0" };
        ini.set("umum", "bin_roots", &self.bin_roots.join(";"));
        ini.set("umum", "profil_aktif", &self.active_profile);
        ini.set("umum", "situs_terakhir", &self.situs_terakhir.join("|"));
        ini.set("umum", "auto_start", b(self.auto_start_services));
        ini.set("umum", "minimize_ke_tray", b(self.minimize_to_tray));
        ini.set("umum", "auto_vhost", b(self.auto_vhost));
        ini.set("umum", "opcache", b(self.opcache));
        ini.set("umum", "kelola_hosts", b(self.manage_hosts));
        ini.set("umum", "php_ini_ke_folder_php", b(self.php_ini_ke_folder_php));
        ini.set("umum", "log_rinci", b(self.log_rinci));
        ini.set("umum", "beranda_di_akar", b(self.beranda_di_akar));
        ini.set("umum", "tema", &self.tema);
        ini.set("umum", "jendela_lebar", &self.jendela_lebar.to_string());
        ini.set("umum", "jendela_tinggi", &self.jendela_tinggi.to_string());
        ini.set("umum", "bahasa", &self.bahasa);
        ini.set("umum", "cek_pembaruan", b(self.cek_pembaruan));
        // Token lama (sampai 1.20.x) dibuang AKTIF, bukan sekadar berhenti ditulis.
        ini.remove("umum", "token_github");
        ini.set("umum", "cek_terakhir", &self.cek_terakhir.map(|s| s.ke_o()).unwrap_or_default());
        ini.set("umum", "terminal", &self.terminal);
        ini.set("umum", "editor", &self.editor);
        let pengguna = if self.db_pengguna.is_empty() { "root" } else { &self.db_pengguna };
        ini.set("basisdata", "pengguna", pengguna);
        ini.set("basisdata", "heidisql", &self.heidi_sql);
        let tersandi = rahasia::tutup_untuk_simpan(
            &self.db_sandi,
            &self.sandi_tersandi_saat_muat,
            &self.sandi_terbuka_saat_muat,
        );
        ini.set("basisdata", "sandi", &tersandi);
        self.sandi_tersandi_saat_muat = tersandi;
        self.sandi_terbuka_saat_muat = self.db_sandi.clone();
        ini.save(&berkas, Some(KEPALA))
    }
}

/// Folder bin Phoron sendiri selalu pertama - itulah yang menang saat ada
/// nama folder kembar - lalu pengelola lain yang terpasang.
pub fn default_bin_roots() -> Vec<String> {
    let mut v = vec![paths::bin().to_string_lossy().into_owned()];
    v.extend(crate::deteksi::folder_bin_terpasang());
    v
}

#[cfg(test)]
mod uji {
    use super::*;
    use std::fs;

    #[test]
    fn berkas_baru_memakai_bawaan() {
        let (_g, d) = paths::sandbox_uji("setelan-baru");
        let h = Settings::muat();
        assert_eq!(h.asal, Sumber::Baru);
        assert!(h.setelan.minimize_to_tray && h.setelan.opcache && !h.setelan.auto_vhost);
        assert_eq!(h.setelan.bin_roots[0], d.join("bin").to_string_lossy());
        let _ = fs::remove_dir_all(&d);
    }

    #[test]
    fn simpan_mempertahankan_kunci_asing_dan_membuang_token() {
        let (_g, d) = paths::sandbox_uji("setelan-asing");
        fs::write(
            d.join("phoron.ini"),
            "[umum]\r\nbahasa=en\r\ntoken_github=abc\r\nkunci_masa_depan=1\r\n",
        )
        .unwrap();
        let mut s = Settings::load();
        assert_eq!(s.bahasa, "en");
        s.save().unwrap();
        let isi = fs::read_to_string(d.join("phoron.ini")).unwrap();
        assert!(isi.contains("kunci_masa_depan=1"));
        assert!(!isi.contains("token_github"));
        let _ = fs::remove_dir_all(&d);
    }

    #[test]
    fn berkas_rusak_dikarantina_sebelum_ditimpa() {
        let (_g, d) = paths::sandbox_uji("setelan-rusak");
        fs::write(d.join("phoron.ini"), "[umum]\r\nbahasa=en\r\n\0\0\0\0").unwrap();
        let mut s = Settings::load();
        assert_eq!(s.asal_muat, Sumber::Rusak);
        s.save().unwrap();
        let salinan = fs::read_dir(&d)
            .unwrap()
            .filter_map(|e| e.ok())
            .any(|e| e.file_name().to_string_lossy().starts_with("phoron.ini.rusak-"));
        assert!(salinan);
        let _ = fs::remove_dir_all(&d);
    }

    #[test]
    fn bahasa_asing_jatuh_ke_indonesia() {
        let (_g, d) = paths::sandbox_uji("setelan-bahasa");
        fs::write(d.join("phoron.ini"), "[umum]\r\nbahasa=xx\r\n").unwrap();
        assert_eq!(Settings::load().bahasa, "id");
        let _ = fs::remove_dir_all(&d);
    }
}
