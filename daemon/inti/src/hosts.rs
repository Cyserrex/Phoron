//! Fitur: blok Phoron di berkas hosts Windows (HostsFile.cs + HostsTool.cs).
//!
//! Berkas hosts milik sistem, bukan milik Phoron: barisnya bisa berisi server
//! kantor, penghalang iklan, atau apa pun yang dipasang orang bertahun-tahun
//! lalu. Phoron hanya boleh menambah dan membuang bloknya sendiri, yang diapit
//! `# === Phoron mulai ===` dan `# === Phoron selesai ===`.
//!
//! Daftar periksa (tiap butir adalah kasus nyata di versi C#, dan punya uji):
//! - Gagal MEMBACA berarti gagal menulis. Dulu pembacaan yang memaafkan
//!   (kunci antivirus, pembantu ber-admin yang berbarengan) mengembalikan
//!   larik kosong, lalu hosts ditulis ulang HANYA berisi blok Phoron - seluruh
//!   baris pengguna lenyap tanpa galat. Pembacaan untuk menulis kini wajib
//!   berhasil ([`sync`]); pembacaan longgar hanya untuk menampilkan.
//! - Tiap baris bermakna di luar blok Phoron yang tadinya ada harus masih ada
//!   sesudahnya; bila tidak, penulisan dibatalkan (penjaga menyeluruh).
//! - Keadaan sebelumnya selalu dicadangkan lebih dulu, di `<data>\hosts-backup`
//!   (BUKAN di System32\drivers\etc: berkas asing di sana memancing antivirus).
//!   `hosts-asli.bak` dibuat sekali seumur mesin, disalin BYTE DEMI BYTE (salinan
//!   "asli" yang ikut rusak encoding-nya bukan asli lagi), dan tidak pernah
//!   dirotasi. Cadangan bertanggal hanya bila isinya berbeda dari cadangan
//!   terbaru maupun yang asli, maksimal 10. Cadangan yang gagal dibuat
//!   menggagalkan penulisan.
//! - Encoding dipertahankan: UTF-8 yang sah dibaca sebagai UTF-8, selain itu
//!   ANSI (codepage sistem). Dulu selalu UTF-8: komentar ANSI ber-"é" jadi
//!   U+FFFD dan ditulis balik rusak, sementara penjaga baris tidak melihatnya
//!   karena membandingkan teks yang sama-sama rusak. Sama dengan C#, BOM
//!   UTF-8 dibaca tapi tidak ditulis ulang (Encoding.GetBytes tanpa preamble).
//! - Penanda rusak: Begin..End berpasangan berapa pun jumlahnya; Begin tanpa
//!   End = Begin + entri berbentuk Phoron sesudahnya; End tanpa Begin = End +
//!   entri berbentuk Phoron sebelumnya. Dulu End dicari dari ATAS berkas:
//!   entri yatim, blok baru ditambahkan tiap sinkron, berkas terus memanjang.
//!   Entri berbentuk Phoron = "127.0.0.1" atau "::1", SATU tab, satu nama.
//! - Ekor baris kosong dibuang supaya blok tidak makin turun tiap sinkron.
//! - Isi tidak berubah = berkas tidak disentuh sama sekali. Tanpa ini Phoron
//!   tanpa Administrator memperingatkan "hosts tidak bisa ditulis" di tiap
//!   Apply, untuk penulisan yang tidak dibutuhkan siapa pun.
//! - Tulis atomik lewat `atomic_file` (yang juga mengurus atribut ReadOnly).
//! - Salinan cadangan dari hosts read-only ikut ber-ReadOnly; atribut itu
//!   dilepas supaya pemangkasan tetap jalan (beda sengaja dari C#, yang
//!   gagal diam-diam menghapusnya; isi cadangan tetap byte demi byte).
//! - Memulihkan cadangan: keadaan sekarang dicadangkan dulu, tanpa penjaga
//!   baris (memulihkan memang boleh membuang baris), encoding ikut cadangan.
//! - HostsTool: nama situs dari SELURUH profil (berganti profil tidak boleh
//!   minta admin lagi), tanpa localhost, kosong bila Virtual Host mati, satu
//!   profil yang gagal tidak membatalkan sisanya.

use crate::atomic_file;
use crate::model::Site;
use crate::paths;
use crate::protokol::Pesan;
use std::collections::HashSet;
use std::io;
use std::path::{Path, PathBuf};
use std::time::SystemTime;

pub const BEGIN: &str = "# === Phoron mulai ===";
pub const END: &str = "# === Phoron selesai ===";

/// Cadangan pertama: keadaan hosts sebelum Phoron pernah menyentuhnya.
pub const NAMA_ASLI: &str = "hosts-asli.bak";

/// Sebanyak ini cadangan bertanggal disimpan; yang asli tidak ikut dihitung.
pub const CADANGAN_MAKS: usize = 10;

// ------------------------------------------------------------------ Encoding

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Encoding {
    Utf8,
    /// UTF-8 yang diawali BOM. Ditulis ulang TANPA BOM, sama seperti C#.
    Utf8Bom,
    /// Codepage ANSI sistem (`Encoding.Default` di .NET Framework).
    Ansi,
}

/// Isi berkas hosts yang sudah didekode.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct IsiHosts {
    pub baris: Vec<String>,
    pub encoding: Encoding,
}

