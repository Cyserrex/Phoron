//! Web server (Apache/Nginx) dan php-cgi: FastCGI profil dan kolam PHP per
//! situs. Daftar periksanya di kepala `services.rs`.

use super::{port16, sambung, Lanjut, Layanan, Proses, Resep};
use crate::config::{self, Hasil};
use crate::model::{BinPackage, Profile, ServiceKind, ServiceState};
use crate::protokol::Pesan;
use crate::{bin, oracle, paths, port};
use std::collections::HashMap;
use std::path::Path;
use std::sync::Arc;
use std::time::{Duration, Instant};

/// Jeda sesudah httpd dilahirkan: httpd yang sehat tidak keluar, jadi yang
/// mati dalam jeda ini gagal mengikat port atau memuat modul.
const JEDA_APACHE: Duration = Duration::from_millis(1200);
const JEDA_NGINX: Duration = Duration::from_millis(1000);
const JEDA_PHP_CGI: Duration = Duration::from_millis(600);

fn jalur(p: &Path) -> String {
    p.to_string_lossy().into_owned()
}

/// Beberapa baris terakhir apache-error.log yang tidak kosong. Apache menulis
/// sebab kematiannya ke sana; menyuruh orang membukanya sendiri hanya
/// menunda jawaban yang sudah dipegang.
pub(super) fn ekor_log_apache(baris: usize) -> String {
    let f = paths::logs().join("apache-error.log");
    let Ok(isi) = std::fs::read(&f) else { return String::new() };
    let teks = String::from_utf8_lossy(&isi);
    let semua: Vec<&str> = teks.lines().collect();
    let awal = semua.len().saturating_sub(baris);
    semua.iter().skip(awal).filter(|b| !b.trim().is_empty()).copied().collect::<Vec<_>>().join("\r\n")
}

/// Sebab yang paling sering di balik kegagalan Apache: runtime Visual C++ yang
/// hilang ("The specified module could not be found" menunjuk DLL PHP).
fn petunjuk_gagal(apache: &BinPackage, php: Option<&BinPackage>) -> String {
    let mut paket: Vec<&BinPackage> = Vec::new();
    if let Some(p) = php {
        paket.push(p);
    }
    paket.push(apache);
    let kurang = bin::runtime_kurang_semua(&paket);
    if kurang.is_empty() {
        return String::new();
    }
    let teks: Vec<String> = kurang.iter().map(config::teks).collect();
    format!("\r\n\r\nKemungkinan sebabnya: {}", teks.join("\r\n"))
}

/// Folder awalan (`-p`) Nginx: milik Phoron, bukan folder paket. Nginx
/// membuka `logs\error.log` di awalannya sebelum membaca config, dan membuat
/// `temp\*_temp` di sana untuk jalur sementara yang tidak disetel config
/// (uwsgi, scgi). Dengan awalan folder paket, keduanya menulisi bin - yang
/// tidak pernah boleh disunting Phoron. Config memakai jalur mutlak, jadi
/// awalannya tidak menentukan apa pun yang lain.
pub(super) fn awalan_nginx() -> std::io::Result<std::path::PathBuf> {
    let a = paths::tmp().join("nginx");
    std::fs::create_dir_all(a.join("logs"))?;
    std::fs::create_dir_all(a.join("temp"))?;
    Ok(a)
}

/// Nama kolam untuk pesan: "PHP 8.3.12 (api-bacameter, bbs)".
fn nama_kolam(k: &config::Kolam) -> String {
    let situs: Vec<&str> = k.situs.iter().map(|s| s.folder.as_str()).collect();
    format!("PHP {} ({})", k.php.version, situs.join(", "))
}

fn daftar_situs(k: &config::Kolam) -> String {
    k.situs.iter().map(|s| s.folder.as_str()).collect::<Vec<_>>().join(", ")
}

