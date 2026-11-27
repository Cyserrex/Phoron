//! Fitur dari NodeRunner.cs - menjalankan proyek Node/TypeScript.
//!
//! Menjalankan skrip package.json (next dev, astro dev, vite, ...) sebagai
//! proses anak daemon. Beberapa proyek boleh jalan sekaligus - itu lazim: satu
//! frontend, satu API, satu situs dokumentasi. Keluaran dan perubahan status
//! dikirim sebagai `Peristiwa` lewat penerima (callback atau channel), yang
//! nanti diteruskan daemon sebagai `Kabar`.
//!
//! Daftar periksa (pelajaran dari versi C#):
//! - Dijalankan lewat `cmd.exe /c`: npm/pnpm/yarn di Windows berupa berkas
//!   .cmd yang tidak bisa dijalankan CreateProcess langsung. "run" dilewati
//!   untuk yarn. Uji: `perintah_per_pengelola`.
//! - Port diteruskan dua cara: variabel PORT selalu (Next.js, Nuxt), dan
//!   `--port` untuk kerangka yang tidak membacanya (Vite, Astro, SvelteKit,
//!   Remix). npm/pnpm/bun butuh `--` supaya argumennya sampai ke skrip, bukan
//!   ke pengelolanya. Uji: `perintah_per_pengelola`, `argumen_port_per_kerangka`.
//! - Versi Node pilihan: folder paketnya ditaruh di DEPAN PATH, jadi node.exe
//!   dan npm.cmd di sana yang terpakai. NO_COLOR=1 dan FORCE_COLOR=0 karena
//!   kode warna ANSI jadi sampah di kotak teks. Uji: `nyata_*` (env PORT).
//! - Alamat dibaca dari keluaran server, bukan ditebak: Next 3000, Astro
//!   4321, Vite 5173, dan semuanya bergeser sendiri bila portnya terpakai.
//!   Hanya alamat lokal (localhost, 127.0.0.1, [::1]); titik/koma penutup
//!   kalimat dibuang. Uji: `url_dari_keluaran`, uji setara.
//! - Bentrok port dikenali dari keluaran (EADDRINUSE, "address already in
//!   use", "Port N is (already) in use"); port diambil dari kalimatnya atau
//!   dari ":N" terakhir di ekornya, -1 bila tak tertera. Uji:
//!   `port_dari_keluhan_*`, uji setara.
//! - Bentrok DICATAT hanya bila prosesnya benar-benar berakhir dengan galat:
//!   next dev versi baru mencetak "Port 3000 is in use, trying 3001 instead"
//!   lalu tetap jalan, dan itu bukan kegagalan. Port tak dikenal dicatat
//!   sebagai port yang dipakai saat itu, atau 3000 sebagai tebakan terakhir.
//!   Catatan lama dihapus tiap kali proyek dijalankan. Uji:
//!   `nyata_bentrok_port_dicatat`, `catat_bentrok_hanya_bila_gagal`.
//! - Proses yang berakhir SENDIRI dibedakan dari yang dihentikan lewat
//!   `stop`: dulu keduanya dilaporkan, jadi menekan Hentikan menghasilkan dua
//!   baris dan yang kedua menyebut kode keluar bukan-nol seolah ada yang gagal.
//!   Kini hanya pihak yang pertama melepas entri yang melapor. Uji:
//!   `nyata_jalan_tangkap_hentikan`.
//! - Hentikan = matikan POHON proses, bukan cmd.exe-nya saja: node.exe yang
//!   memegang port ada di bawahnya. Uji: `nyata_jalan_tangkap_hentikan`.
//! - Anak terikat ke Job Object daemon (lihat `process_job`), jadi ikut mati
//!   bila daemon mati. Uji: `nyata_job_ditutup_mematikan_proyek`.
//! - Riwayat keluaran disimpan DI SINI, per proyek, maksimal 400 baris
//!   terakhir: dulu satu-satunya penampung adalah halaman Node, sehingga
//!   berpindah halaman atau menutup ke baki menghilangkan setiap baris -
//!   termasuk galat saat start. Uji: `riwayat_dibatasi_400`.
//! - Baris kosong/spasi saja tidak dilaporkan; folder dibandingkan tanpa
//!   peka besar-kecil (C#: OrdinalIgnoreCase).
//!
//! Beda yang disengaja dengan C#:
//! - Baris "> perintah" dilaporkan SEBELUM keluaran proses mulai dibaca, dan
//!   "-- proses berakhir --" sesudah seluruh keluaran terkuras (paling lama
//!   2 detik menunggu pipa yang dipegang cucu).
//! - `@sveltejs/kit` (nilai yang memang dikembalikan `detect_framework`)
//!   diperlakukan seperti "sveltekit"; di C# perbandingannya tidak pernah
//!   cocok sehingga SvelteKit tidak pernah diberi `--port`.

use crate::model::BinPackage;
use crate::node_apps::{self, NodeApp};
use crate::protokol::{Pesan, StatusNode};
use crate::{port, process_job, shell};
use serde::{Deserialize, Serialize};
use std::collections::{HashMap, VecDeque};
use std::io::Read;
use std::os::windows::process::CommandExt;
use std::path::Path;
use std::process::{Child, ChildStderr, ChildStdout, Command, Stdio};
use std::sync::atomic::{AtomicBool, AtomicI32, AtomicU64, Ordering};
use std::sync::{mpsc, Arc, Mutex};
use std::time::Duration;