const CP_ACP: u32 = 0;

// Fitur Win32_Globalization tidak ada di Cargo.toml; dua fungsi ini cukup
// dinyatakan sendiri (kernel32 selalu tertaut).
#[link(name = "kernel32")]
extern "system" {
    fn MultiByteToWideChar(cp: u32, flags: u32, src: *const u8, n_src: i32, dst: *mut u16, n_dst: i32) -> i32;
    fn WideCharToMultiByte(
        cp: u32,
        flags: u32,
        src: *const u16,
        n_src: i32,
        dst: *mut u8,
        n_dst: i32,
        default_char: *const u8,
        used_default: *mut i32,
    ) -> i32;
}

fn ansi_ke_teks(b: &[u8]) -> String {
    if b.is_empty() {
        return String::new();
    }
    unsafe {
        let n = MultiByteToWideChar(CP_ACP, 0, b.as_ptr(), b.len() as i32, std::ptr::null_mut(), 0);
        if n <= 0 {
            return String::from_utf8_lossy(b).into_owned();
        }
        let mut w = vec![0u16; n as usize];
        MultiByteToWideChar(CP_ACP, 0, b.as_ptr(), b.len() as i32, w.as_mut_ptr(), n);
        String::from_utf16_lossy(&w)
    }
}

fn teks_ke_ansi(s: &str) -> Vec<u8> {
    if s.is_empty() {
        return Vec::new();
    }
    let w: Vec<u16> = s.encode_utf16().collect();
    unsafe {
        let n = WideCharToMultiByte(
            CP_ACP, 0, w.as_ptr(), w.len() as i32, std::ptr::null_mut(), 0, std::ptr::null(), std::ptr::null_mut(),
        );
        if n <= 0 {
            return s.as_bytes().to_vec();
        }
        let mut b = vec![0u8; n as usize];
        WideCharToMultiByte(
            CP_ACP, 0, w.as_ptr(), w.len() as i32, b.as_mut_ptr(), n, std::ptr::null(), std::ptr::null_mut(),
        );
        b
    }
}

/// UTF-8 (dengan BOM bila ada) bila isinya UTF-8 yang sah, selain itu ANSI.
pub fn deteksi_encoding(b: &[u8]) -> Encoding {
    if b.starts_with(&[0xEF, 0xBB, 0xBF]) {
        Encoding::Utf8Bom
    } else if std::str::from_utf8(b).is_ok() {
        Encoding::Utf8
    } else {
        Encoding::Ansi
    }
}

/// `StringReader.ReadLine`: pemisah \r\n, \n, atau \r sendirian; baris
/// terakhir tanpa pemisah tetap dihitung, pemisah di ujung tidak menambah baris.
fn pecah_baris(t: &str) -> Vec<String> {
    let mut hasil = Vec::new();
    let mut sisa = t;
    while !sisa.is_empty() {
        match sisa.find(['\r', '\n']) {
            Some(i) => {
                hasil.push(sisa[..i].to_string());
                let lompat = if sisa[i..].starts_with("\r\n") { 2 } else { 1 };
                sisa = &sisa[i + lompat..];
            }
            None => {
                hasil.push(sisa.to_string());
                break;
            }
        }
    }
    hasil
}

/// Dekode isi berkas hosts dengan encoding yang benar-benar dipakainya.
pub fn urai(b: &[u8]) -> IsiHosts {
    let encoding = deteksi_encoding(b);
    let teks = match encoding {
        Encoding::Ansi => ansi_ke_teks(b),
        _ => String::from_utf8_lossy(b).into_owned(),
    };
    let teks = teks.strip_prefix('\u{FEFF}').unwrap_or(&teks);
    IsiHosts { baris: pecah_baris(teks), encoding }
}

/// Seperti `AtomicFile.WriteAllLines`: tiap baris diakhiri CRLF.
pub fn susun(baris: &[String], encoding: Encoding) -> Vec<u8> {
    let mut s = String::new();
    for l in baris {
        s.push_str(l);
        s.push_str("\r\n");
    }
    match encoding {
        Encoding::Ansi => teks_ke_ansi(&s),
        _ => s.into_bytes(),
    }
}

pub fn baca_baris(path: &Path) -> io::Result<IsiHosts> {
    Ok(urai(&std::fs::read(path)?))
}

/// Pembacaan yang wajib berhasil. Berkas yang tidak ada = kosong (UTF-8).
fn baca_wajib() -> io::Result<IsiHosts> {
    let f = paths::hosts_file();
    if !f.exists() {
        return Ok(IsiHosts { baris: Vec::new(), encoding: Encoding::Utf8 });
    }
    baca_baris(&f)
}

/// Pembacaan yang boleh gagal - HANYA untuk menampilkan. Larik kosongnya
/// tidak bisa dibedakan dari hosts yang memang kosong, jadi jangan pernah
/// dipakai sebagai dasar penulisan.
fn baca_longgar() -> Vec<String> {
    baca_baris(&paths::hosts_file()).map(|i| i.baris).unwrap_or_default()
}

