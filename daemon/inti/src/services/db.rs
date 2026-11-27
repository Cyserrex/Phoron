//! MySQL/MariaDB: menyiapkan folder data, menyalakan mysqld, dan mematikannya
//! dengan rapi. Daftar periksanya di kepala `services.rs`.

use super::{port16, sambung, Lanjut, Layanan, Proses};
use crate::config;
use crate::model::{BinPackage, Profile, ServiceKind, ServiceState};
use crate::protokol::Pesan;
use crate::shell::RunResult;
use crate::{log_file, paths, port, shell, waktu};
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::{Duration, Instant};
use windows_sys::Win32::Foundation::CloseHandle;
use windows_sys::Win32::System::Threading::{OpenEventW, SetEvent, EVENT_MODIFY_STATE};

/// Sesudah sekian lama belum siap, orang diberi tahu bahwa yang ditunggu
/// kemungkinan pemulihan InnoDB.
const PESAN_SABAR: Duration = Duration::from_secs(20);

/// Waktu yang diberikan mysqld untuk berhenti sendiri sebelum dipaksa. InnoDB
/// yang menulis buffer pool-nya bisa perlu beberapa detik; dua belas detik
/// jauh di atas yang pernah terukur.
const BATAS_RAPI: Duration = Duration::from_secs(12);

/// Salinan keluaran mysqld (`--console` membuat mysqld mengabaikan log-error).
pub fn mysql_error_log() -> PathBuf {
    paths::logs().join("mysql-error.log")
}

fn jalur(p: &Path) -> String {
    p.to_string_lossy().into_owned()
}

fn sudah_init(data_dir: &str) -> bool {
    Path::new(data_dir).join("mysql").is_dir()
}

/// Tabel sistem yang hanya ada setelah inisialisasi berjalan cukup jauh:
/// mysql.ibd (MySQL 8), mysql\user.frm / user.MYD (MySQL 5.x, MariaDB lama),
/// mysql\global_priv.* (MariaDB 10.4+). Folder mysql\ saja tidak cukup - init
/// yang terputus juga meninggalkannya.
pub fn tabel_sistem_ada(data_dir: &str) -> bool {
    let d = Path::new(data_dir);
    let m = d.join("mysql");
    d.join("mysql.ibd").is_file()
        || ["user.frm", "user.MYD", "global_priv.frm", "global_priv.MAD"].iter().any(|f| m.join(f).is_file())
}

/// Apakah inisialisasi boleh dianggap berhasil: terputus selalu gagal; kode
/// bukan 0 hanya dimaafkan bila tabel sistemnya benar-benar ada
/// (mysql_install_db MariaDB kadang keluar bukan 0 sesudah selesai).
pub fn init_berhasil(res: &RunResult, data_dir: &str) -> bool {
    if res.timed_out {
        return false;
    }
    res.exit_code == 0 || tabel_sistem_ada(data_dir)
}

/// Setel event "MySQLShutdown<PID>" milik mysqld - jalan yang sama dengan
/// penghentian layanan Windows-nya, tanpa kata sandi apa pun. `false` bila
/// event itu tidak ada (build yang tidak membuatnya, atau mysqld milik
/// pengguna lain).
pub fn minta_mysql_berhenti(pid: u32) -> bool {
    let nama: Vec<u16> = format!("MySQLShutdown{pid}").encode_utf16().chain(std::iter::once(0)).collect();
    // SAFETY: nama berakhiran nol dan hidup sepanjang panggilan; handle ditutup.
    unsafe {
        let h = OpenEventW(EVENT_MODIFY_STATE, 0, nama.as_ptr());
        if h.is_null() {
            return false;
        }
        let ok = SetEvent(h) != 0;
        CloseHandle(h);
        ok
    }
}

impl Layanan {
    /// Nyalakan MySQL profil. Start yang masih berjalan dipakai ulang.
    pub fn start_db(&self, profile: &Profile, mysql: Option<&BinPackage>) -> bool {
        self.antre(ServiceKind::Db, |g| self.mulai_db(profile, mysql, g))
    }