/// Jumlah baris keluaran terakhir yang disimpan per proyek.
pub const BATAS_RIWAYAT: usize = 400;

const CREATE_NO_WINDOW: u32 = 0x0800_0000;

/// Satu baris keluaran proyek: mentah dari prosesnya, atau teks Phoron
/// sendiri yang masih harus diterjemahkan UI.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum BarisNode {
    Mentah(String),
    Pesan(Pesan),
}

impl BarisNode {
    /// Teks tanpa terjemahan (kunci Indonesia dengan {n} diganti argumen),
    /// untuk tempat yang hanya bisa menampung string.
    pub fn teks(&self) -> String {
        match self {
            BarisNode::Mentah(s) => s.clone(),
            BarisNode::Pesan(p) => {
                let mut s = p.kunci.clone();
                for (i, a) in p.args.iter().enumerate() {
                    s = s.replace(&format!("{{{i}}}"), a);
                }
                s
            }
        }
    }
}

/// Kabar dari runner; `folder` = folder proyek seperti yang diberikan.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Peristiwa {
    /// Satu baris keluaran (juga disimpan di riwayat).
    Keluaran { folder: String, baris: BarisNode },
    /// Proyek mulai atau berhenti.
    Status { folder: String, berjalan: bool },
    /// Alamat server terdeteksi dari keluaran.
    Url { folder: String, url: String },
}

type Penerima = Arc<dyn Fn(Peristiwa) + Send + Sync>;

struct Berjalan {
    /// Pembeda entri untuk folder yang sama: jalan ulang tidak boleh dilepas
    /// oleh pengawas jalan sebelumnya.
    id: u64,
    pid: u32,
    folder: String,
    url: Mutex<Option<String>>,
    /// Port yang dikeluhkan "sudah dipakai" di keluarannya; 0 bila tidak ada.
    bentrok: AtomicI32,
    selesai: AtomicBool,
}

#[derive(Default)]
struct Data {
    jalan: HashMap<String, Arc<Berjalan>>,
    /// Proyek yang terakhir BERHENTI karena portnya dipakai proses lain.
    /// Port bentrok jalan terakhir per proyek: (folder asli, port).
    bentrok: HashMap<String, (String, i32)>,
    riwayat: HashMap<String, VecDeque<BarisNode>>,
}

struct Inti {
    data: Mutex<Data>,
    penerima: Penerima,
    urutan: AtomicU64,
}

/// Pengelola proyek Node yang sedang berjalan. Murah di-clone; semua salinan
/// berbagi keadaan yang sama.
#[derive(Clone)]
pub struct NodeRunner {
    inti: Arc<Inti>,
    /// Job khusus (uji); `None` = job daemon.
    job: Option<Arc<process_job::Job>>,
}

fn kunci(folder: &str) -> String {
    folder.to_lowercase()
}

impl NodeRunner {
    pub fn baru(penerima: impl Fn(Peristiwa) + Send + Sync + 'static) -> NodeRunner {
        NodeRunner {
            inti: Arc::new(Inti {
                data: Mutex::new(Data::default()),
                penerima: Arc::new(penerima),
                urutan: AtomicU64::new(1),
            }),
            job: None,
        }
    }

    /// Runner yang peristiwanya dikirim ke channel.
    pub fn dengan_saluran() -> (NodeRunner, mpsc::Receiver<Peristiwa>) {
        let (tx, rx) = mpsc::channel();
        (NodeRunner::baru(move |p| drop(tx.send(p))), rx)
    }