// ------------------------------------------------------------------ Galat

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum GalatHosts {
    /// Penulisan ditolak sistem (Phoron jalan tanpa Administrator).
    TidakBerhak,
    /// Berkas hosts tidak terbaca, jadi sengaja tidak ditulis.
    TidakTerbaca(String),
    /// Penjaga: baris di luar blok Phoron akan ikut hilang.
    BarisHilang { jumlah: usize, contoh: String },
    CadanganGagal(String),
    CadanganTidakAda,
    Tulis(String),
}

impl GalatHosts {
    pub fn pesan(&self) -> Pesan {
        match self {
            GalatHosts::TidakBerhak => Pesan::baru(
                "Berkas hosts tidak bisa ditulis - jalankan Phoron sebagai Administrator atau matikan \"Sinkronkan berkas hosts Windows\" di Pengaturan.",
            ),
            GalatHosts::TidakTerbaca(e) => Pesan::dengan(
                "Berkas hosts tidak bisa dibaca, jadi sengaja tidak ditulis ulang: {0} Menulisnya dari bacaan yang gagal akan membuang seluruh baris milik Anda.",
                &[e],
            ),
            GalatHosts::BarisHilang { jumlah, contoh } => Pesan::dengan(
                "Penulisan berkas hosts dibatalkan: {0} baris di luar blok Phoron akan ikut hilang, mis. \"{1}\". Phoron hanya boleh mengubah blok miliknya sendiri.",
                &[&jumlah.to_string(), contoh],
            ),
            GalatHosts::CadanganGagal(e) => {
                Pesan::dengan("Cadangan berkas hosts gagal dibuat, jadi hosts tidak ditulis ulang: {0}", &[e])
            }
            GalatHosts::CadanganTidakAda => Pesan::baru("Berkas cadangan tidak ditemukan."),
            GalatHosts::Tulis(e) => Pesan::dengan("Gagal menyunting hosts: {0}", &[e]),
        }
    }
}

impl std::fmt::Display for GalatHosts {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        let p = self.pesan();
        let mut s = p.kunci.clone();
        for (i, a) in p.args.iter().enumerate() {
            s = s.replace(&format!("{{{i}}}"), a);
        }
        f.write_str(&s)
    }
}

impl std::error::Error for GalatHosts {}

fn galat_tulis(e: io::Error) -> GalatHosts {
    if e.kind() == io::ErrorKind::PermissionDenied {
        GalatHosts::TidakBerhak
    } else {
        GalatHosts::Tulis(e.to_string())
    }
}

// ------------------------------------------------------------------ Blok

/// Semua nama host di berkas hosts (blok siapa pun), dikecilkan. Pembacaan
/// longgar: dipakai untuk menampilkan dan memutuskan perlu tidaknya admin.
pub fn all_names() -> HashSet<String> {
    crate::situs::nama_hosts(&baca_longgar().join("\n"))
}

/// Entri persis berbentuk tulisan Phoron: "127.0.0.1" atau "::1", satu tab, satu nama.
fn entri_phoron(l: &str) -> bool {
    if l.contains(' ') {
        return false;
    }
    let mut bagian = l.split('\t');
    match (bagian.next(), bagian.next(), bagian.next()) {
        (Some(ip), Some(nama), None) => (ip == "127.0.0.1" || ip == "::1") && !nama.is_empty(),
        _ => false,
    }
}

/// Rentang baris (awal, akhir, inklusif) milik Phoron. Dipakai bersama oleh
/// sinkron dan penjaga baris, jadi keduanya selalu sepakat soal apa yang dibuang.
pub fn rentang_blok<S: AsRef<str>>(lines: &[S]) -> Vec<(usize, usize)> {
    let t = |k: usize| lines[k].as_ref().trim();
    let n = lines.len();
    let mut hasil = Vec::new();
    let (mut batas_bawah, mut i) = (0usize, 0usize);
    while i < n {
        if t(i) == BEGIN {
            let mut j = i + 1;
            while j < n && t(j) != END && t(j) != BEGIN {
                j += 1;
            }
            if j < n && t(j) == END {
                hasil.push((i, j));
                i = j + 1;
            } else {
                let mut k = i + 1;
                while k < n && entri_phoron(lines[k].as_ref()) {
                    k += 1;
                }
                hasil.push((i, k - 1));
                i = k;
            }
            batas_bawah = i;
        } else if t(i) == END {
            let mut k = i;
            while k > batas_bawah && entri_phoron(lines[k - 1].as_ref()) {
                k -= 1;
            }
            hasil.push((k, i));
            i += 1;
            batas_bawah = i;
        } else {
            i += 1;
        }
    }
    hasil
}

/// Baris bermakna di LUAR blok Phoron (sudah di-trim): milik orang lain.
fn di_luar_blok<S: AsRef<str>>(lines: &[S]) -> Vec<String> {
    let mut milik = vec![false; lines.len()];
    for (a, b) in rentang_blok(lines) {
        milik[a..=b].iter_mut().for_each(|m| *m = true);
    }
    lines
        .iter()
        .zip(milik)
        .filter(|(_, m)| !m)
        .map(|(l, _)| l.as_ref().trim().to_string())
        .filter(|t| !t.is_empty())
        .collect()
}