    fn mulai_db(&self, profile: &Profile, mysql: Option<&BinPackage>, g: u64) -> Lanjut<bool> {
        if self.db_state() == ServiceState::Jalan {
            return Ok(true);
        }
        let Some(mysql) = mysql else {
            self.say(Pesan::baru("Profil belum menunjuk versi MySQL."));
            return Ok(false);
        };
        self.ubah(ServiceKind::Db, ServiceState::Menyalakan, None);
        let port = port16(profile.mysql_port);
        let u = port::check(port);
        if u.in_use {
            return Ok(self.gagal(ServiceKind::Db, sambung(u.describe(), " Matikan dulu, atau ubah port MySQL profil ini.")));
        }

        let my_ini = jalur(&paths::etc_mysql().join("my.ini"));
        let data_dir = config::mysql_data_dir(mysql);
        if !sudah_init(&data_dir) {
            self.say(Pesan::dengan("Folder data {0} belum ada - menyiapkan basis data awal...", &[&mysql.id]));
            // Folder data baru selagi folder lama bernama sama berisi basis
            // data: yang dikenal pengguna TIDAK akan kelihatan. Disebut
            // terang-terangan, supaya "semua basis data saya hilang" punya
            // jawaban yang bisa ditemukan.
            let lama = jalur(&paths::data().join(&mysql.id));
            if !crate::teks::sama_abaikan_besar(lama.trim_end_matches('\\'), data_dir.trim_end_matches('\\'))
                && sudah_init(&lama)
            {
                let pemilik = config::pemilik_data(&lama).unwrap_or_else(|| "?".into());
                self.say(Pesan::dengan(
                    "Catatan: basis data lama ada di {0} dan tercatat milik MySQL lain ({1}). Paket ini memakai {2} \
                     - tidak ada yang dipindah atau dihapus.",
                    &[&lama, &pemilik, &data_dir],
                ));
            }
            let galat = self.siapkan_data(mysql, &my_ini, &data_dir)?;
            if self.batal(ServiceKind::Db, g) {
                return Ok(false);
            }
            if let Some(p) = galat {
                // Pesannya sudah dicatat siapkan_data, bersama nasib sisanya.
                self.ubah(ServiceKind::Db, ServiceState::Gagal, Some(p));
                return Ok(false);
            }
            self.say(Pesan::baru("Basis data siap. Pengguna: root, tanpa kata sandi."));
        }

        let mysqld = jalur(&Path::new(&mysql.path).join("bin").join("mysqld.exe"));
        // --console: galatnya sampai ke Aktivitas seketika; salinan lengkapnya
        // ditulis Phoron sendiri ke mysql-error.log.
        let args = format!("--defaults-file=\"{my_ini}\" --console");
        let p = match self.spawn(&mysqld, &args, &mysql.path, None, "mysql", Some(mysql_error_log()), None, false) {
            Ok(p) => p,
            Err(pesan) => return Ok(self.gagal(ServiceKind::Db, pesan)),
        };
        {
            let mut d = self.data();
            d.db = Some(p.clone());
            d.db_paket = Some(mysql.clone());
            d.db_port = port;
        }

        // Port dijadikan tanda siap karena log-nya berbeda-beda antarversi.
        // Ditunggu selama mysqld masih hidup, sampai batas_siap_db.
        let batas = self.batas_siap_db();
        let sejak = Instant::now();
        let mut sudah_bilang = false;
        while sejak.elapsed() < batas {
            // Stop datang selagi InnoDB masih memulihkan diri. Kalau Stop itu
            // datang sebelum mysqld sempat dipasang, start ini sendiri yang
            // membereskannya, dengan rapi.
            if self.batal(ServiceKind::Db, g) {
                if self.lepas_db_jika(&p) {
                    self.matikan_mysql_rapi(&p, Some(mysql), port);
                }
                return Ok(false);
            }
            if !sudah_bilang && sejak.elapsed() >= PESAN_SABAR {
                sudah_bilang = true;
                self.say(Pesan::dengan(
                    "MySQL belum siap setelah 20 detik - biasanya sedang memulihkan basis data sesudah komputer \
                     mati mendadak. Ditunggu sampai {0} menit; tekan Stop untuk membatalkan. Jangan matikan paksa \
                     lewat Task Manager.",
                    &[&(batas.as_secs() / 60).to_string()],
                ));
            }
            // Mati sendiri di tengah start: dilaporkan SEKALI (pengawas atau di
            // sini), tanpa "gagal siap" atau "MySQL dimatikan" di bawahnya.
            if p.keluar() {
                if self.lepas_db_jika(&p) {
                    self.gagal(
                        ServiceKind::Db,
                        Pesan::dengan("MySQL berhenti sendiri (kode {0}). Lihat logs\\mysql-error.log.", &[&p
                            .kode()
                            .to_string()]),
                    );
                }
                return Ok(false);
            }
            if !port::is_free(port) {
                // Folder data ini kini TERCATAT milik paket ini.
                config::catat_pemilik_data(&data_dir, Some(mysql));
                self.say(Pesan::dengan(
                    "MySQL {0} jalan di port {1} (PID {2}).",
                    &[&mysql.version, &port.to_string(), &p.pid.to_string()],
                ));
                return Ok(self.selesaikan(ServiceKind::Db, g, &p, Some(mysql), port));
            }
            p.tunggu_keluar(Duration::from_millis(500));
        }
        let lama = if batas.as_secs() / 60 > 0 {
            format!("{} menit", batas.as_secs() / 60)
        } else {
            format!("{} detik", batas.as_secs())
        };
        let galat = Pesan::dengan("MySQL gagal siap dalam {0}. Lihat logs\\mysql-error.log.", &[&lama]);
        self.say(galat.clone());
        // Dihentikan dengan RAPI: bisa jadi ia masih menulis hasil pemulihannya.
        if self.lepas_db_jika(&p) && !self.matikan_mysql_rapi(&p, Some(mysql), port) {
            self.say(Pesan::baru("MySQL tidak mau berhenti dengan rapi - dimatikan paksa."));
        }
        self.say(Pesan::baru("MySQL dimatikan."));
        self.ubah(ServiceKind::Db, ServiceState::Gagal, Some(galat));
        Ok(false)
    }