impl Layanan {
    /// Nyalakan web server profil. Start yang masih berjalan dipakai ulang.
    pub fn start_web(&self, profile: &Profile, web: Option<&BinPackage>, php: Option<&BinPackage>, cfg: &Hasil) -> bool {
        self.antre(ServiceKind::Web, |g| self.mulai_web(profile, web, php, cfg, g))
    }

    fn mulai_web(
        &self,
        profile: &Profile,
        web: Option<&BinPackage>,
        php: Option<&BinPackage>,
        cfg: &Hasil,
        g: u64,
    ) -> Lanjut<bool> {
        if self.web_state() == ServiceState::Jalan {
            return Ok(true);
        }
        let Some(web) = web else {
            return Ok(self.gagal(ServiceKind::Web, Pesan::baru("Profil belum menunjuk web server.")));
        };
        self.ubah(ServiceKind::Web, ServiceState::Menyalakan, None);
        // Sebab yang jelas jauh lebih berguna daripada Apache yang mati dengan
        // kode 1 tanpa keterangan.
        let u = port::check(port16(profile.http_port));
        if u.in_use {
            return Ok(self.gagal(
                ServiceKind::Web,
                sambung(u.describe(), " Matikan dulu aplikasi itu atau ganti port profil."),
            ));
        }
        if cfg.ssl_enabled {
            // Apache yang gagal mengikat SATU port menolak start seluruhnya.
            let u = port::check(port16(profile.https_port));
            if u.in_use {
                return Ok(self.gagal(
                    ServiceKind::Web,
                    sambung(
                        u.describe(),
                        " HTTPS memakai port itu, dan Apache menolak start kalau salah satu portnya terpakai.",
                    ),
                ));
            }
        }
        if profile.web_server == "nginx" {
            self.start_nginx(web, php, cfg, g)
        } else {
            self.start_apache(profile, web, php, cfg, g)
        }
    }

    /// Web server yang baru dilahirkan ternyata sudah tidak diminta lagi:
    /// bereskan sendiri, sebab Stop mungkin datang sebelum ia sempat dipasang.
    pub(super) fn buang_web_yatim(&self, p: &Arc<Proses>) {
        if self.lepas_web_jika(p) {
            p.bunuh();
        }
        self.stop_fastcgi();
    }

    fn pasang_web(&self, p: &Arc<Proses>) {
        self.data().web = Some(p.clone());
    }

    /// Langkah bersama Apache dan Nginx sesudah config lolos uji: php-cgi,
    /// kolam, lalu web server itu sendiri. `None` = sudah dilaporkan.
    #[allow(clippy::too_many_arguments)]
    fn lahirkan_web(
        &self,
        exe: &str,
        args: &str,
        folder: &str,
        env: &HashMap<String, String>,
        tag: &str,
        php: Option<&BinPackage>,
        cfg: &Hasil,
        fcgi: bool,
        g: u64,
    ) -> Option<Arc<Proses>> {
        if fcgi {
            if let Some(php) = php {
                let r = self.start_fastcgi(php, port16(cfg.fastcgi_port), cfg.php_ini_dir.as_deref(), g);
                if self.batal(ServiceKind::Web, g) {
                    self.stop_fastcgi();
                    return None;
                }
                if let Err(p) = r {
                    self.gagal(ServiceKind::Web, p);
                    return None;
                }
            }
        }
        // Kolam SEBELUM web server: permintaan pertama ke situs ber-PHP lain
        // tidak boleh sampai lebih dulu daripada php-cgi-nya.
        self.mulai_kolam(cfg, g);
        if self.batal(ServiceKind::Web, g) {
            self.stop_fastcgi();
            return None;
        }
        let p = match self.spawn(exe, args, folder, Some(env), tag, None, None, true) {
            Ok(p) => p,
            Err(pesan) => {
                self.stop_fastcgi();
                self.gagal(ServiceKind::Web, pesan);
                return None;
            }
        };
        self.pasang_web(&p);
        if self.batal(ServiceKind::Web, g) {
            self.buang_web_yatim(&p);
            return None;
        }
        Some(p)
    }