/// Penjaga menyeluruh: satu baris orang lain hilang berarti batalkan.
fn pastikan_masuk_akal(awal: &[String], baru: &[String]) -> Result<(), GalatHosts> {
    let sesudah: HashSet<String> = di_luar_blok(baru).into_iter().collect();
    let mut hilang: Vec<String> = Vec::new();
    for x in di_luar_blok(awal) {
        if !sesudah.contains(&x) && !hilang.contains(&x) {
            hilang.push(x);
        }
    }
    match hilang.first() {
        None => Ok(()),
        Some(c) => Err(GalatHosts::BarisHilang { jumlah: hilang.len(), contoh: c.clone() }),
    }
}

/// Huruf besar satu-ke-satu (OrdinalIgnoreCase .NET).
fn kunci_besar(s: &str) -> String {
    s.chars()
        .map(|c| {
            let mut u = c.to_uppercase();
            match (u.next(), u.next()) {
                (Some(h), None) => h,
                _ => c,
            }
        })
        .collect()
}

/// Nama dirapikan: kosong dibuang, di-trim, unik tanpa peka besar-kecil
/// (yang pertama menang), lalu diurutkan tanpa peka besar-kecil.
fn rapikan_nama<I, S>(nama: I) -> Vec<String>
where
    I: IntoIterator<Item = S>,
    S: AsRef<str>,
{
    let mut lihat = HashSet::new();
    let mut v: Vec<String> = Vec::new();
    for n in nama {
        let t = n.as_ref().trim();
        if t.is_empty() {
            continue;
        }
        if lihat.insert(kunci_besar(t)) {
            v.push(t.to_string());
        }
    }
    v.sort_by_cached_key(|n| kunci_besar(n));
    v
}

fn buang_ekor_kosong(v: &mut Vec<String>) {
    while v.last().is_some_and(|l| l.trim().is_empty()) {
        v.pop();
    }
}

/// Isi hosts baru: blok lama (dan sisa penanda rusak) dibuang, ekor kosong
/// dirapikan, lalu satu blok berisi persis `nama` ditambahkan bila ada.
pub fn susun_sinkron<I, S>(awal: &[String], nama: I) -> Vec<String>
where
    I: IntoIterator<Item = S>,
    S: AsRef<str>,
{
    let nama = rapikan_nama(nama);
    let mut lines = awal.to_vec();
    for (a, b) in rentang_blok(&lines).into_iter().rev() {
        lines.drain(a..=b);
    }
    buang_ekor_kosong(&mut lines);
    if !nama.is_empty() {
        lines.push(String::new());
        lines.push(BEGIN.into());
        for n in &nama {
            lines.push(format!("127.0.0.1\t{n}"));
            lines.push(format!("::1\t{n}"));
        }
        lines.push(END.into());
    }
    lines
}

/// Tulis ulang blok Phoron berisi persis daftar nama ini. `Ok(true)` bila
/// berkasnya ditulis, `Ok(false)` bila isinya sudah sama (tidak disentuh).
pub fn sync<I, S>(nama: I) -> Result<bool, GalatHosts>
where
    I: IntoIterator<Item = S>,
    S: AsRef<str>,
{
    let awal = baca_wajib().map_err(|e| GalatHosts::TidakTerbaca(e.to_string()))?;
    let baru = susun_sinkron(&awal.baris, nama);

    // Ekor kosong diabaikan: itu satu-satunya yang dirapikan, bukan alasan menulis.
    let mut lama = awal.baris.clone();
    buang_ekor_kosong(&mut lama);
    if lama == baru {
        return Ok(false);
    }

    pastikan_masuk_akal(&awal.baris, &baru)?;
    cadangkan(&awal.baris)?;
    atomic_file::write_all_bytes(&paths::hosts_file(), &susun(&baru, awal.encoding)).map_err(galat_tulis)?;
    Ok(true)
}

/// Kosongkan blok Phoron.
pub fn clear() -> Result<bool, GalatHosts> {
    sync(std::iter::empty::<&str>())
}

/// Padanan `Engine.SyncHosts`: blok berisi nama situs profil aktif, atau
/// dikosongkan bila Virtual Host mati - nama yang tertinggal tetap menunjuk
/// 127.0.0.1 dan dijawab vhost bawaan, jadi alamatnya tampak hidup padahal
/// menampilkan proyek yang salah.
pub fn sinkron_situs(auto_vhost: bool, situs: &[Site], peringatan: &mut Vec<Pesan>) {
    let nama: Vec<&str> = if auto_vhost {
        situs
            .iter()
            .map(|s| s.host_name.as_str())
            .filter(|n| !n.trim().is_empty() && *n != "localhost")
            .collect()
    } else {
        Vec::new()
    };
    if let Err(e) = sync(nama) {
        peringatan.push(e.pesan());
    }
}

// ------------------------------------------------------------------ Cadangan

/// Folder cadangan, `<data>\hosts-backup`.
pub fn folder_cadangan() -> PathBuf {
    let p = paths::data().join("hosts-backup");
    let _ = std::fs::create_dir_all(&p);
    p
}

#[derive(Debug, Clone)]
pub struct Cadangan {
    pub path: PathBuf,
    pub waktu: SystemTime,
    pub ukuran: u64,
    /// Keadaan sebelum Phoron dipasang; tidak pernah dibuang.
    pub asli: bool,
}

impl Cadangan {
    pub fn nama(&self) -> String {
        self.path.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default()
    }
}

