//! Padanan `Models.cs`: paket bin, profil, situs, dan keadaan layanan.

use serde::{Deserialize, Serialize};
use std::cmp::Ordering;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub enum BinKind {
    Php,
    Apache,
    Nginx,
    MySql,
    Node,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum ServiceKind {
    Web,
    Db,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum ServiceState {
    Berhenti,
    Menyalakan,
    Jalan,
    Mematikan,
    Gagal,
}

impl ServiceState {
    /// `Engine.Hidup`: Menyalakan dan Jalan dihitung hidup; Berhenti, Gagal,
    /// dan Mematikan tidak.
    pub fn hidup(self) -> bool {
        matches!(self, ServiceState::Menyalakan | ServiceState::Jalan)
    }
}

/// `System.Version` versi sederhana: 2-4 komponen bilangan bulat non-negatif.
/// Komponen ke-4 yang tidak ada bernilai -1, sehingga "1.2.3" < "1.2.3.0",
/// sama seperti .NET.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct Versi(pub i32, pub i32, pub i32, pub i32);

impl Versi {
    pub const NOL: Versi = Versi(0, 0, 0, -1);

    /// Seperti `BinPackage.Parsed`: dinormalkan ke minimal tiga komponen, lalu
    /// `Version.TryParse`; gagal = 0.0.0.
    pub fn dari(v: &str) -> Versi {
        let v = if v.is_empty() { "0.0.0" } else { v };
        let mut bagian: Vec<&str> = v.split('.').collect();
        while bagian.len() < 3 {
            bagian.push("0");
        }
        if bagian.len() > 4 {
            return Versi::NOL;
        }
        let mut n = [0i32, 0, 0, -1];
        for (i, b) in bagian.iter().enumerate() {
            match b.trim().parse::<i32>() {
                // int.TryParse(NumberStyles.Integer): spasi dan tanda + diterima.
                Ok(x) if x >= 0 => n[i] = x,
                _ => return Versi::NOL,
            }
        }
        Versi(n[0], n[1], n[2], n[3])
    }
}

impl PartialOrd for Versi {
    fn partial_cmp(&self, o: &Self) -> Option<Ordering> {
        Some(self.cmp(o))
    }
}

impl Ord for Versi {
    fn cmp(&self, o: &Self) -> Ordering {
        (self.0, self.1, self.2, self.3).cmp(&(o.0, o.1, o.2, o.3))
    }
}

/// Satu folder versi hasil pindaian, mis. php-8.3.12-Win32-vs16-x64.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct BinPackage {
    pub kind: BinKind,
    /// Nama folder apa adanya - jadi kunci di berkas profil.
    pub id: String,
    pub path: String,
    pub version: String,
    /// Toolset kompilator: VC11, VC15, VS16, ... Kosong bila tidak tertera.
    pub compiler: String,
    /// x64 atau x86.
    pub arch: String,
    /// PHP saja: build Thread Safe (punya php*apache2_4.dll).
    pub thread_safe: bool,
    pub main_exe: String,
    pub apache_module_dll: String,
    pub source_root: String,
    /// Identitas unik di antara semua paket; kosong = sama dengan `id`.
    /// Nama folder tidak unik (php-8.3.12 di bin Phoron DAN Laragon, "mysql"
    /// di dua XAMPP), jadi kembarannya diberi tanda asal: "mysql@C-xampp".
    pub kunci: String,
    /// Ada paket sejenis lain dengan nama folder yang sama.
    pub kembar: bool,
}

impl BinPackage {
    pub fn baru(kind: BinKind, id: &str, path: &str, version: &str) -> Self {
        BinPackage {
            kind,
            id: id.into(),
            path: path.into(),
            version: version.into(),
            compiler: String::new(),
            arch: String::new(),
            thread_safe: false,
            main_exe: String::new(),
            apache_module_dll: String::new(),
            source_root: String::new(),
            kunci: String::new(),
            kembar: false,
        }
    }

    pub fn kunci(&self) -> &str {
        if self.kunci.is_empty() { &self.id } else { &self.kunci }
    }

    /// Yang disimpan ke profil: nama folder bila unik (profil tetap bisa dibawa
    /// ke komputer lain), jalur lengkap bila kembar.
    pub fn nilai_simpan(&self) -> &str {
        if self.kembar { &self.path } else { &self.id }
    }

    pub fn parsed(&self) -> Versi {
        Versi::dari(&self.version)
    }

    /// Label pendek untuk daftar: "8.3.12 · VS16 · x64 · TS".
    pub fn label(&self) -> String {
        let mut bits = vec![self.version.clone()];
        if !self.compiler.is_empty() {
            bits.push(self.compiler.to_uppercase());
        }
        if !self.arch.is_empty() {
            bits.push(self.arch.clone());
        }
        if self.kind == BinKind::Php {
            bits.push(if self.thread_safe { "TS" } else { "NTS" }.into());
        }
        bits.join(" · ")
    }
}

/// Template setting yang bisa di-switch: kombinasi versi PHP + web server +
/// database + port. Satu berkas .ini per profil di folder profiles\.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Profile {
    pub name: String,
    /// Nama berkas tanpa .ini.
    pub file_name: String,
    pub php_id: String,
    pub apache_id: String,
    pub nginx_id: String,
    pub mysql_id: String,
    /// "apache" atau "nginx".
    pub web_server: String,
    pub http_port: i32,
    pub https_port: i32,
    pub mysql_port: i32,
    /// Folder yang dipindai untuk mencari situs; kosong = <akar>\www. Yang
    /// pertama jadi akar utama (http://localhost).
    pub project_roots: Vec<String>,
    pub site_suffix: String,
    /// Ekstensi PHP yang dinyalakan (nama tanpa awalan php_).
    pub php_extensions: Vec<String>,
    /// Penimpaan php.ini, urutan dipertahankan, kunci tidak peka besar-kecil.
    pub php_ini_overrides: Vec<(String, String)>,
    /// Versi PHP khusus per situs: jalur folder situs -> Id paket PHP.
    pub php_per_situs: Vec<(String, String)>,
    pub notes: String,
}