    /// Sesudah jeda pertama: masih hidup? `Some(false)` = sudah dilaporkan
    /// (mati atau dibatalkan), `None` = sehat.
    fn periksa_jeda(&self, p: &Arc<Proses>, g: u64, jeda: Duration) -> Option<bool> {
        // Ditunggu sampai jedanya habis ATAU proses mati lebih dulu.
        let mati = p.tunggu_keluar(jeda);
        // Stop yang datang selama jeda sudah membunuh prosesnya. Tanpa
        // pemeriksaan ini kematian yang DISENGAJA dilaporkan "berhenti
        // seketika", lengkap dengan saran memasang VC++.
        if self.batal(ServiceKind::Web, g) {
            self.buang_web_yatim(p);
            return Some(false);
        }
        // dalam_start tetap menyala sampai `selesaikan`: proses yang mati di
        // sisa start tetap dilaporkan start, sekali.
        if mati || p.keluar() {
            if !self.lepas_web_jika(p) {
                // Pengawas sudah melapor.
                return Some(false);
            }
            self.stop_fastcgi();
            return Some(true);
        }
        None
    }

    fn start_apache(
        &self,
        profile: &Profile,
        apache: &BinPackage,
        php: Option<&BinPackage>,
        cfg: &Hasil,
        g: u64,
    ) -> Lanjut<bool> {
        let httpd = jalur(&Path::new(&apache.path).join("bin").join("httpd.exe"));
        let conf = cfg.httpd_conf.clone().unwrap_or_else(|| paths::etc_apache().join("httpd.conf"));
        let args = format!("-f \"{}\" -d \"{}\"", jalur(&conf), apache.path);
        let env = super::env_for(php, cfg.php_ini_dir.as_deref());

        // Uji config dulu: httpd yang salah config mencetak sebabnya lalu
        // keluar seketika; tanpa uji ini orang cuma melihat "Gagal".
        // Bisa dipotong stop: start sesudah stop tidak perlu menunggu uji ini.
        let uji = self
            .jalankan(&httpd, &format!("{args} -t"), Path::new(&apache.path), 30_000, Some(&env), ServiceKind::Web, g)
            .map_err(|e| e.to_string())?;
        if self.batal(ServiceKind::Web, g) {
            return Ok(false);
        }
        if !uji.ok() {
            return Ok(self.gagal(
                ServiceKind::Web,
                Pesan::dengan("Konfigurasi Apache ditolak:\r\n{0}{1}", &[&uji.all(), &petunjuk_gagal(apache, php)]),
            ));
        }

        let Some(p) = self.lahirkan_web(&httpd, &args, &apache.path, &env, "apache", php, cfg, cfg.php_fastcgi, g)
        else {
            return Ok(false);
        };
        match self.periksa_jeda(&p, g, JEDA_APACHE) {
            Some(true) => {
                let ekor = ekor_log_apache(6);
                let ekor = if ekor.is_empty() { ekor } else { format!("\r\n{ekor}") };
                return Ok(self.gagal(
                    ServiceKind::Web,
                    Pesan::dengan(
                        "Apache berhenti seketika (kode {0}).{1}{2}",
                        &[&p.kode().to_string(), &ekor, &petunjuk_gagal(apache, php)],
                    ),
                ));
            }
            Some(false) => return Ok(false),
            None => {}
        }
        self.say(Pesan::dengan(
            "Apache {0} jalan di port {1} (PID {2}).",
            &[&apache.version, &profile.http_port.to_string(), &p.pid.to_string()],
        ));
        Ok(self.selesaikan(ServiceKind::Web, g, &p, None, 0))
    }