/// Cadangan yang ada (`hosts-*.bak`), terbaru lebih dulu.
pub fn daftar_cadangan() -> Vec<Cadangan> {
    let mut hasil = Vec::new();
    if let Ok(rd) = std::fs::read_dir(folder_cadangan()) {
        for e in rd.flatten() {
            let nama = e.file_name().to_string_lossy().to_lowercase();
            if !(nama.starts_with("hosts-") && nama.ends_with(".bak")) {
                continue;
            }
            let Ok(m) = e.metadata() else { continue };
            if !m.is_file() {
                continue;
            }
            hasil.push(Cadangan {
                path: e.path(),
                waktu: m.modified().unwrap_or(SystemTime::UNIX_EPOCH),
                ukuran: m.len(),
                asli: nama == NAMA_ASLI,
            });
        }
    }
    hasil.sort_by_key(|c| std::cmp::Reverse(c.waktu));
    hasil
}

fn sama_isi(path: &Path, lines: &[String]) -> bool {
    baca_baris(path).map(|i| i.baris == lines).unwrap_or(false)
}

/// Salin hosts ke cadangan byte demi byte. Atribut ReadOnly yang ikut tersalin
/// dari hosts dilepas: cadangan milik Phoron, dan cadangan read-only tidak
/// bisa dipangkas (di C# `File.Delete`-nya gagal diam-diam, cadangan menumpuk).
fn salin_cadangan(hosts: &Path, tujuan: &Path) -> io::Result<()> {
    std::fs::copy(hosts, tujuan)?;
    let mut izin = std::fs::metadata(tujuan)?.permissions();
    if izin.readonly() {
        #[allow(clippy::permissions_set_readonly_false)]
        izin.set_readonly(false);
        std::fs::set_permissions(tujuan, izin)?;
    }
    Ok(())
}

fn cadangkan(awal: &[String]) -> Result<(), GalatHosts> {
    let jalan = || -> io::Result<()> {
        let folder = folder_cadangan();
        let asli = folder.join(NAMA_ASLI);
        let hosts = paths::hosts_file();
        if !asli.exists() {
            // Sekali seumur mesin ini, byte demi byte dari berkas aslinya.
            if hosts.exists() {
                salin_cadangan(&hosts, &asli)?;
            } else {
                atomic_file::write_all_bytes(&asli, &susun(awal, Encoding::Utf8))?;
            }
            return Ok(());
        }
        // Apply jalan tiap start dan tiap ganti profil: tanpa syarat ini
        // hasilnya belasan berkas kembar.
        if let Some(t) = daftar_cadangan().first() {
            if sama_isi(&t.path, awal) {
                return Ok(());
            }
        }
        if sama_isi(&asli, awal) {
            return Ok(());
        }
        let tujuan = folder.join(format!("hosts-{}.bak", crate::waktu::cap_lokal_berkas()));
        if hosts.exists() {
            salin_cadangan(&hosts, &tujuan)?;
        } else {
            atomic_file::write_all_bytes(&tujuan, &susun(awal, Encoding::Utf8))?;
        }
        pangkas();
        Ok(())
    };
    // Menulis berkas milik sistem tanpa cadangan adalah persis keadaan yang
    // seluruh modul ini ada untuk mencegahnya.
    jalan().map_err(|e| GalatHosts::CadanganGagal(e.to_string()))
}

fn pangkas() {
    for c in daftar_cadangan().into_iter().filter(|c| !c.asli).skip(CADANGAN_MAKS) {
        let _ = std::fs::remove_file(&c.path);
    }
}

/// Kembalikan hosts ke salah satu cadangan. Keadaan sekarang ikut dicadangkan
/// lebih dulu - memulihkan juga sebuah perubahan, dan orang harus bisa
/// membatalkannya.
pub fn pulihkan(cadangan: &Path) -> Result<(), GalatHosts> {
    if cadangan.as_os_str().is_empty() || !cadangan.is_file() {
        return Err(GalatHosts::CadanganTidakAda);
    }
    let isi = baca_baris(cadangan).map_err(|e| GalatHosts::Tulis(e.to_string()))?;
    if let Ok(sekarang) = baca_wajib() {
        if !sekarang.baris.is_empty() {
            cadangkan(&sekarang.baris)?;
        }
    }
    atomic_file::write_all_bytes(&paths::hosts_file(), &susun(&isi.baris, isi.encoding)).map_err(galat_tulis)
}

// ------------------------------------------------------------------ HostsTool

/// Kode keluar pembantu `--sinkron-hosts`, dibaca pemanggilnya.
pub const KELUAR_BERHASIL: i32 = 0;
pub const KELUAR_TIDAK_BERHAK: i32 = 2;
pub const KELUAR_GALAT: i32 = 3;

/// Nama host semua situs di SEMUA profil, tanpa localhost; kosong bila
/// Virtual Host mati (tidak ada yang akan melayani nama itu).
pub fn semua_nama_situs() -> Vec<String> {
    if !crate::settings::Settings::load().auto_vhost {
        return Vec::new();
    }
    let ada = all_names();
    let mut nama = Vec::new();
    for profil in crate::profile_store::load_all(None) {
        let mut abai = Vec::new();
        for s in crate::situs::scan(Some(&profil), &ada, &mut abai) {
            let h = s.host_name.trim();
            if !h.is_empty() && !h.eq_ignore_ascii_case("localhost") {
                nama.push(h.to_string());
            }
        }
    }
    rapikan_nama(nama)
}