    fn data(&self) -> std::sync::MutexGuard<'_, Data> {
        self.inti.data()
    }

    pub fn is_running(&self, folder: &str) -> bool {
        self.data().jalan.get(&kunci(folder)).is_some_and(|b| !b.selesai.load(Ordering::SeqCst))
    }

    pub fn url_of(&self, folder: &str) -> Option<String> {
        self.data().jalan.get(&kunci(folder)).and_then(|b| b.url.lock().unwrap().clone())
    }

    pub fn pid(&self, folder: &str) -> Option<u32> {
        self.data().jalan.get(&kunci(folder)).map(|b| b.pid)
    }

    /// Folder proyek yang sedang berjalan, dengan ejaan saat dijalankan.
    pub fn running_folders(&self) -> Vec<String> {
        self.data().jalan.values().map(|b| b.folder.clone()).collect()
    }

    /// Port yang membuat proyek ini gagal start terakhir kali; 0 bila tidak
    /// ada.
    pub fn port_bentrok(&self, folder: &str) -> i32 {
        self.data().bentrok.get(&kunci(folder)).map(|b| b.1).unwrap_or(0)
    }

    /// Port yang ditawarkan untuk proyek yang gagal karena portnya terpakai:
    /// port bebas pertama sesudah port bentrok, dalam 50 port, melewati
    /// `lewati` (port web/MySQL profil aktif). Padanan Engine.PortTawaran.
    pub fn port_tawaran(&self, folder: &str, lewati: &[u16]) -> Option<u16> {
        let b = self.port_bentrok(folder);
        if b <= 0 {
            return None;
        }
        let mulai = b + 1;
        (mulai..(mulai + 50).min(65536))
            .filter_map(|p| u16::try_from(p).ok())
            .find(|p| !lewati.contains(p) && port::is_free(*p))
    }

    /// Status semua proyek yang sedang jalan atau pernah bentrok, untuk
    /// `Keadaan` di protokol.
    pub fn status_semua(&self) -> Vec<StatusNode> {
        let d = self.data();
        let mut v: Vec<StatusNode> = d
            .jalan
            .iter()
            .map(|(k, b)| StatusNode {
                path: b.folder.clone(),
                berjalan: !b.selesai.load(Ordering::SeqCst),
                url: b.url.lock().unwrap().clone(),
                port_bentrok: d.bentrok.get(k).and_then(|p| u16::try_from(p.1).ok()),
            })
            .collect();
        // Proyek yang berhenti KARENA bentrok sudah keluar dari peta jalan,
        // padahal justru dialah yang butuh tawaran "Jalankan di port N".
        for (k, (folder, p)) in &d.bentrok {
            if !d.jalan.contains_key(k) {
                v.push(StatusNode {
                    path: folder.clone(),
                    berjalan: false,
                    url: None,
                    port_bentrok: u16::try_from(*p).ok(),
                });
            }
        }
        v.sort_by(|a, b| a.path.cmp(&b.path));
        v
    }

    /// Salinan keluaran terakhir sebuah proyek, terlama lebih dulu.
    pub fn riwayat(&self, folder: &str) -> Vec<BarisNode> {
        self.data().riwayat.get(&kunci(folder)).map(|q| q.iter().cloned().collect()).unwrap_or_default()
    }

    /// Sisipkan satu baris ke keluaran proyek seolah dari prosesnya (mis.
    /// peringatan HSTS dari Engine), supaya terbaca di tempat pengguna sedang
    /// menatap.
    pub fn sisipkan(&self, folder: &str, baris: BarisNode) {
        self.inti.lapor(folder, baris);
    }

    /// Jalankan skrip proyek. `port_sekali` > 0 memakai port itu untuk kali
    /// ini saja; selain itu port tetap proyeknya (0 = pilihan servernya).
    /// `node` = paket Node pilihan; `None` = ikut PATH sistem.
    pub fn start(&self, app: &NodeApp, node: Option<&BinPackage>, port_sekali: u16) -> Result<(), Pesan> {
        let folder = app.path.as_str();
        if folder.trim().is_empty() {
            return Err(Pesan::baru("Proyek belum punya folder."));
        }
        if !Path::new(folder).is_dir() {
            return Err(Pesan::dengan("Folder tidak ada: {0}", &[folder]));
        }
        if !node_apps::looks_like_node_project(folder) {
            return Err(Pesan::dengan("Tidak ada package.json di {0}.", &[folder]));
        }
        let rencana = Rencana::dari(app, port_sekali);

        let mut cmd = Command::new("cmd.exe");
        cmd.raw_arg(format!("/c {}", rencana.perintah))
            .current_dir(folder)
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped());
        if let Some(jalur) = node.map(|n| n.path.as_str()).filter(|p| !p.is_empty()) {
            let path = std::env::var("PATH").unwrap_or_default();
            cmd.env("PATH", format!("{jalur};{path}"));
        }
        cmd.env("NO_COLOR", "1").env("FORCE_COLOR", "0");
        if rencana.port > 0 {
            cmd.env("PORT", rencana.port.to_string());
        }

        let k = kunci(folder);
        let (mut anak, entri) = {
            let mut d = self.data();
            if d.jalan.get(&k).is_some_and(|b| !b.selesai.load(Ordering::SeqCst)) {
                return Err(Pesan::baru("Proyek ini sudah jalan."));
            }
            d.bentrok.remove(&k);
            let hasil = match &self.job {
                Some(j) => j.spawn(&mut cmd, CREATE_NO_WINDOW),
                None => process_job::spawn_terikat(&mut cmd, CREATE_NO_WINDOW),
            };
            let anak = match hasil {
                Ok(a) => a,
                // Kunci tanpa {0}: UI menambahkan argumennya di belakang,
                // seperti Lang.T("Tidak bisa menjalankan: ") + ex.Message.
                Err(e) => return Err(Pesan::dengan("Tidak bisa menjalankan: ", &[&e.to_string()])),
            };
            let entri = Arc::new(Berjalan {
                id: self.inti.urutan.fetch_add(1, Ordering::SeqCst),
                pid: anak.id(),
                folder: folder.to_string(),
                url: Mutex::new(None),
                bentrok: AtomicI32::new(0),
                selesai: AtomicBool::new(false),
            });
            d.jalan.insert(k, entri.clone());
            (anak, entri)
        };

        // Dilaporkan sebelum pembaca keluaran dimulai: baris perintah selalu
        // yang pertama di riwayat.
        self.inti.lapor(folder, BarisNode::Mentah(format!("> {}   (di {})", rencana.perintah, folder)));
        self.inti.ubah(folder, true);

        let (tx, rx) = mpsc::channel::<()>();
        let so = anak.stdout.take();
        let se = anak.stderr.take();
        if let Some(s) = so {
            self.baca(Sumber::Out(s), entri.clone(), tx.clone());
        }
        if let Some(s) = se {
            self.baca(Sumber::Err(s), entri.clone(), tx.clone());
        }
        drop(tx);
        let inti = self.inti.clone();
        let port_dipakai = rencana.port;
        std::thread::spawn(move || inti.awasi(&mut anak, entri, rx, port_dipakai));
        Ok(())
    }

    fn baca(&self, sumber: Sumber, entri: Arc<Berjalan>, selesai: mpsc::Sender<()>) {
        let inti = self.inti.clone();
        std::thread::spawn(move || {
            per_baris(sumber, |baris| {
                if baris.trim().is_empty() {
                    return;
                }
                let keluh = port_dari_keluhan(&baris);
                if keluh != 0 && entri.bentrok.load(Ordering::SeqCst) <= 0 {
                    entri.bentrok.store(keluh, Ordering::SeqCst);
                }
                let url = url_dari_baris(&baris);
                inti.lapor(&entri.folder, BarisNode::Mentah(baris));
                if let Some(u) = url {
                    let baru = {
                        let mut g = entri.url.lock().unwrap();
                        if g.is_none() {
                            *g = Some(u.clone());
                            true
                        } else {
                            false
                        }
                    };
                    if baru {
                        (inti.penerima)(Peristiwa::Url { folder: entri.folder.clone(), url: u });
                    }
                }
            });
            drop(selesai);
        });
    }

    /// Hentikan proyek beserta pohon prosesnya.
    pub fn stop(&self, folder: &str) {
        // Dilepas dulu supaya pengawas tidak ikut melapor.
        let Some(b) = self.data().jalan.remove(&kunci(folder)) else { return };
        if !b.selesai.load(Ordering::SeqCst) {
            shell::kill_tree(b.pid);
        }
        self.inti.lapor(folder, BarisNode::Pesan(Pesan::baru("-- dihentikan --")));
        self.inti.ubah(folder, false);
    }

    pub fn stop_all(&self) {
        for f in self.running_folders() {
            self.stop(&f);
        }
    }
}

