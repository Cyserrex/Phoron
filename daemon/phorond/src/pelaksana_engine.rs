//! Pelaksana sungguhan: setiap perintah pipe dan butir menu baki dijalankan
//! oleh `phoron_inti::engine::Engine`.
//!
//! Isi menu baki meniru menu baki Phoron C# (Pengendali.IsiMenuBaki): daya,
//! proyek Node, situs PHP terakhir, ganti profil, folder proyek, localhost.
//! Teksnya mengikuti bahasa antarmuka di phoron.ini; terjemahannya disalin
//! dari kamus C# (Lang.cs, LangLayar*.cs) - kunci sama, jadi tetap satu sumber.

use crate::pelaksana::{ItemMenu, Pelaksana};
use crate::penyiar::Penyiar;
use phoron_inti::engine::{self, Engine};
use phoron_inti::model::ServiceState;
use phoron_inti::protokol::{HasilHalo, IsiJawaban, Perintah};
use phoron_inti::situs;

pub struct PelaksanaEngine {
    engine: Engine,
}

impl PelaksanaEngine {
    /// Membuat Engine (memuat setelan, profil, paket, situs) dan menyambungkan
    /// kabarnya ke semua klien pipe.
    pub fn baru(penyiar: Penyiar) -> Self {
        let p = penyiar.clone();
        let engine = Engine::baru(move |k| p.siar(&k));
        PelaksanaEngine { engine }
    }

    /// Nyalakan layanan saat daemon mulai bila setelan "auto_start" hidup -
    /// padanan AutoStartServices di Pengendali C#. Di utas sendiri: menyalakan
    /// MySQL bisa makan puluhan detik dan pipe harus sudah melayani.
    pub fn mulai_otomatis(&self) -> bool {
        self.engine.settings().auto_start_services
    }

    fn t(&self, kunci: &str) -> String {
        terjemah(&self.engine.settings().bahasa, kunci)
    }

    fn t1(&self, kunci: &str, a: &str) -> String {
        self.t(kunci).replace("{0}", a)
    }

    /// Alamat situs PHP, padanan Engine.SiteUrl C#.
    fn url_situs(&self, site_path: &str, host: &str) -> Option<String> {
        let active = self.engine.active()?;
        let port = if active.http_port == 80 { String::new() } else { format!(":{}", active.http_port) };
        if self.engine.settings().auto_vhost {
            return Some(format!("http://{host}{port}/"));
        }
        let utama = situs::document_root(Some(&active));
        let jalur = site_path.trim_end_matches('\\');
        let akar = utama.trim_end_matches('\\');
        if jalur.is_empty() || akar.is_empty() {
            return None;
        }
        // Dibandingkan pada BATAS folder: "proyek-lain" bukan di dalam "proyek".
        let rel = if jalur.eq_ignore_ascii_case(akar) {
            ""
        } else if jalur.len() > akar.len()
            && jalur.is_char_boundary(akar.len())
            && jalur[..akar.len()].eq_ignore_ascii_case(akar)
            && jalur[akar.len()..].starts_with('\\')
        {
            jalur[akar.len()..].trim_matches('\\')
        } else {
            return None;
        };
        let rel = rel.replace('\\', "/");
        Some(format!("http://localhost{port}/{}", if rel.is_empty() { String::new() } else { rel + "/" }))
    }

    fn root_url(&self) -> String {
        match self.engine.active() {
            Some(a) if a.http_port != 80 => format!("http://localhost:{}/", a.http_port),
            _ => "http://localhost/".into(),
        }
    }
}

impl Pelaksana for PelaksanaEngine {
    fn tangani(&self, perintah: Perintah) -> IsiJawaban {
        match perintah {
            Perintah::Halo { .. } => IsiJawaban::Ok {
                hasil: serde_json::to_value(HasilHalo {
                    versi_daemon: phoron_inti::VERSI.to_string(),
                    protokol: phoron_inti::protokol::VERSI_PROTOKOL,
                })
                .unwrap_or_default(),
                peringatan: Vec::new(),
            },
            lain => engine::tangani(&self.engine, lain),
        }
    }

    fn teks(&self, kunci: &str) -> String {
        self.t(kunci)
    }