/// Nama yang belum ada di hosts - dasar keputusan perlu tidaknya minta admin.
pub fn belum_terdaftar() -> Vec<String> {
    let ada = all_names();
    semua_nama_situs().into_iter().filter(|n| !ada.contains(&n.to_lowercase())).collect()
}

/// Tulis blok Phoron berisi seluruh nama situs. Harus dari proses ber-Administrator.
pub fn daftarkan_semua() -> Result<Vec<String>, GalatHosts> {
    let nama = semua_nama_situs();
    sync(&nama)?;
    Ok(nama)
}

/// Isi pembantu `--sinkron-hosts`: dijalankan ber-Administrator lewat runas,
/// menulis hosts, lalu keluar dengan kode ini. Entri hosts menetap, jadi
/// sesudahnya Phoron boleh jalan sebagai pengguna biasa (autostart lewat
/// kunci Run selalu tanpa Administrator).
pub fn sinkron_hosts_saja() -> i32 {
    if !crate::tulis::is_admin() {
        return KELUAR_TIDAK_BERHAK;
    }
    match daftarkan_semua() {
        Ok(_) => KELUAR_BERHASIL,
        Err(GalatHosts::TidakBerhak) => KELUAR_TIDAK_BERHAK,
        Err(_) => KELUAR_GALAT,
    }
}

#[cfg(test)]
mod uji {
    use super::*;
    use std::fs;

    fn s(v: &[&str]) -> Vec<String> {
        v.iter().map(|x| x.to_string()).collect()
    }

    /// Sandbox + berkas hosts sementara. JANGAN PERNAH hosts sistem.
    fn siapkan(nama: &str) -> (paths::FolderUji, PathBuf, PathBuf) {
        let (g, d) = paths::sandbox_uji(nama);
        let h = d.join("hosts");
        paths::set_hosts_file(Some(h.clone()));
        (g, d, h)
    }

    fn selesai(d: &Path) {
        paths::set_hosts_file(None);
        let _ = fs::remove_dir_all(d);
    }

    fn baris_berkas(h: &Path) -> Vec<String> {
        baca_baris(h).unwrap().baris
    }

    const PENGGUNA: [&str; 4] =
        ["127.0.0.1 localhost", "# catatan lama", "10.0.0.1 kantor.internal", "0.0.0.0 iklan.contoh"];

    #[test]
    fn blok_ditulis_baris_pengguna_selamat_dan_dicadangkan() {
        let (_g, d, h) = siapkan("hosts-dasar");
        fs::write(&h, PENGGUNA.join("\r\n") + "\r\n").unwrap();
        assert!(sync(["toko.test"]).unwrap());
        let isi = baris_berkas(&h);
        assert!(PENGGUNA.iter().all(|p| isi.contains(&p.to_string())));
        assert!(isi.contains(&"127.0.0.1\ttoko.test".to_string()) && isi.contains(&"::1\ttoko.test".to_string()));

        let asli = folder_cadangan().join(NAMA_ASLI);
        assert_eq!(baris_berkas(&asli), s(&PENGGUNA));

        // Isi berubah: tercadang. Isi sama: tidak disentuh, tidak menumpuk.
        let n = daftar_cadangan().len();
        assert!(sync(["toko.test", "kedai.test"]).unwrap());
        assert_eq!(daftar_cadangan().len(), n + 1);
        let n = daftar_cadangan().len();
        assert!(!sync(["kedai.test", " toko.test ", "TOKO.test"]).unwrap());
        assert_eq!(daftar_cadangan().len(), n);
        selesai(&d);
    }

    #[test]
    fn gagal_membaca_berarti_gagal_menulis() {
        use std::os::windows::fs::OpenOptionsExt;
        let (_g, d, h) = siapkan("hosts-kunci");
        fs::write(&h, PENGGUNA.join("\r\n")).unwrap();
        let sebelum = fs::read(&h).unwrap();
        {
            let _kunci = fs::OpenOptions::new().read(true).write(true).share_mode(0).open(&h).unwrap();
            assert!(matches!(sync(["baru.test"]), Err(GalatHosts::TidakTerbaca(_))));
        }
        assert_eq!(fs::read(&h).unwrap(), sebelum);
        selesai(&d);
    }

    #[test]
    fn ansi_dipertahankan_dan_asli_byte_demi_byte() {
        let (_g, d, h) = siapkan("hosts-ansi");
        let asli = teks_ke_ansi("# komentar café\r\n127.0.0.1 localhost\r\n");
        fs::write(&h, &asli).unwrap();
        // Codepage UTF-8 sistem-lebar membuat "ANSI" sama dengan UTF-8.
        let ansi_sungguhan = deteksi_encoding(&asli) == Encoding::Ansi;
        sync(["toko.test"]).unwrap();
        let sesudah = fs::read(&h).unwrap();
        let teks = ansi_ke_teks(&sesudah);
        assert!(teks.contains("# komentar café"), "{teks}");
        assert!(!teks.contains('\u{FFFD}'));
        if ansi_sungguhan {
            assert!(std::str::from_utf8(&sesudah).is_err(), "ditulis ulang sebagai UTF-8");
        }
        assert_eq!(fs::read(folder_cadangan().join(NAMA_ASLI)).unwrap(), asli);
        selesai(&d);
    }