    /// Matikan MySQL dengan rapi - InnoDB tidak suka dibunuh, dan tabel MyISAM
    /// bisa rusak karenanya. Rujukannya dilepas LEBIH DULU, sebelum mysqld
    /// diminta berhenti. `profile`/`mysql` boleh kosong: dipakai yang tercatat
    /// saat mysqld dinyalakan.
    pub fn stop_db(&self, profile: Option<&Profile>, mysql: Option<&BinPackage>) {
        self.naikkan_giliran(ServiceKind::Db);
        let Some(p) = self.ambil_lepas_db() else {
            self.ubah(ServiceKind::Db, ServiceState::Berhenti, None);
            return;
        };
        self.ubah(ServiceKind::Db, ServiceState::Mematikan, None);
        self.say(Pesan::baru("Meminta MySQL berhenti dengan rapi..."));
        let (paket, port) = {
            let d = self.data();
            let port = profile.map(|x| port16(x.mysql_port)).unwrap_or(d.db_port);
            (mysql.cloned().or_else(|| d.db_paket.clone()), port)
        };
        if !self.matikan_mysql_rapi(&p, paket.as_ref(), port) {
            self.say(Pesan::baru("MySQL tidak mau berhenti dengan rapi - dimatikan paksa."));
        }
        self.say(Pesan::baru("MySQL dimatikan."));
        self.ubah(ServiceKind::Db, ServiceState::Berhenti, None);
    }