impl Inti {
    fn data(&self) -> std::sync::MutexGuard<'_, Data> {
        self.data.lock().unwrap_or_else(|e| e.into_inner())
    }

    fn lapor(&self, folder: &str, baris: BarisNode) {
        {
            let mut d = self.data();
            let q = d.riwayat.entry(kunci(folder)).or_default();
            q.push_back(baris.clone());
            // Server pengembangan bisa mencetak ribuan baris; hanya ekornya
            // yang berguna.
            while q.len() > BATAS_RIWAYAT {
                q.pop_front();
            }
        }
        (self.penerima)(Peristiwa::Keluaran { folder: folder.to_string(), baris });
    }

    fn ubah(&self, folder: &str, berjalan: bool) {
        (self.penerima)(Peristiwa::Status { folder: folder.to_string(), berjalan });
    }

    /// Uji-lalu-lepas dalam satu langkah: benar hanya bagi pihak pertama.
    fn lepas_jika(&self, b: &Berjalan) -> bool {
        let mut d = self.data();
        let k = kunci(&b.folder);
        if d.jalan.get(&k).is_some_and(|x| x.id == b.id) {
            d.jalan.remove(&k);
            true
        } else {
            false
        }
    }

    fn awasi(&self, anak: &mut Child, b: Arc<Berjalan>, pembaca: mpsc::Receiver<()>, port: u16) {
        let kode = anak.wait().ok().and_then(|s| s.code()).unwrap_or(-1);
        // Kuras keluaran dulu, tapi jangan menunggu selamanya pipa yang masih
        // dipegang cucu yang lolos.
        // Pembaca tidak pernah mengirim apa pun; channel putus saat keduanya
        // selesai.
        let _ = pembaca.recv_timeout(Duration::from_secs(2));
        b.selesai.store(true, Ordering::SeqCst);
        // Rujukannya masih kita berarti proses berakhir SENDIRI; kalau sudah
        // dilepas, stop() yang menghentikannya dan sudah melapor.
        if !self.lepas_jika(&b) {
            return;
        }
        let bentrok = b.bentrok.load(Ordering::SeqCst);
        if kode != 0 && bentrok != 0 {
            let p = catat_bentrok(bentrok, port);
            self.data().bentrok.insert(kunci(&b.folder), (b.folder.clone(), p));
        }
        let baris = if kode == 0 {
            Pesan::baru("-- proses berakhir (kode 0) --")
        } else {
            Pesan::dengan("-- proses berakhir dengan galat (kode {0}) --", &[&kode.to_string()])
        };
        self.lapor(&b.folder, BarisNode::Pesan(baris));
        self.ubah(&b.folder, false);
    }
}

/// Port yang dicatat sebagai sebab gagal: dari keluhan, atau port yang
/// dipakai saat itu, atau 3000 sebagai tebakan terakhir.
fn catat_bentrok(keluhan: i32, port: u16) -> i32 {
    if keluhan > 0 {
        keluhan
    } else if port > 0 {
        port as i32
    } else {
        3000
    }
}

enum Sumber {
    Out(ChildStdout),
    Err(ChildStderr),
}

impl Read for Sumber {
    fn read(&mut self, buf: &mut [u8]) -> std::io::Result<usize> {
        match self {
            Sumber::Out(s) => s.read(buf),
            Sumber::Err(s) => s.read(buf),
        }
    }
}

/// Seperti BeginOutputReadLine: pemisah \r\n, \n, atau \r sendirian; UTF-8.
fn per_baris(mut r: impl Read, mut f: impl FnMut(String)) {
    let mut buf = [0u8; 8192];
    let mut baris: Vec<u8> = Vec::new();
    let mut habis_cr = false;
    loop {
        let n = match r.read(&mut buf) {
            Ok(0) | Err(_) => break,
            Ok(n) => n,
        };
        for &c in &buf[..n] {
            match c {
                b'\n' if habis_cr => habis_cr = false,
                b'\r' | b'\n' => {
                    habis_cr = c == b'\r';
                    f(String::from_utf8_lossy(&baris).into_owned());
                    baris.clear();
                }
                _ => {
                    habis_cr = false;
                    baris.push(c);
                }
            }
        }
    }
    if !baris.is_empty() {
        f(String::from_utf8_lossy(&baris).into_owned());
    }
}