    #[test]
    fn hosts_readonly_ditulis_dan_cadangannya_bisa_dipangkas() {
        let (_g, d, h) = siapkan("hosts-ro");
        fs::write(&h, "127.0.0.1 localhost\r\n").unwrap();
        let mut izin = fs::metadata(&h).unwrap().permissions();
        izin.set_readonly(true);
        fs::set_permissions(&h, izin).unwrap();
        sync(["a.test"]).unwrap();
        sync(["b.test"]).unwrap();
        assert!(fs::metadata(&h).unwrap().permissions().readonly());
        assert!(baris_berkas(&h).contains(&"::1\tb.test".to_string()));
        let c = daftar_cadangan();
        assert_eq!(c.len(), 2);
        assert!(c.iter().all(|c| !fs::metadata(&c.path).unwrap().permissions().readonly()));
        let mut izin = fs::metadata(&h).unwrap().permissions();
        #[allow(clippy::permissions_set_readonly_false)]
        izin.set_readonly(false);
        fs::set_permissions(&h, izin).unwrap();
        selesai(&d);
    }

    #[test]
    fn bom_dibaca_tapi_tidak_ditulis_ulang() {
        let (_g, d, h) = siapkan("hosts-bom");
        fs::write(&h, b"\xEF\xBB\xBF127.0.0.1 localhost\n").unwrap();
        assert_eq!(baris_berkas(&h), s(&["127.0.0.1 localhost"]));
        sync(["a.test"]).unwrap();
        let b = fs::read(&h).unwrap();
        assert!(b.starts_with(b"127.0.0.1 localhost\r\n\r\n# === Phoron mulai ==="));
        selesai(&d);
    }

    #[test]
    fn penanda_rusak_dibersihkan_tanpa_memanjang() {
        let (_g, d, h) = siapkan("hosts-blok");
        // Begin dihapus tangan.
        fs::write(&h, s(&["127.0.0.1 localhost", "10.0.0.1 kantor.internal", "", "127.0.0.1\tlama.test", "::1\tlama.test", END]).join("\r\n")).unwrap();
        sync(["baru.test"]).unwrap();
        let isi = baris_berkas(&h);
        assert!(!isi.iter().any(|l| l.contains("lama.test")), "{isi:?}");
        assert_eq!(isi.iter().filter(|l| l.trim() == BEGIN).count(), 1);
        assert_eq!(isi.iter().filter(|l| l.trim() == END).count(), 1);
        let sekali = fs::read(&h).unwrap();
        assert!(!sync(["baru.test"]).unwrap());
        assert_eq!(fs::read(&h).unwrap(), sekali);

        // End dihapus tangan: baris pengguna SESUDAH entri selamat.
        fs::write(&h, s(&["127.0.0.1 localhost", BEGIN, "127.0.0.1\tlama.test", "::1\tlama.test", "# catatan pengguna", "10.0.0.2 printer.kantor"]).join("\r\n")).unwrap();
        sync(["baru.test"]).unwrap();
        let isi = baris_berkas(&h);
        assert!(!isi.iter().any(|l| l.contains("lama.test")));
        assert!(isi.contains(&"# catatan pengguna".into()) && isi.contains(&"10.0.0.2 printer.kantor".into()));

        // Dua blok dilebur jadi satu.
        fs::write(&h, s(&["127.0.0.1 localhost", BEGIN, "127.0.0.1\ta.test", END, "10.0.0.3 nas.rumah", BEGIN, "127.0.0.1\tb.test", END]).join("\r\n")).unwrap();
        sync(["c.test"]).unwrap();
        let isi = baris_berkas(&h);
        assert_eq!(isi.iter().filter(|l| l.trim() == BEGIN).count(), 1);
        assert!(isi.contains(&"10.0.0.3 nas.rumah".into()));
        assert!(!isi.iter().any(|l| l.contains("a.test") || l.contains("b.test")));
        selesai(&d);
    }

    #[test]
    fn rentang_blok_dan_entri_phoron() {
        let r = rentang_blok(&[BEGIN, "127.0.0.1\tx.test", "127.0.0.1 milikku.test"]);
        assert_eq!(r, vec![(0, 1)]);
        assert!(rentang_blok(&["127.0.0.1\tlocalhost", "# x"]).is_empty());
        // End yatim tidak boleh menelan entri di atas blok sebelumnya.
        let r = rentang_blok(&[BEGIN, "::1\ta.test", END, "127.0.0.1\tb.test", END]);
        assert_eq!(r, vec![(0, 2), (3, 4)]);
        assert!(!entri_phoron("127.0.0.1\ta\tb") && !entri_phoron("127.0.0.1\t") && !entri_phoron("10.0.0.1\ta"));
    }

    #[test]
    fn penjaga_menolak_kehilangan_baris() {
        let awal = s(&["10.0.0.1 kantor", "# x"]);
        let baru = s(&["# x"]);
        let e = pastikan_masuk_akal(&awal, &baru).unwrap_err();
        assert_eq!(e, GalatHosts::BarisHilang { jumlah: 1, contoh: "10.0.0.1 kantor".into() });
        assert!(pastikan_masuk_akal(&awal, &s(&["10.0.0.1 kantor", "# x", BEGIN, END])).is_ok());
    }