    fn start_nginx(&self, nginx: &BinPackage, php: Option<&BinPackage>, cfg: &Hasil, g: u64) -> Lanjut<bool> {
        let exe = jalur(&Path::new(&nginx.path).join("nginx.exe"));
        let conf = cfg.nginx_conf.clone().unwrap_or_else(|| paths::etc_nginx().join("nginx.conf"));
        let awalan = awalan_nginx().map_err(|e| e.to_string())?;
        let args = format!("-p \"{}\" -c \"{}\"", jalur(&awalan), jalur(&conf));
        let env = super::env_for(php, cfg.php_ini_dir.as_deref());

        let uji = self
            .jalankan(&exe, &format!("{args} -t"), Path::new(&nginx.path), 30_000, Some(&env), ServiceKind::Web, g)
            .map_err(|e| e.to_string())?;
        if self.batal(ServiceKind::Web, g) {
            return Ok(false);
        }
        if !uji.ok() {
            return Ok(self.gagal(ServiceKind::Web, Pesan::dengan("Konfigurasi Nginx ditolak:\n{0}", &[&uji.all()])));
        }
        // Nginx selalu lewat FastCGI.
        let Some(p) = self.lahirkan_web(&exe, &args, &nginx.path, &env, "nginx", php, cfg, true, g) else {
            return Ok(false);
        };
        // Penjaga yang sama seperti Apache, yang dulu TIDAK ada: nginx yang
        // mati seketika tetap dilaporkan "jalan".
        match self.periksa_jeda(&p, g, JEDA_NGINX) {
            Some(true) => {
                return Ok(self.gagal(
                    ServiceKind::Web,
                    Pesan::dengan(
                        "Nginx berhenti seketika (kode {0}). Lihat logs\\nginx-error.log.",
                        &[&p.kode().to_string()],
                    ),
                ));
            }
            Some(false) => return Ok(false),
            None => {}
        }
        self.say(Pesan::dengan("Nginx {0} jalan (PID {1}).", &[&nginx.version, &p.pid.to_string()]));
        Ok(self.selesaikan(ServiceKind::Web, g, &p, None, 0))
    }

    /// Matikan web server beserta php-cgi dan kolamnya.
    pub fn stop_web(&self) {
        // PALING AWAL: start yang sedang berjalan harus melihat dirinya
        // dibatalkan sebelum sempat melahirkan httpd yang tak ditemukan Stop.
        self.naikkan_giliran(ServiceKind::Web);
        self.ubah(ServiceKind::Web, ServiceState::Mematikan, None);
        // Dilepas SEBELUM dimatikan: pengawas memakai rujukan itu untuk
        // membedakan "dimatikan pengguna" dari "mati sendiri".
        if let Some(p) = self.ambil_lepas_web() {
            p.bunuh();
        }
        self.stop_fastcgi();
        self.say(Pesan::baru("Web server dimatikan."));
        self.ubah(ServiceKind::Web, ServiceState::Berhenti, None);
    }

    // ------------------------------------------------------------ php-cgi

    fn env_php_cgi(php: &BinPackage, folder_ini: Option<&str>, anak: &str) -> HashMap<String, String> {
        let mut env = super::env_for(Some(php), folder_ini);
        // php-cgi memutar ulang dirinya setelah sekian permintaan; bawaan 500
        // membuat pekerja mati di tengah pengembangan dan Apache membalas 503.
        env.insert("PHP_FCGI_MAX_REQUESTS".into(), "0".into());
        // Tanpa ini hanya ada SATU responder: sepuluh permintaan serentak ke
        // berkas yang tidur sedetik makan 10,1 detik, dengan empat pekerja 3,0.
        // php-cgi.exe di Windows memang melahirkan pekerja anak; mereka ikut
        // mati bersama induknya karena yang dimatikan selalu pohonnya.
        env.insert("PHP_FCGI_CHILDREN".into(), anak.into());
        env
    }

    pub(super) fn env_php_cgi_profil(php: &BinPackage, folder_ini: Option<&str>) -> HashMap<String, String> {
        Self::env_php_cgi(php, folder_ini, "4")
    }