/// Baris perintah dan port yang akan dipakai.
#[derive(Debug, PartialEq, Eq)]
struct Rencana {
    perintah: String,
    port: u16,
}

impl Rencana {
    fn dari(app: &NodeApp, port_sekali: u16) -> Rencana {
        let kerangka = node_apps::detect_framework(&app.path);
        Rencana::susun(app, kerangka, port_sekali)
    }

    fn susun(app: &NodeApp, kerangka: &str, port_sekali: u16) -> Rencana {
        let manager = if app.manager.trim().is_empty() { "npm" } else { app.manager.trim() };
        let skrip = if app.script.trim().is_empty() { "dev" } else { app.script.trim() };
        let port = if port_sekali > 0 { port_sekali } else { u16::try_from(app.port.max(0)).unwrap_or(0) };
        let mut perintah =
            if manager == "yarn" { format!("{manager} {skrip}") } else { format!("{manager} run {skrip}") };
        if port > 0 && butuh_argumen_port(kerangka) {
            perintah.push_str(if manager == "yarn" { " --port " } else { " -- --port " });
            perintah.push_str(&port.to_string());
        }
        Rencana { perintah, port }
    }
}

/// Kerangka yang tidak membaca variabel PORT dan butuh `--port` di baris
/// perintahnya. Next.js dan Nuxt membaca PORT.
pub fn butuh_argumen_port(kerangka: &str) -> bool {
    matches!(kerangka.to_lowercase().as_str(), "vite" | "astro" | "sveltekit" | "@sveltejs/kit" | "remix")
}

// ---------------------------------------------------------------------------
// Pengenalan keluaran. Ditulis tangan (tanpa crate regex) dengan semantik
// yang sama dengan pola C#:
//   bentrok: EADDRINUSE|address already in use|port\s+(\d+)\s+is\s+(?:already\s+)?in\s+use  (abaikan besar-kecil)
//   ekor   : :(\d{2,5})\b   - kecocokan TERAKHIR sejak awal keluhan
//   url    : https?://(?:localhost|127\.0\.0\.1|\[::1\])(?::\d+)?(?:/\S*)?  (abaikan besar-kecil)
// ---------------------------------------------------------------------------

/// Teks ASCII `pola` ada di `s[i..]`, tanpa peka besar-kecil. Mengembalikan
/// indeks sesudahnya.
fn kata(s: &str, i: usize, pola: &str) -> Option<usize> {
    let b = s.as_bytes();
    let akhir = i + pola.len();
    (akhir <= b.len() && b[i..akhir].eq_ignore_ascii_case(pola.as_bytes())).then_some(akhir)
}

/// `\s+`: minimal satu spasi.
fn spasi(s: &str, i: usize) -> Option<usize> {
    let n: usize = s[i..].chars().take_while(|c| c.is_whitespace()).map(char::len_utf8).sum();
    (n > 0).then_some(i + n)
}

/// `\d+`: minimal satu angka.
fn angka(s: &str, i: usize) -> Option<usize> {
    let n = s.as_bytes()[i..].iter().take_while(|c| c.is_ascii_digit()).count();
    (n > 0).then_some(i + n)
}

/// `port\s+(\d+)\s+is\s+(?:already\s+)?in\s+use` di posisi i; hasil = isi grup.
fn keluhan_port(s: &str, i: usize) -> Option<&str> {
    let j = spasi(s, kata(s, i, "port")?)?;
    let a = angka(s, j)?;
    let k = spasi(s, kata(s, spasi(s, a)?, "is")?)?;
    let k = kata(s, k, "already").and_then(|x| spasi(s, x)).unwrap_or(k);
    let k = spasi(s, kata(s, k, "in")?)?;
    kata(s, k, "use")?;
    Some(&s[j..a])
}

/// Port dari baris keluaran yang mengeluhkan port terpakai; 0 bila bukan
/// baris itu, -1 bila bentrok tapi portnya tidak tertera.
pub fn port_dari_keluhan(baris: &str) -> i32 {
    for (i, _) in baris.char_indices() {
        if let Some(g) = keluhan_port(baris, i) {
            if let Ok(p) = g.parse::<i32>() {
                return p;
            }
            return port_ekor(&baris[i..]);
        }
        if kata(baris, i, "EADDRINUSE").is_some() || kata(baris, i, "address already in use").is_some() {
            return port_ekor(&baris[i..]);
        }
    }
    0
}

/// `:(\d{2,5})\b` terakhir; -1 bila tidak ada.
fn port_ekor(s: &str) -> i32 {
    let b = s.as_bytes();
    let mut hasil = -1;
    for (i, _) in s.match_indices(':') {
        let n = b[i + 1..].iter().take_while(|c| c.is_ascii_digit()).count();
        if !(2..=5).contains(&n) {
            continue;
        }
        // \b: sesudah angka tidak boleh huruf/angka/garis bawah.
        let sesudah = s[i + 1 + n..].chars().next();
        if sesudah.is_some_and(|c| c.is_alphanumeric() || c == '_') {
            continue;
        }
        if let Ok(p) = s[i + 1..i + 1 + n].parse::<i32>() {
            hasil = p;
        }
    }
    hasil
}