    fn menu_baki(&self) -> Vec<ItemMenu> {
        let mut m = Vec::new();
        let st = self.engine.layanan().status();
        let hidup = matches!(st.web, ServiceState::Jalan | ServiceState::Menyalakan)
            || matches!(st.db, ServiceState::Jalan | ServiceState::Menyalakan);
        m.push(if hidup {
            ItemMenu::biasa(&self.t("Matikan semua"), Perintah::MatikanSemua)
        } else {
            ItemMenu::biasa(&self.t("Nyalakan semua"), Perintah::NyalakanSemua)
        });

        // Proyek Node: yang berjalan punya sub-menu (buka, hentikan, folder).
        let apps = self.engine.node_apps();
        if !apps.is_empty() {
            m.push(ItemMenu::Pemisah);
            let node = self.engine.node();
            let mut isi = Vec::new();
            for a in &apps {
                if node.is_running(&a.path) {
                    let mut sub = Vec::new();
                    if let Some(url) = node.url_of(&a.path) {
                        sub.push(ItemMenu::Buka { teks: self.t("Buka di browser"), target: url });
                    }
                    sub.push(ItemMenu::biasa(&self.t("Hentikan"), Perintah::HentikanNode { path: a.path.clone() }));
                    sub.push(ItemMenu::Buka { teks: self.t("Buka folder"), target: a.path.clone() });
                    isi.push(ItemMenu::Sub { teks: format!("{} \u{25CF}", a.display_name()), isi: sub });
                } else {
                    isi.push(ItemMenu::biasa(
                        &self.t1("Jalankan {0}", &a.display_name()),
                        Perintah::JalankanNode { path: a.path.clone(), port_sekali: 0 },
                    ));
                }
            }
            m.push(ItemMenu::Sub { teks: self.t("Proyek Node"), isi });
        }

        // Situs PHP yang terakhir dibuka.
        let terakhir = self.engine.situs_terakhir(5);
        if !terakhir.is_empty() {
            let isi: Vec<ItemMenu> = terakhir
                .iter()
                .filter_map(|s| {
                    let url = self.url_situs(&s.path, &s.host_name)?;
                    Some(ItemMenu::Buka { teks: s.folder.clone(), target: url })
                })
                .collect();
            if !isi.is_empty() {
                m.push(ItemMenu::Sub { teks: self.t("Situs PHP terakhir"), isi });
            }
        }

        // Ganti profil: profil aktif dicentang.
        let profiles = self.engine.profiles();
        if profiles.len() > 1 {
            let aktif = self.engine.active().map(|a| a.file_name).unwrap_or_default();
            let isi = profiles
                .iter()
                .map(|p| ItemMenu::Perintah {
                    teks: p.name.clone(),
                    perintah: Perintah::GantiProfil { file_name: p.file_name.clone(), restart: true },
                    aktif: p.file_name != aktif,
                    tercentang: p.file_name == aktif,
                })
                .collect();
            m.push(ItemMenu::Pemisah);
            m.push(ItemMenu::Sub { teks: self.t("Ganti profil"), isi });
        }

        m.push(ItemMenu::Pemisah);
        if let Some(a) = self.engine.active() {
            m.push(ItemMenu::Buka { teks: self.t("Buka folder proyek"), target: situs::document_root(Some(&a)) });
        }
        m.push(ItemMenu::Buka { teks: self.t("Buka localhost"), target: self.root_url() });
        m
    }

    fn sebelum_keluar(&self) {
        // Web server dan php-cgi dimatikan, MySQL dengan shutdown rapi, proyek
        // Node dihentikan beserta pohon prosesnya.
        self.engine.tutup();
    }

    fn layanan_hidup(&self) -> Vec<String> {
        let st = self.engine.layanan().status();
        let mut v = Vec::new();
        if matches!(st.web, ServiceState::Jalan | ServiceState::Menyalakan) {
            v.push("web".into());
        }
        if matches!(st.db, ServiceState::Jalan | ServiceState::Menyalakan) {
            v.push("db".into());
        }
        for n in self.engine.node().status_semua() {
            if n.berjalan {
                v.push(format!("node:{}", n.path));
            }
        }
        v
    }

    fn nyalakan_awal(&self, daftar: &[String]) {
        // MySQL lebih dulu, seperti StartAllAsync: aplikasi PHP yang dibuka
        // begitu web hidup langsung bisa tersambung ke basis datanya.
        if daftar.iter().any(|d| d == "db") {
            let _ = self.engine.nyalakan_db();
        }
        if daftar.iter().any(|d| d == "web") {
            let _ = self.engine.nyalakan_web();
        }
        for d in daftar {
            if let Some(path) = d.strip_prefix("node:") {
                let _ = self.engine.jalankan_node(path, 0);
            }
        }
    }
}

/// Teks menu baki per bahasa (id, en, jv, bjn), disalin dari kamus C#.
fn terjemah(bahasa: &str, kunci: &str) -> String {
    const KAMUS: &[[&str; 4]] = &[
        ["Buka Phoron", "Open Phoron", "Bukak Phoron", "Buka Phoron"],
        ["Keluar", "Quit", "Metu", "Kaluar"],
        ["Nyalakan semua", "Start all", "Uripake kabeh", "Hidupakan samunyaan"],
        ["Matikan semua", "Stop all", "Patenana kabeh", "Pajahakan samunyaan"],
        ["Proyek Node", "Node projects", "Proyek Node", "Proyek Node"],
        ["Jalankan {0}", "Run {0}", "Uripake {0}", "Jalanakan {0}"],
        ["Hentikan", "Stop", "Patenana", "Pajahakan"],
        ["Buka di browser", "Open in browser", "Bukak ing browser", "Buka di browser"],
        ["Buka folder", "Open folder", "Bukak folder", "Buka folder"],
        ["Situs PHP terakhir", "Recent PHP sites", "Situs PHP pungkasan", "Situs PHP tarakhir"],
        ["Ganti profil", "Switch profile", "Ganti profil", "Ganti profil"],
        ["Buka folder proyek", "Open project folder", "Bukak folder proyek", "Buka folder proyek"],
        ["Buka localhost", "Open localhost", "Bukak localhost", "Buka localhost"],
    ];
    let i = match bahasa {
        "en" => 1,
        "jv" => 2,
        "bjn" => 3,
        _ => 0,
    };
    KAMUS
        .iter()
        .find(|b| b[0] == kunci)
        .map(|b| b[i].to_string())
        .unwrap_or_else(|| kunci.to_string())
}

#[cfg(test)]
mod uji {
    use super::*;

    #[test]
    fn terjemahan_menu() {
        assert_eq!(terjemah("en", "Keluar"), "Quit");
        assert_eq!(terjemah("bjn", "Nyalakan semua"), "Hidupakan samunyaan");
        assert_eq!(terjemah("id", "Keluar"), "Keluar");
        assert_eq!(terjemah("xx", "Hentikan"), "Hentikan");
        assert_eq!(terjemah("en", "kunci tak dikenal"), "kunci tak dikenal");
    }
}