impl Default for Profile {
    fn default() -> Self {
        Profile {
            name: "Baru".into(),
            file_name: String::new(),
            php_id: String::new(),
            apache_id: String::new(),
            nginx_id: String::new(),
            mysql_id: String::new(),
            web_server: "apache".into(),
            http_port: 80,
            https_port: 443,
            mysql_port: 3306,
            project_roots: Vec::new(),
            site_suffix: "test".into(),
            php_extensions: Vec::new(),
            php_ini_overrides: Vec::new(),
            php_per_situs: Vec::new(),
            notes: String::new(),
        }
    }
}

impl Profile {
    /// Id web server yang benar-benar dipakai, sesuai pilihan Apache/Nginx.
    pub fn web_id(&self) -> &str {
        if self.web_server == "nginx" { &self.nginx_id } else { &self.apache_id }
    }

    /// Kosong berarti "(tidak dipakai)" - pilihan sengaja, bukan kegagalan.
    pub fn pakai_web(&self) -> bool {
        !self.web_id().is_empty()
    }

    pub fn pakai_mysql(&self) -> bool {
        !self.mysql_id.is_empty()
    }
}

/// Isi kamus berurutan dengan kunci tidak peka besar-kecil, seperti
/// `Dictionary<string,string>(OrdinalIgnoreCase)` yang tidak pernah dihapus.
pub fn kamus_set(v: &mut Vec<(String, String)>, k: &str, nilai: &str) {
    if let Some(kv) = v.iter_mut().find(|(x, _)| crate::teks::sama_abaikan_besar(x, k)) {
        kv.1 = nilai.to_string();
    } else {
        v.push((k.to_string(), nilai.to_string()));
    }
}

/// Satu situs (subfolder di folder proyek) yang dapat vhost otomatis.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
pub struct Site {
    pub folder: String,
    pub path: String,
    pub host_name: String,
    pub in_hosts: bool,
    pub has_vhost: bool,
    /// Folder proyek asal situs ini.
    pub root: String,
    /// Subfolder public/ atau web/ sebagai DocumentRoot bila ada.
    pub doc_root: String,
}

#[cfg(test)]
mod uji {
    use super::*;

    #[test]
    fn versi_seperti_system_version() {
        assert_eq!(Versi::dari("8.3"), Versi(8, 3, 0, -1));
        assert!(Versi::dari("8.3.12") > Versi::dari("8.3.9"));
        assert!(Versi::dari("10.0.0") > Versi::dari("9.9.9"));
        assert_eq!(Versi::dari("8.x"), Versi::NOL);
        assert_eq!(Versi::dari("1.2.3.4.5"), Versi::NOL);
        assert!(Versi::dari("1.2.3.0") > Versi::dari("1.2.3"));
    }

    #[test]
    fn label_paket() {
        let mut p = BinPackage::baru(BinKind::Php, "php-8", r"C:\php", "8.3.12");
        p.compiler = "vs16".into();
        p.arch = "x64".into();
        p.thread_safe = true;
        assert_eq!(p.label(), "8.3.12 · VS16 · x64 · TS");
        assert_eq!(p.nilai_simpan(), "php-8");
        p.kembar = true;
        assert_eq!(p.nilai_simpan(), r"C:\php");
    }
}