/// Alamat server lokal pertama di baris keluaran, tanpa titik/koma penutup.
pub fn url_dari_baris(baris: &str) -> Option<String> {
    for (i, _) in baris.char_indices() {
        let Some(mut j) = kata(baris, i, "http") else { continue };
        if let Some(x) = kata(baris, j, "s").filter(|x| kata(baris, *x, "://").is_some()) {
            j = x;
        }
        let Some(j) = kata(baris, j, "://") else { continue };
        let Some(mut j) =
            ["localhost", "127.0.0.1", "[::1]"].iter().find_map(|h| kata(baris, j, h))
        else {
            continue;
        };
        if baris.as_bytes().get(j) == Some(&b':') {
            if let Some(x) = angka(baris, j + 1) {
                j = x;
            }
        }
        if baris.as_bytes().get(j) == Some(&b'/') {
            j += baris[j..].chars().take_while(|c| !c.is_whitespace()).map(char::len_utf8).sum::<usize>();
        }
        return Some(baris[i..j].trim_end_matches(['.', ',']).to_string());
    }
    None
}

#[cfg(test)]
mod uji {
    use super::*;
    use std::fs;
    use std::time::Instant;

    #[test]
    fn port_dari_keluhan_kerangka() {
        assert_eq!(port_dari_keluhan("Error: listen EADDRINUSE: address already in use :::3000"), 3000);
        assert_eq!(port_dari_keluhan("Error: listen EADDRINUSE: address already in use 127.0.0.1:5173"), 5173);
        assert_eq!(port_dari_keluhan(" ⚠ Port 3000 is in use, trying 3001 instead."), 3000);
        assert_eq!(port_dari_keluhan("error when starting dev server: Error: Port 5173 is already in use"), 5173);
        assert_eq!(port_dari_keluhan("PORT\t4321  IS ALREADY   IN USE"), 4321);
        assert_eq!(port_dari_keluhan("  code: 'EADDRINUSE',"), -1);
        assert_eq!(port_dari_keluhan("  port: 3000"), 0);
        assert_eq!(port_dari_keluhan(""), 0);
    }

    #[test]
    fn port_dari_keluhan_tepi() {
        // Ekor terakhir, dan hanya sesudah awal keluhan.
        assert_eq!(port_dari_keluhan("address already in use :::5173 dan :8080"), 8080);
        assert_eq!(port_dari_keluhan("x:1234 EADDRINUSE :22"), 22);
        assert_eq!(port_dari_keluhan("x:1234 EADDRINUSE"), -1);
        // \d{2,5}\b: terlalu panjang atau diikuti huruf tidak dihitung.
        assert_eq!(port_dari_keluhan("EADDRINUSE :::99999999"), -1);
        assert_eq!(port_dari_keluhan("EADDRINUSE :3000ms"), -1);
        assert_eq!(port_dari_keluhan("EADDRINUSE :3000."), 3000);
        // Angka grup terlalu besar untuk int: jatuh ke ekor.
        assert_eq!(port_dari_keluhan("port 123456789012 is in use"), -1);
        assert_eq!(port_dari_keluhan("port 99999999999 is in use at :3005"), 3005);
        // Keluhan pertama dari kiri yang menang.
        assert_eq!(port_dari_keluhan("EADDRINUSE lalu port 3000 is in use"), -1);
        assert_eq!(port_dari_keluhan("port 3000 is in use; EADDRINUSE :4000"), 3000);
    }

    #[test]
    fn url_dari_keluaran() {
        assert_eq!(url_dari_baris("  - Local:        http://localhost:3000").as_deref(), Some("http://localhost:3000"));
        assert_eq!(url_dari_baris("  ➜  Local:   http://localhost:5173/").as_deref(), Some("http://localhost:5173/"));
        assert_eq!(url_dari_baris("┃ Local    http://localhost:4321/").as_deref(), Some("http://localhost:4321/"));
        assert_eq!(url_dari_baris("Siap di http://127.0.0.1:8080/api.").as_deref(), Some("http://127.0.0.1:8080/api"));
        assert_eq!(url_dari_baris("di http://[::1]:3000, ya").as_deref(), Some("http://[::1]:3000"));
        assert_eq!(url_dari_baris("HTTPS://LOCALHOST:443/x").as_deref(), Some("HTTPS://LOCALHOST:443/x"));
        assert_eq!(url_dari_baris("http://localhost:abc").as_deref(), Some("http://localhost"));
        assert_eq!(url_dari_baris("http://localhostx").as_deref(), Some("http://localhost"));
        assert_eq!(url_dari_baris("  - Network:      http://192.168.1.5:3000"), None);
        assert_eq!(url_dari_baris("https://example.com lalu http://localhost:1/").as_deref(), Some("http://localhost:1/"));
        assert_eq!(url_dari_baris("httpx://localhost http://localhost:9"), Some("http://localhost:9".into()));
    }

    #[test]
    fn argumen_port_per_kerangka() {
        for k in ["vite", "Astro", "sveltekit", "@sveltejs/kit", "remix"] {
            assert!(butuh_argumen_port(k), "{k}");
        }
        for k in ["next", "nuxt", "", "express"] {
            assert!(!butuh_argumen_port(k), "{k}");
        }
    }