    /// Lingkungan php-cgi kolam: dua pekerja (satu kolam 1+2 proses ~76 MB
    /// menganggur), Instant Client yang VERSINYA cocok di depan PATH.
    pub(super) fn env_php_cgi_kolam(k: &config::Kolam) -> HashMap<String, String> {
        let mut env = Self::env_php_cgi(&k.php, Some(&k.folder_ini), "2");
        let path = env.get("PATH").cloned().unwrap_or_default();
        if let Some(ic) = oracle::folder_untuk(Some(&k.php), &k.ekstensi, Some(&path)) {
            env.insert("PATH".into(), format!("{ic};{path}"));
        }
        env
    }

    fn start_fastcgi(&self, php: &BinPackage, port: u16, folder_ini: Option<&str>, g: u64) -> Result<(), Pesan> {
        let exe = jalur(&Path::new(&php.path).join("php-cgi.exe"));
        if !Path::new(&exe).is_file() {
            return Err(Pesan::dengan("php-cgi.exe tidak ada di {0}.", &[&php.id]));
        }
        self.stop_fastcgi();
        let env = Self::env_php_cgi_profil(php, folder_ini);
        let args = format!("-b 127.0.0.1:{port}");
        let resep = Resep {
            exe: exe.clone(),
            args: args.clone(),
            kerja_di: php.path.clone(),
            env: env.clone(),
            tag: "php-cgi".into(),
            nama: "php-cgi".into(),
            port,
            kolam: false,
        };
        let p = self.spawn(&exe, &args, &php.path, Some(&env), "php-cgi", None, Some(resep), false)?;
        self.data().fcgi = Some(p.clone());
        let mati = p.tunggu_keluar(JEDA_PHP_CGI);
        if self.batal(ServiceKind::Web, g) {
            // Pemanggil yang membereskan; tidak ada yang perlu dikeluhkan.
            return Ok(());
        }
        if mati || p.keluar() {
            self.lepas_fcgi_jika(&p);
            return Err(Pesan::dengan(
                "php-cgi berhenti seketika - port {0} mungkin dipakai proses lain.",
                &[&port.to_string()],
            ));
        }
        self.say(Pesan::dengan("php-cgi (FastCGI) jalan di 127.0.0.1:{0}.", &[&port.to_string()]));
        Ok(())
    }

    /// Matikan php-cgi profil DAN semua kolam: setiap jalur yang mematikan
    /// php-cgi (stop, start yang batal, web server yang mati sendiri) otomatis
    /// ikut mematikan kolamnya. Dilepas dulu sebelum dibunuh.
    pub(super) fn stop_fastcgi(&self) {
        let (kolam, fcgi) = {
            let mut d = self.data();
            d.bangkit.clear();
            (std::mem::take(&mut d.kolam), d.fcgi.take())
        };
        for k in kolam.iter().chain(fcgi.iter()) {
            k.bunuh();
        }
    }

    /// Satu php-cgi untuk tiap kolam PHP per situs. Kolam yang gagal TIDAK
    /// menggagalkan web server; yang gagal disebut beserta situsnya.
    fn mulai_kolam(&self, cfg: &Hasil, g: u64) {
        let mut lahir: Vec<(Arc<Proses>, &config::Kolam)> = Vec::new();
        for k in &cfg.kolam {
            let nama = nama_kolam(k);
            let port = port16(k.port);
            if !port::is_free(port) {
                self.say(Pesan::dengan(
                    "{0} tidak dinyalakan: port {1} sudah dipakai proses lain. Situs itu akan membalas 503.",
                    &[&nama, &port.to_string()],
                ));
                continue;
            }
            let env = Self::env_php_cgi_kolam(k);
            let exe = jalur(&Path::new(&k.php.path).join("php-cgi.exe"));
            let args = format!("-b 127.0.0.1:{port}");
            let tag = format!("php-cgi {}", k.php.version);
            let resep = Resep {
                exe: exe.clone(),
                args: args.clone(),
                kerja_di: k.php.path.clone(),
                env: env.clone(),
                tag: tag.clone(),
                nama: nama.clone(),
                port,
                kolam: true,
            };
            match self.spawn(&exe, &args, &k.php.path, Some(&env), &tag, None, Some(resep), false) {
                Ok(p) => {
                    self.data().kolam.push(p.clone());
                    lahir.push((p, k));
                }
                Err(pesan) => {
                    self.say(pesan);
                    self.say(Pesan::dengan("{0} tidak bisa dinyalakan.", &[&nama]));
                }
            }
        }
        if lahir.is_empty() {
            return;
        }
        // Satu jeda untuk semua kolam, bukan satu per kolam.
        std::thread::sleep(JEDA_PHP_CGI);
        if self.batal(ServiceKind::Web, g) {
            return;
        }
        for (p, k) in lahir {
            if p.keluar() {
                self.lepas_kolam_jika(&p);
                self.say(Pesan::dengan("{0} berhenti seketika - situs itu akan membalas 503.", &[&nama_kolam(k)]));
            } else {
                self.say(Pesan::dengan(
                    "PHP {0} jalan di 127.0.0.1:{1} untuk {2}.",
                    &[&k.php.version, &k.port.to_string(), &daftar_situs(k)],
                ));
            }
        }
    }