    #[test]
    fn hosts_kosong_dan_tidak_ada() {
        let (_g, d, h) = siapkan("hosts-kosong");
        // Tidak ada + tanpa nama: tidak menulis apa pun.
        assert!(!sync(std::iter::empty::<&str>()).unwrap());
        assert!(!h.exists());
        assert!(sync(["a.test"]).unwrap());
        assert_eq!(fs::read(&h).unwrap(), b"\r\n# === Phoron mulai ===\r\n127.0.0.1\ta.test\r\n::1\ta.test\r\n# === Phoron selesai ===\r\n");
        assert_eq!(fs::read(folder_cadangan().join(NAMA_ASLI)).unwrap(), b"");
        assert!(clear().unwrap());
        assert_eq!(fs::read(&h).unwrap(), b"");
        selesai(&d);
    }

    #[test]
    fn cadangan_dipangkas_dan_dipulihkan() {
        let (_g, d, h) = siapkan("hosts-pangkas");
        fs::write(&h, PENGGUNA.join("\r\n")).unwrap();
        sync(["toko.test"]).unwrap();
        for i in 0..15 {
            fs::write(folder_cadangan().join(format!("hosts-lama{i}.bak")), format!("cadangan lama {i}\r\n")).unwrap();
        }
        sync(["lain.test"]).unwrap();
        assert_eq!(daftar_cadangan().iter().filter(|c| !c.asli).count(), CADANGAN_MAKS);
        let asli = folder_cadangan().join(NAMA_ASLI);
        assert!(asli.exists());

        let sekarang = baris_berkas(&h);
        pulihkan(&asli).unwrap();
        assert_eq!(baris_berkas(&h), s(&PENGGUNA));
        assert!(daftar_cadangan().iter().any(|c| !c.asli && baris_berkas(&c.path) == sekarang));
        assert_eq!(pulihkan(&d.join("tidak-ada.bak")), Err(GalatHosts::CadanganTidakAda));
        selesai(&d);
    }

    #[test]
    fn situs_vhost_mati_mengosongkan_blok() {
        let (_g, d, h) = siapkan("hosts-situs");
        fs::write(&h, s(&["127.0.0.1 localhost", "", BEGIN, "127.0.0.1\tlama.test", "::1\tlama.test", END]).join("\r\n")).unwrap();
        let situs = vec![
            Site { host_name: "blog.test".into(), ..Site::default() },
            Site { host_name: "localhost".into(), ..Site::default() },
        ];
        let mut w = Vec::new();
        sinkron_situs(true, &situs, &mut w);
        assert!(w.is_empty());
        assert_eq!(baris_berkas(&h), s(&["127.0.0.1 localhost", "", BEGIN, "127.0.0.1\tblog.test", "::1\tblog.test", END]));
        sinkron_situs(false, &situs, &mut w);
        assert_eq!(baris_berkas(&h), s(&["127.0.0.1 localhost"]));
        selesai(&d);
    }

    #[test]
    fn semua_nama_dari_seluruh_profil() {
        let (_g, d, h) = siapkan("hosts-tool");
        fs::write(&h, "127.0.0.1 localhost\r\n127.0.0.1 Blog.test\r\n").unwrap();
        // Virtual Host mati: tidak ada yang perlu didaftarkan.
        assert!(semua_nama_situs().is_empty());

        let mut st = crate::settings::Settings::load();
        st.auto_vhost = true;
        st.save().unwrap();
        for f in [r"www\blog", r"www\toko", r"lain\api"] {
            fs::create_dir_all(d.join(f)).unwrap();
        }
        let mut a = crate::model::Profile { name: "A".into(), ..Default::default() };
        crate::profile_store::save(&mut a).unwrap();
        let mut b = crate::model::Profile {
            name: "B".into(),
            project_roots: vec![d.join("lain").to_string_lossy().into(), d.join("www").to_string_lossy().into()],
            site_suffix: "local".into(),
            ..Default::default()
        };
        crate::profile_store::save(&mut b).unwrap();
        assert_eq!(semua_nama_situs(), s(&["api.local", "blog.local", "blog.test", "toko.local", "toko.test"]));
        assert_eq!(belum_terdaftar(), s(&["api.local", "blog.local", "toko.local", "toko.test"]));
        assert_eq!(daftarkan_semua().unwrap().len(), 5);
        assert!(belum_terdaftar().is_empty());
        selesai(&d);
    }

    #[test]
    fn pesan_galat_berkunci_teks_indonesia() {
        assert_eq!(GalatHosts::CadanganTidakAda.pesan().kunci, "Berkas cadangan tidak ditemukan.");
        let t = GalatHosts::BarisHilang { jumlah: 2, contoh: "x".into() }.to_string();
        assert!(t.starts_with("Penulisan berkas hosts dibatalkan: 2 baris") && t.contains("\"x\""));
    }

    #[test]
    fn pecah_baris_seperti_stringreader() {
        assert_eq!(pecah_baris("a\r\nb\rc\nd"), s(&["a", "b", "c", "d"]));
        assert_eq!(pecah_baris("a\r\n\r\n"), s(&["a", ""]));
        assert!(pecah_baris("").is_empty());
    }
}