    #[test]
    fn perintah_per_pengelola() {
        let app = |m: &str, s: &str, port: i32| NodeApp {
            path: r"D:\web\x".into(),
            manager: m.into(),
            script: s.into(),
            port,
            ..NodeApp::default()
        };
        let r = |a: &NodeApp, k: &str, p: u16| Rencana::susun(a, k, p).perintah;
        assert_eq!(r(&app("npm", "dev", 0), "vite", 0), "npm run dev");
        assert_eq!(r(&app(" ", " ", 0), "", 0), "npm run dev");
        assert_eq!(r(&app("yarn", "dev", 5173), "vite", 0), "yarn dev --port 5173");
        assert_eq!(r(&app("pnpm", "start", 4000), "astro", 0), "pnpm run start -- --port 4000");
        assert_eq!(r(&app("bun", "dev", 0), "vite", 3002), "bun run dev -- --port 3002");
        // Next membaca PORT: tanpa --port, tapi port tetap dipakai.
        let n = Rencana::susun(&app("npm", "dev", 3001), "next", 0);
        assert_eq!(n, Rencana { perintah: "npm run dev".into(), port: 3001 });
        // Port sekali menang atas port tetap.
        assert_eq!(Rencana::susun(&app("npm", "dev", 3001), "next", 3009).port, 3009);
    }

    #[test]
    fn pemisah_baris_seperti_dotnet() {
        let mut v = Vec::new();
        per_baris(&b"a\r\nb\nc\rd\r\r\ne\xff"[..], |s| v.push(s));
        assert_eq!(v, vec!["a", "b", "c", "d", "", "e\u{fffd}"]);
    }

    #[test]
    fn catat_bentrok_hanya_bila_gagal() {
        assert_eq!(catat_bentrok(5173, 3000), 5173);
        assert_eq!(catat_bentrok(-1, 3005), 3005);
        assert_eq!(catat_bentrok(-1, 0), 3000);
    }

    #[test]
    fn riwayat_dibatasi_400() {
        let (r, rx) = NodeRunner::dengan_saluran();
        for i in 0..450 {
            r.sisipkan(r"D:\Web\A", BarisNode::Mentah(format!("baris {i}")));
        }
        let h = r.riwayat(r"d:\web\a");
        assert_eq!(h.len(), BATAS_RIWAYAT);
        assert_eq!(h[0], BarisNode::Mentah("baris 50".into()));
        assert_eq!(rx.try_iter().count(), 450);
        assert!(r.riwayat(r"D:\lain").is_empty());
    }

    #[test]
    fn galat_start_dalam_pesan() {
        let (r, _rx) = NodeRunner::dengan_saluran();
        let mut app = NodeApp::default();
        assert_eq!(r.start(&app, None, 0), Err(Pesan::baru("Proyek belum punya folder.")));
        app.path = r"Z:\tidak\ada\sama\sekali".into();
        assert_eq!(r.start(&app, None, 0).unwrap_err().kunci, "Folder tidak ada: {0}");
        let d = std::env::temp_dir().join(format!("phoron-rs-node-kosong-{}", std::process::id()));
        fs::create_dir_all(&d).unwrap();
        app.path = d.to_string_lossy().into_owned();
        assert_eq!(r.start(&app, None, 0), Err(Pesan::dengan("Tidak ada package.json di {0}.", &[&app.path])));
        let _ = fs::remove_dir_all(&d);
        assert_eq!(
            BarisNode::Pesan(Pesan::dengan("-- proses berakhir dengan galat (kode {0}) --", &["1"])).teks(),
            "-- proses berakhir dengan galat (kode 1) --"
        );
    }

    // --- Uji nyata: butuh node dan npm di PATH; dilewati diam-diam bila tidak ada.

    fn ada_npm() -> bool {
        shell::run("cmd.exe", "/c npm --version", Some(&std::env::temp_dir()), 30_000, None, None)
            .map(|r| r.ok())
            .unwrap_or(false)
    }