    /// Nyalakan ulang php-cgi yang mati sendiri, di port yang sama. Dulu cukup
    /// satu baris log, dan situsnya 503 sampai web server dinyalakan ulang -
    /// sementara lampu Apache tetap hijau. Paling banyak tiga kali per menit
    /// per port: php-cgi yang langsung mati lagi tidak boleh diputar terus.
    pub(super) fn bangkitkan(&self, r: Resep, kode: i32) {
        let g = self.giliran(ServiceKind::Web);
        let boleh = {
            let mut d = self.data();
            super::boleh_bangkit(d.bangkit.entry(r.port).or_default(), Instant::now())
        };
        let kode = kode.to_string();
        if !boleh {
            let kunci_pesan = if r.kolam {
                "{0} berhenti sendiri berulang kali (kode {1}) - tidak dinyalakan ulang lagi. Situs itu akan \
                 membalas 503. Lihat logs\\php_errors.log, lalu nyalakan ulang web server."
            } else {
                "{0} berhenti sendiri berulang kali (kode {1}) - tidak dinyalakan ulang lagi. Halaman PHP akan \
                 membalas 503. Lihat logs\\php_errors.log, lalu nyalakan ulang web server."
            };
            self.say(Pesan::dengan(kunci_pesan, &[&r.nama, &kode]));
            return;
        }
        self.say(Pesan::dengan("{0} berhenti sendiri (kode {1}) - dinyalakan ulang.", &[&r.nama, &kode]));

        let diri = self.clone();
        std::thread::spawn(move || {
            // Port milik proses yang mati dilepas Windows sesaat kemudian.
            for _ in 0..20 {
                if port::is_free(r.port) {
                    break;
                }
                std::thread::sleep(Duration::from_millis(100));
            }
            let masih = |d: &Layanan| !d.batal(ServiceKind::Web, g) && d.web_state() == ServiceState::Jalan;
            if !masih(&diri) {
                return;
            }
            let baru =
                match diri.spawn(&r.exe, &r.args, &r.kerja_di, Some(&r.env), &r.tag, None, Some(r.clone()), false) {
                    Ok(p) => p,
                    Err(pesan) => {
                        diri.say(pesan);
                        diri.say(Pesan::dengan("{0} tidak bisa dinyalakan ulang.", &[&r.nama]));
                        return;
                    }
                };
            let simpan = {
                let mut d = diri.data();
                // Web server dimatikan selagi kebangkitan ini berjalan: yang
                // baru lahir tidak boleh tertinggal tanpa pemilik.
                let simpan = masih(&diri);
                if simpan {
                    if r.kolam {
                        d.kolam.push(baru.clone());
                    } else {
                        d.fcgi = Some(baru.clone());
                    }
                }
                simpan
            };
            if !simpan {
                baru.bunuh();
                return;
            }
            // Mati sebelum sempat dicatat: pengawasnya tidak menemukannya.
            if baru.keluar() {
                diri.proses_mati(&baru);
            }
        });
    }
}