    /// Minta mysqld berhenti dengan tertib (event dulu, mysqladmin sebagai
    /// cadangan karena butuh root tanpa sandi), tunggu, lalu paksa. Benar bila
    /// ia berhenti sendiri.
    pub(super) fn matikan_mysql_rapi(&self, p: &Arc<Proses>, mysql: Option<&BinPackage>, port: u16) -> bool {
        if p.keluar() {
            return true;
        }
        let diminta = minta_mysql_berhenti(p.pid);
        if !diminta && port > 0 {
            if let Some(m) = mysql {
                let admin = Path::new(&m.path).join("bin").join("mysqladmin.exe");
                if admin.is_file() {
                    let _ = shell::run(
                        &jalur(&admin),
                        &format!("--protocol=tcp --port={port} -u root shutdown"),
                        Some(Path::new(&m.path)),
                        15_000,
                        None,
                        None,
                    );
                }
            }
        }
        if p.tunggu_keluar(BATAS_RAPI) {
            return true;
        }
        shell::kill_tree(p.pid);
        false
    }

    /// Siapkan folder data kosong. `Some(pesan)` = gagal (sudah dicatat).
    fn siapkan_data(&self, mysql: &BinPackage, my_ini: &str, data_dir: &str) -> Lanjut<Option<Pesan>> {
        // Hanya folder yang KOSONG sebelum init yang boleh disingkirkan bila
        // init gagal. Folder yang sudah berisi apa pun bukan buatan kita.
        // Folder yang tidak bisa dibaca dianggap berisi: lebih baik tidak
        // memindah apa pun daripada memindah milik orang.
        let kosong_sebelumnya = !Path::new(data_dir).exists()
            || std::fs::read_dir(data_dir).map(|mut r| r.next().is_none()).unwrap_or(false);
        std::fs::create_dir_all(data_dir).map_err(|e| e.to_string())?;
        let bin = Path::new(&mysql.path).join("bin");
        let install = bin.join("mysql_install_db.exe");
        let hasil = if install.is_file() {
            // MariaDB: mysqld --initialize tidak ada di sana.
            shell::run(
                &jalur(&install),
                &format!("--datadir=\"{data_dir}\" --service= --password="),
                Some(&bin),
                300_000,
                None,
                None,
            )
        } else {
            shell::run(
                &jalur(&bin.join("mysqld.exe")),
                &format!("--defaults-file=\"{my_ini}\" --initialize-insecure --console"),
                Some(&bin),
                300_000,
                None,
                None,
            )
        };
        // Exe yang tidak bisa dijalankan dianggap init gagal (dengan sebabnya),
        // supaya sisanya tetap dibereskan.
        let res = hasil.unwrap_or_else(|e| RunResult { exit_code: -1, stderr: e.to_string(), ..RunResult::default() });
        let semua = res.all();
        // Keluaran init juga ikut --console, jadi disalin dengan cara yang sama.
        if !semua.is_empty() {
            log_file::tambah(&mysql_error_log(), &semua);
        }
        if init_berhasil(&res, data_dir) {
            return Ok(None);
        }
        let galat = Pesan::dengan(
            "Gagal menyiapkan folder data{0}:\n{1}",
            &[if res.timed_out { " (melewati batas waktu)" } else { "" }, &semua],
        );
        self.say(galat.clone());
        // Sisa init yang gagal membuat percobaan berikutnya tertolak ("data
        // directory is not empty") atau dikira sudah siap. Dipindah, tidak
        // dihapus - siapa tahu ada yang ingin melihatnya.
        if kosong_sebelumnya && Path::new(data_dir).is_dir() {
            let sisih = format!("{}.gagal-{}", data_dir.trim_end_matches('\\'), waktu::cap_lokal_berkas());
            match std::fs::rename(data_dir, &sisih) {
                Ok(()) => self.say(Pesan::dengan(
                    "Sisa percobaan yang gagal dipindah ke {0} - start berikutnya akan menyiapkan folder data dari awal.",
                    &[&sisih],
                )),
                Err(e) => self.say(Pesan::dengan(
                    "Sisa percobaan yang gagal tidak bisa dipindah ({0}). Kosongkan {1} sebelum mencoba lagi.",
                    &[&e.to_string(), data_dir],
                )),
            }
        }
        Ok(Some(galat))
    }
}