    /// Proyek kecil: server http yang mencetak alamatnya, satu baris stderr.
    fn proyek(nama: &str) -> (std::path::PathBuf, NodeApp) {
        let d = std::env::temp_dir().join(format!("phoron-rs-node-{}-{}", nama, std::process::id()));
        let _ = fs::remove_dir_all(&d);
        fs::create_dir_all(&d).unwrap();
        fs::write(d.join("package.json"), r#"{ "name": "uji", "private": true, "scripts": { "dev": "node server.js" } }"#)
            .unwrap();
        fs::write(
            d.join("server.js"),
            "const p = +process.env.PORT || 3999;\n\
             require('http').createServer((q, s) => s.end('ok')).listen(p, '127.0.0.1', () => {\n\
               console.log('Siap di http://localhost:' + p + '/.');\n\
               console.error('peringatan uji ' + process.env.NO_COLOR);\n\
             });\n",
        )
        .unwrap();
        let app = NodeApp { path: d.to_string_lossy().into_owned(), ..NodeApp::default() };
        (d, app)
    }

    fn port_bebas() -> u16 {
        std::net::TcpListener::bind("127.0.0.1:0").unwrap().local_addr().unwrap().port()
    }

    /// Kumpulkan peristiwa sampai `selesai` benar atau waktu habis.
    fn tunggu(rx: &mpsc::Receiver<Peristiwa>, semua: &mut Vec<Peristiwa>, selesai: impl Fn(&[Peristiwa]) -> bool) -> bool {
        let batas = Instant::now() + Duration::from_secs(30);
        while Instant::now() < batas {
            if selesai(semua) {
                return true;
            }
            if let Ok(p) = rx.recv_timeout(Duration::from_millis(100)) {
                semua.push(p);
            }
        }
        selesai(semua)
    }

    fn ada_url(v: &[Peristiwa]) -> bool {
        v.iter().any(|p| matches!(p, Peristiwa::Url { .. }))
    }

    fn berhenti(v: &[Peristiwa]) -> bool {
        v.iter().any(|p| matches!(p, Peristiwa::Status { berjalan: false, .. }))
    }

    #[test]
    fn nyata_jalan_tangkap_hentikan() {
        if !ada_npm() {
            return;
        }
        let (d, mut app) = proyek("jalan");
        let port = port_bebas();
        app.port = port as i32;
        let (r, rx) = NodeRunner::dengan_saluran();
        r.start(&app, None, 0).unwrap();
        assert_eq!(r.start(&app, None, 0), Err(Pesan::baru("Proyek ini sudah jalan.")));
        let mut v = Vec::new();
        assert!(tunggu(&rx, &mut v, ada_url), "alamat tidak terdeteksi: {v:?}");
        let url = format!("http://localhost:{port}/");
        assert_eq!(r.url_of(&app.path.to_uppercase()), Some(url.clone()));
        assert!(tunggu(&rx, &mut v, |v| v
            .iter()
            .any(|p| matches!(p, Peristiwa::Keluaran { baris: BarisNode::Mentah(s), .. } if s == "peringatan uji 1"))));
        let h = r.riwayat(&app.path);
        assert_eq!(h[0], BarisNode::Mentah(format!("> npm run dev   (di {})", app.path)));

        let pid = r.pid(&app.path).unwrap();
        let pohon = process_job::keturunan(pid);
        assert!(!pohon.is_empty());
        r.stop(&app.path);
        assert!(!r.is_running(&app.path));
        assert!(!process_job::masih_hidup(pid));
        assert!(pohon.iter().all(|p| !process_job::masih_hidup(*p)), "node.exe masih hidup");
        // Hentikan sengaja: satu baris "-- dihentikan --", tanpa "berakhir".
        std::thread::sleep(Duration::from_millis(500));
        v.extend(rx.try_iter());
        let teks: Vec<String> = r.riwayat(&app.path).iter().map(BarisNode::teks).collect();
        assert_eq!(teks.last().map(String::as_str), Some("-- dihentikan --"));
        assert!(!teks.iter().any(|t| t.starts_with("-- proses berakhir")), "{teks:?}");
        assert_eq!(v.iter().filter(|p| matches!(p, Peristiwa::Status { berjalan: false, .. })).count(), 1);
        assert_eq!(r.port_bentrok(&app.path), 0);
        let _ = fs::remove_dir_all(&d);
    }

    #[test]
    fn nyata_bentrok_port_dicatat() {
        if !ada_npm() {
            return;
        }
        let (d, mut app) = proyek("bentrok");
        let kunci_port = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
        let port = kunci_port.local_addr().unwrap().port();
        app.port = port as i32;
        let (r, rx) = NodeRunner::dengan_saluran();
        r.start(&app, None, 0).unwrap();
        let mut v = Vec::new();
        assert!(tunggu(&rx, &mut v, berhenti), "proses tidak berhenti: {v:?}");
        assert_eq!(r.port_bentrok(&app.path), port as i32);
        let akhir = r.riwayat(&app.path).last().cloned().unwrap();
        assert!(matches!(&akhir, BarisNode::Pesan(p) if p.kunci == "-- proses berakhir dengan galat (kode {0}) --"));
        // Tawaran: port bebas sesudah port bentrok.
        let t = r.port_tawaran(&app.path, &[]).unwrap();
        assert!(t > port);
        // Proyek yang berhenti karena bentrok tetap dilaporkan, beserta
        // portnya, supaya UI bisa menawarkan "Jalankan di port N".
        let st = r.status_semua();
        assert_eq!(st.len(), 1);
        assert!(!st[0].berjalan);
        assert_eq!(st[0].port_bentrok, Some(port));
        let _ = fs::remove_dir_all(&d);
    }

    #[test]
    fn nyata_job_ditutup_mematikan_proyek() {
        if !ada_npm() {
            return;
        }
        let (d, mut app) = proyek("job");
        app.port = port_bebas() as i32;
        let job = Arc::new(process_job::Job::baru().unwrap());
        let (mut r, rx) = NodeRunner::dengan_saluran();
        r.job = Some(job.clone());
        r.start(&app, None, 0).unwrap();
        let mut v = Vec::new();
        assert!(tunggu(&rx, &mut v, ada_url));
        let pid = r.pid(&app.path).unwrap();
        let pohon = process_job::keturunan(pid);
        assert!(!pohon.is_empty());
        r.job = None;
        drop(job);
        // Semua pemegang job sudah lepas: Windows mematikan seluruh pohon,
        // dan runner melapor proses berakhir sendiri.
        assert!(tunggu(&rx, &mut v, berhenti), "proses tidak mati saat job ditutup");
        assert!(!process_job::masih_hidup(pid));
        assert!(pohon.iter().all(|p| !process_job::masih_hidup(*p)));
        assert!(!r.is_running(&app.path));
        let _ = fs::remove_dir_all(&d);
    }
}
