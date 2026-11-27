//! Fitur: HTTPS untuk situs lokal (SslTool.cs) - CA lokal dan sertifikat situs
//! lewat openssl.exe milik paket Apache, serta pemasangan CA ke Trusted Root.
//!
//! Daftar periksa (tiap butir kasus nyata di versi C#, dan punya uji):
//! - DUA sertifikat: CA lokal (phoron-ca.crt) yang dipercayai Windows, dan
//!   sertifikat situs (phoron.crt, CA:FALSE) yang ditandatangani CA itu. Dulu
//!   satu sertifikat self-signed ber-CA:TRUE: Firefox menolaknya selamanya
//!   (MOZILLA_PKIX_ERROR_CA_CERT_USED_AS_END_ENTITY).
//! - [`exists`] mensyaratkan CA juga: instalasi lama yang hanya punya
//!   phoron.crt model lama dibuatkan pasangan baru.
//! - CA yang sudah ada DIPAKAI ULANG, jadi kepercayaan yang sudah dipasang
//!   tetap berlaku; hanya sertifikat situs yang dibuat ulang.
//! - CA punya nameConstraints (kritis): hanya akhiran situs aktif, akhiran
//!   profil lain, "test", localhost, 127.0.0.1, ::1. pathlen:0 saja tidak
//!   membatasi nama - tanpa ini kunci CA di cakram cukup untuk memalsukan
//!   google.com yang diterima semua browser di mesin ini.
//! - CA tanpa batasan (buatan sebelum 1.39) atau yang tidak mencakup akhiran
//!   ini dibuang BESERTA kuncinya, lalu dibuat baru. Kecocokan per nama utuh:
//!   "test" tidak cocok dengan "testing".
//! - SAN wajib (*.akhiran, akhiran, localhost, 127.0.0.1, ::1): tanpa SAN
//!   browser modern menolak sertifikatnya.
//! - Ekstensi lewat berkas konfigurasi, bukan -addext (baru ada di OpenSSL
//!   1.1.1; paket Apache lama membawa 1.0.2).
//! - OPENSSL_CONF disetel ke berkas cnf kita: tanpa itu openssl bawaan Apache
//!   mencari config di jalur build-nya dan gagal membingungkan.
//! - Nomor seri acak, bukan -CAcreateserial: seri yang berulang sesudah CA
//!   dibuat ulang ditolak Firefox (SEC_ERROR_REUSED_ISSUER_AND_SERIAL).
//! - crt/key ditulis ke nama ".baru" lalu dipindah: Apache yang sedang jalan
//!   tidak boleh melihat pasangan crt/key yang tidak cocok. Tidak ada sisa.
//! - Sertifikat baru menandai config web berubah (perlu restart).
//! - "Sudah dipercaya" dibandingkan lewat sidik jari, bukan nama subjek, di
//!   gudang Root pengguna DAN mesin; jawabannya disinggahkan 20 detik dan
//!   dilupakan begitu CA dibuat atau dipasang.
//! - Pemasangan (certutil -addstore Root) butuh Administrator; sesudahnya akar
//!   Phoron lama (self-signed, O=Phoron Local Development, sidik jari lain)
//!   dibuang, gagal diam-diam.

use crate::model::BinPackage;
use crate::paths;
use crate::protokol::Pesan;
use crate::shell;
use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use std::time::{Duration, Instant};

pub fn crt_path() -> PathBuf {
    paths::etc_ssl().join("phoron.crt")
}

pub fn key_path() -> PathBuf {
    paths::etc_ssl().join("phoron.key")
}

pub fn ca_crt_path() -> PathBuf {
    paths::etc_ssl().join("phoron-ca.crt")
}

pub fn ca_key_path() -> PathBuf {
    paths::etc_ssl().join("phoron-ca.key")
}

/// CA ikut disyaratkan: instalasi lama hanya punya phoron.crt model lama, dan
/// dengan begini pasangan baru dibuat pada penerapan berikutnya alih-alih terus
/// menyajikan sertifikat yang ditolak Firefox.
pub fn exists() -> bool {
    crt_path().is_file() && key_path().is_file() && ca_crt_path().is_file()
}

const ORGANISASI: &str = "Phoron Local Development";

/// openssl.exe ikut dalam paket Apache; tidak perlu unduhan terpisah.
pub fn find_openssl(apache: Option<&BinPackage>) -> Option<PathBuf> {
    let p = Path::new(&apache?.path).join("bin").join("openssl.exe");
    p.is_file().then_some(p)
}

/// Akhiran dirapikan: tanpa spasi, tanpa titik di tepi, huruf kecil; kosong = "test".
fn akhiran(s: &str) -> String {
    let t = s.trim();
    if t.is_empty() {
        "test".into()
    } else {
        t.trim_matches('.').to_lowercase()
    }
}

/// Nilai nameConstraints untuk CA baru. Akhiran profil lain ikut supaya
/// berganti profil tidak memaksa CA dibuat dan dipercayai ulang. "DNS:test"
/// mencakup test sendiri dan semua nama di bawahnya (RFC 5280 4.2.1.10).
pub fn batasan_nama<S: AsRef<str>>(suffix: &str, akhiran_lain: &[S]) -> String {
    let mut semua = vec![akhiran(suffix), "test".into(), "localhost".into()];
    semua.extend(akhiran_lain.iter().map(|a| a.as_ref()).filter(|a| !a.trim().is_empty()).map(akhiran));
    let mut unik: Vec<String> = Vec::new();
    for a in semua {
        if !unik.iter().any(|u| crate::teks::sama_abaikan_besar(u, &a)) {
            unik.push(a);
        }
    }
    let mut s = String::from("critical");
    for a in &unik {
        s.push_str(", permitted;DNS:");
        s.push_str(a);
    }
    s.push_str(", permitted;IP:127.0.0.1/255.255.255.255");
    s.push_str(", permitted;IP:0:0:0:0:0:0:0:1/FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF");
    s
}

fn baris(v: &[&str]) -> String {
    let mut s = String::new();
    for l in v {
        s.push_str(l);
        s.push_str("\r\n");
    }
    s
}

/// Berkas konfigurasi openssl untuk CA.
pub fn isi_cnf_ca<S: AsRef<str>>(suffix: &str, akhiran_lain: &[S]) -> String {
    let o = format!("O = {ORGANISASI}");
    let nc = format!("nameConstraints = {}", batasan_nama(suffix, akhiran_lain));
    baris(&[
        "[req]",
        "default_bits = 2048",
        "prompt = no",
        "default_md = sha256",
        "distinguished_name = dn",
        "x509_extensions = ca",
        "",
        "[dn]",
        "C = ID",
        &o,
        "CN = Phoron Local CA",
        "",
        "[ca]",
        // pathlen:0 - hanya boleh menandatangani sertifikat situs, bukan CA lain.
        "basicConstraints = critical, CA:TRUE, pathlen:0",
        "keyUsage = critical, keyCertSign, cRLSign",
        "subjectKeyIdentifier = hash",
        &nc,
    ])
}

/// Berkas konfigurasi openssl untuk sertifikat situs (CSR + ekstensi "leaf").
pub fn isi_cnf_situs(suffix: &str) -> String {
    let a = akhiran(suffix);
    let o = format!("O = {ORGANISASI}");
    let cn = format!("CN = *.{a}");
    let d1 = format!("DNS.1 = *.{a}");
    let d2 = format!("DNS.2 = {a}");
    baris(&[
        "[req]",
        "default_bits = 2048",
        "prompt = no",
        "default_md = sha256",
        "distinguished_name = dn",
        "",
        "[dn]",
        "C = ID",
        &o,
        &cn,
        "",
        "[leaf]",
        "basicConstraints = critical, CA:FALSE",
        "keyUsage = critical, digitalSignature, keyEncipherment",
        "extendedKeyUsage = serverAuth",
        "subjectKeyIdentifier = hash",
        "authorityKeyIdentifier = keyid",
        "subjectAltName = @alt",
        "",
        "[alt]",
        &d1,
        &d2,
        "DNS.3 = localhost",
        "IP.1 = 127.0.0.1",
        "IP.2 = ::1",
    ])
}

fn jalankan_openssl(openssl: &Path, cnf: &Path, args: &str) -> shell::RunResult {
    let mut env = HashMap::new();
    env.insert("OPENSSL_CONF".to_string(), cnf.to_string_lossy().into_owned());
    let kerja = openssl.parent().map(Path::to_path_buf);
    shell::run(&openssl.to_string_lossy(), args, kerja.as_deref(), 120_000, Some(&env), None)
        .unwrap_or_else(|e| shell::RunResult { exit_code: -1, stderr: e.to_string(), ..Default::default() })
}

fn tambah(p: &Path, akhir: &str) -> PathBuf {
    let mut s = p.as_os_str().to_owned();
    s.push(akhir);
    PathBuf::from(s)
}

/// 16 digit heksadesimal acak untuk nomor seri.
fn seri_acak() -> String {
    use windows_sys::Win32::Security::Cryptography::{BCryptGenRandom, BCRYPT_USE_SYSTEM_PREFERRED_RNG};
    let mut b = [0u8; 8];
    let st = unsafe { BCryptGenRandom(std::ptr::null_mut(), b.as_mut_ptr(), 8, BCRYPT_USE_SYSTEM_PREFERRED_RNG) };
    if st != 0 {
        // Sangat tidak mungkin; waktu nano tetap jauh dari seri berulang.
        let n = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap_or_default().as_nanos();
        b = (n as u64).to_le_bytes();
    }
    b.iter().map(|x| format!("{x:02x}")).collect()
}

fn ganti_berkas(sumber: &Path, tujuan: &Path) -> std::io::Result<()> {
    if tujuan.exists() {
        std::fs::remove_file(tujuan)?;
    }
    std::fs::rename(sumber, tujuan)
}

/// Buat sertifikat wildcard untuk akhiran situs (mis. *.test) plus localhost,
/// ditandatangani CA lokal. CA yang sudah ada (dan mencakup akhiran ini)
/// dipakai ulang.
pub fn generate<S: AsRef<str>>(apache: Option<&BinPackage>, suffix: &str, akhiran_lain: &[S]) -> Result<(), Pesan> {
    let Some(openssl) = find_openssl(apache) else {
        return Err(Pesan::baru("openssl.exe tidak ditemukan di paket Apache yang dipilih."));
    };
    let suffix = akhiran(suffix);
    let ssl = paths::etc_ssl();
    let _ = std::fs::create_dir_all(&ssl);
    let (ca_crt, ca_key, crt, key) = (ca_crt_path(), ca_key_path(), crt_path(), key_path());

    // Kunci CA lama yang tidak dibatasi itulah yang berbahaya: siapa pun yang
    // bisa membacanya bisa menerbitkan sertifikat apa saja yang dipercaya
    // seluruh mesin. Tanpa kunci, akar lamanya di Trusted Root tidak bisa
    // menandatangani apa pun lagi, dan trust() berikutnya membuangnya.
    if ca_crt.exists() && !ca_mencakup(&suffix) {
        let _ = std::fs::remove_file(&ca_key);
        let _ = std::fs::remove_file(&ca_crt);
    }
    let tmp = paths::tmp();

    if !ca_crt.exists() || !ca_key.exists() {
        let ca_cnf = tmp.join("phoron-ca.cnf");
        if let Err(e) = std::fs::write(&ca_cnf, isi_cnf_ca(&suffix, akhiran_lain)) {
            return Err(Pesan::dengan("openssl gagal membuat CA: {0}", &[&e.to_string()]));
        }
        let res = jalankan_openssl(
            &openssl,
            &ca_cnf,
            &format!(
                "req -x509 -nodes -days 3650 -newkey rsa:2048 -keyout \"{}\" -out \"{}\" -config \"{}\"",
                ca_key.display(),
                ca_crt.display(),
                ca_cnf.display()
            ),
        );
        // CA baru: jawaban "sudah dipercaya" yang tersimpan menyangkut CA lain.
        lupakan_kepercayaan();
        if !ca_crt.exists() || !ca_key.exists() {
            return Err(Pesan::dengan("openssl gagal membuat CA: {0}", &[&res.all()]));
        }
    }

    let cnf = tmp.join("phoron-ssl.cnf");
    let csr = tmp.join("phoron.csr");
    if let Err(e) = std::fs::write(&cnf, isi_cnf_situs(&suffix)) {
        return Err(Pesan::dengan("openssl gagal: {0}", &[&e.to_string()]));
    }

    let crt_baru = tambah(&crt, ".baru");
    let key_baru = tambah(&key, ".baru");
    for f in [&crt_baru, &key_baru, &csr] {
        let _ = std::fs::remove_file(f);
    }

    let res = jalankan_openssl(
        &openssl,
        &cnf,
        &format!(
            "req -new -nodes -newkey rsa:2048 -keyout \"{}\" -out \"{}\" -config \"{}\"",
            key_baru.display(),
            csr.display(),
            cnf.display()
        ),
    );
    if !key_baru.exists() || !csr.exists() {
        return Err(Pesan::dengan("openssl gagal: {0}", &[&res.all()]));
    }

    let res = jalankan_openssl(
        &openssl,
        &cnf,
        &format!(
            "x509 -req -sha256 -days 3650 -in \"{}\" -CA \"{}\" -CAkey \"{}\" -set_serial 0x{} -extfile \"{}\" -extensions leaf -out \"{}\"",
            csr.display(),
            ca_crt.display(),
            ca_key.display(),
            seri_acak(),
            cnf.display(),
            crt_baru.display()
        ),
    );
    let _ = std::fs::remove_file(&csr);
    if !crt_baru.exists() {
        let _ = std::fs::remove_file(&key_baru);
        return Err(Pesan::dengan("openssl gagal: {0}", &[&res.all()]));
    }

    if let Err(e) = ganti_berkas(&key_baru, &key).and_then(|_| ganti_berkas(&crt_baru, &crt)) {
        return Err(Pesan::dengan("Sertifikat tidak bisa disimpan: {0}", &[&e.to_string()]));
    }
    crate::tulis::tandai_web_berubah();
    Ok(())
}

/// Hasil [`pastikan`], padanan `Engine.PastikanSertifikat`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum HasilPastikan {
    /// Sudah ada dan mencakup akhiran, atau tidak bisa/tidak perlu dibuat
    /// (nginx, tanpa Apache, paket tanpa openssl - diam saja).
    TidakPerlu,
    /// Sertifikat dibuat pertama kali.
    Dibuat,
    /// CA lama diganti; kepercayaannya hilang, pengguna perlu "Percayai" lagi.
    CaDiganti,
    Gagal(Pesan),
}

/// Buat sertifikat bila perlu. Tanpa ini HTTPS mati total dan browser hanya
/// bilang "tidak dapat tersambung" (Firefox kerap menaikkan http ke https).
pub fn pastikan<S: AsRef<str>>(
    apache: Option<&BinPackage>,
    web_server: &str,
    suffix: &str,
    akhiran_lain: &[S],
) -> HasilPastikan {
    if !perlu_dibuat(suffix) || apache.is_none() || web_server == "nginx" || find_openssl(apache).is_none() {
        return HasilPastikan::TidakPerlu;
    }
    let ganti_ca = exists();
    match generate(apache, suffix, akhiran_lain) {
        Err(p) => HasilPastikan::Gagal(p),
        Ok(()) if ganti_ca => HasilPastikan::CaDiganti,
        Ok(()) => HasilPastikan::Dibuat,
    }
}

// ------------------------------------------------------------ CryptoAPI

mod capi {
    use std::path::Path;
    use windows_sys::Win32::Security::Cryptography::*;

    pub const ENC: u32 = X509_ASN_ENCODING | PKCS_7_ASN_ENCODING;
    const CERT_ALT_NAME_DNS_NAME: u32 = 3;

    /// Konteks sertifikat yang dibebaskan otomatis.
    pub struct Sert(pub *mut CERT_CONTEXT);

    impl Drop for Sert {
        fn drop(&mut self) {
            if !self.0.is_null() {
                unsafe { CertFreeCertificateContext(self.0) };
            }
        }
    }

    /// PEM (openssl) atau DER.
    pub fn muat(path: &Path) -> Option<Sert> {
        let b = std::fs::read(path).ok()?;
        let der = if b.windows(10).any(|w| w == b"-----BEGIN") {
            let w: Vec<u16> = String::from_utf8_lossy(&b).encode_utf16().collect();
            let mut n = 0u32;
            let ok = unsafe {
                CryptStringToBinaryW(w.as_ptr(), w.len() as u32, CRYPT_STRING_BASE64HEADER, std::ptr::null_mut(), &mut n, std::ptr::null_mut(), std::ptr::null_mut())
            };
            if ok == 0 || n == 0 {
                return None;
            }
            let mut d = vec![0u8; n as usize];
            let ok = unsafe {
                CryptStringToBinaryW(w.as_ptr(), w.len() as u32, CRYPT_STRING_BASE64HEADER, d.as_mut_ptr(), &mut n, std::ptr::null_mut(), std::ptr::null_mut())
            };
            if ok == 0 {
                return None;
            }
            d.truncate(n as usize);
            d
        } else {
            b
        };
        let c = unsafe { CertCreateCertificateContext(ENC, der.as_ptr(), der.len() as u32) };
        (!c.is_null()).then_some(Sert(c))
    }

    pub fn sidik_jari(c: *const CERT_CONTEXT) -> Option<Vec<u8>> {
        let mut n = 0u32;
        unsafe {
            if CertGetCertificateContextProperty(c, CERT_SHA1_HASH_PROP_ID, std::ptr::null_mut(), &mut n) == 0 {
                return None;
            }
            let mut v = vec![0u8; n as usize];
            if CertGetCertificateContextProperty(c, CERT_SHA1_HASH_PROP_ID, v.as_mut_ptr().cast(), &mut n) == 0 {
                return None;
            }
            v.truncate(n as usize);
            Some(v)
        }
    }

    /// Nama DNS di subtree "permitted" nameConstraints; None bila CA tidak
    /// punya ekstensi itu sama sekali.
    pub fn dns_diizinkan(c: &Sert) -> Option<Vec<String>> {
        unsafe {
            let info = &*(*c.0).pCertInfo;
            let ext = CertFindExtension(szOID_NAME_CONSTRAINTS, info.cExtension, info.rgExtension);
            if ext.is_null() {
                return None;
            }
            let ext = &*ext;
            let mut p: *mut CERT_NAME_CONSTRAINTS_INFO = std::ptr::null_mut();
            let mut n = 0u32;
            let ok = CryptDecodeObjectEx(
                ENC,
                X509_NAME_CONSTRAINTS,
                ext.Value.pbData,
                ext.Value.cbData,
                CRYPT_DECODE_ALLOC_FLAG,
                std::ptr::null(),
                (&mut p as *mut *mut CERT_NAME_CONSTRAINTS_INFO).cast(),
                &mut n,
            );
            if ok == 0 || p.is_null() {
                return Some(Vec::new());
            }
            let nc = &*p;
            let mut hasil = Vec::new();
            for i in 0..nc.cPermittedSubtree as usize {
                let st = &*nc.rgPermittedSubtree.add(i);
                if st.Base.dwAltNameChoice == CERT_ALT_NAME_DNS_NAME {
                    let w = st.Base.Anonymous.pwszDNSName;
                    if !w.is_null() {
                        let mut len = 0;
                        while *w.add(len) != 0 {
                            len += 1;
                        }
                        hasil.push(String::from_utf16_lossy(std::slice::from_raw_parts(w, len)));
                    }
                }
            }
            windows_sys::Win32::Foundation::LocalFree(p.cast());
            Some(hasil)
        }
    }

    pub fn buka_root(mesin: bool, tulis: bool) -> HCERTSTORE {
        let lokasi = if mesin { CERT_SYSTEM_STORE_LOCAL_MACHINE } else { CERT_SYSTEM_STORE_CURRENT_USER };
        let mut flags = lokasi | CERT_STORE_OPEN_EXISTING_FLAG;
        if !tulis {
            flags |= CERT_STORE_READONLY_FLAG;
        }
        let nama: Vec<u16> = "Root".encode_utf16().chain(Some(0)).collect();
        unsafe { CertOpenStore(CERT_STORE_PROV_SYSTEM_W, 0, 0, flags, nama.as_ptr().cast()) }
    }

    pub fn ada_di(store: HCERTSTORE, sidik: &[u8]) -> bool {
        let blob = CRYPT_INTEGER_BLOB { cbData: sidik.len() as u32, pbData: sidik.as_ptr() as *mut u8 };
        unsafe {
            let c = CertFindCertificateInStore(store, ENC, 0, CERT_FIND_SHA1_HASH, (&blob as *const CRYPT_INTEGER_BLOB).cast(), std::ptr::null());
            if c.is_null() {
                return false;
            }
            CertFreeCertificateContext(c);
            true
        }
    }

    pub fn subjek(c: *const CERT_CONTEXT) -> String {
        unsafe {
            let info = &*(*c).pCertInfo;
            let n = CertNameToStrW(ENC, &info.Subject, CERT_X500_NAME_STR, std::ptr::null_mut(), 0);
            if n <= 1 {
                return String::new();
            }
            let mut w = vec![0u16; n as usize];
            CertNameToStrW(ENC, &info.Subject, CERT_X500_NAME_STR, w.as_mut_ptr(), n);
            String::from_utf16_lossy(&w[..n as usize - 1])
        }
    }

    pub fn self_signed(c: *const CERT_CONTEXT) -> bool {
        unsafe {
            let info = &*(*c).pCertInfo;
            CertCompareCertificateName(ENC, &info.Subject, &info.Issuer) != 0
        }
    }
}

/// Apakah daftar DNS batasan memuat akhiran ini, per nama utuh.
pub fn batasan_memuat<S: AsRef<str>>(dns: &[S], suffix: &str) -> bool {
    dns.iter().any(|d| crate::teks::sama_abaikan_besar(d.as_ref().trim_start_matches('.'), suffix))
}

/// DNS "permitted" di nameConstraints CA yang ada; None bila tidak ada CA
/// atau CA tanpa batasan.
pub fn batasan_ca() -> Option<Vec<String>> {
    capi::dns_diizinkan(&capi::muat(&ca_crt_path())?)
}

/// CA yang ada punya batasan nama dan batasan itu mencakup akhiran ini.
/// CA tanpa batasan sama sekali selalu false.
pub fn ca_mencakup(suffix: &str) -> bool {
    batasan_ca().is_some_and(|d| batasan_memuat(&d, &akhiran(suffix)))
}

/// Sertifikat perlu dibuat (ulang) untuk akhiran ini.
pub fn perlu_dibuat(suffix: &str) -> bool {
    !exists() || !ca_mencakup(suffix)
}

// ------------------------------------------------------------ Kepercayaan

/// Menelusuri gudang Root berarti membaca ratusan sertifikat, dua gudang
/// sekaligus, dan Beranda menanyakannya beberapa kali per penyegaran.
static PERCAYA: Mutex<Option<(bool, Instant)>> = Mutex::new(None);
const UMUR_PERCAYA: Duration = Duration::from_secs(20);

/// Lupakan jawaban [`is_trusted`] yang tersimpan.
pub fn lupakan_kepercayaan() {
    *PERCAYA.lock().unwrap_or_else(|e| e.into_inner()) = None;
}

/// CA sudah terpasang di Trusted Root (pengguna atau mesin)? Lewat sidik jari:
/// sertifikat lama bersubjek sama tapi berkunci lain tetap diperingatkan browser.
pub fn is_trusted() -> bool {
    if !exists() {
        return false;
    }
    let mut g = PERCAYA.lock().unwrap_or_else(|e| e.into_inner());
    if let Some((v, t)) = *g {
        if t.elapsed() < UMUR_PERCAYA {
            return v;
        }
    }
    let v = hitung_kepercayaan();
    *g = Some((v, Instant::now()));
    v
}

fn hitung_kepercayaan() -> bool {
    let Some(sidik) = capi::muat(&ca_crt_path()).and_then(|c| capi::sidik_jari(c.0)) else {
        return false;
    };
    for mesin in [false, true] {
        let store = capi::buka_root(mesin, false);
        if store.is_null() {
            continue;
        }
        let ada = capi::ada_di(store, &sidik);
        unsafe { windows_sys::Win32::Security::Cryptography::CertCloseStore(store, 0) };
        if ada {
            return true;
        }
    }
    false
}

/// Pasang CA ke Trusted Root Windows. Butuh hak Administrator.
pub fn trust() -> Result<(), Pesan> {
    if !exists() {
        return Err(Pesan::baru("Sertifikat belum dibuat."));
    }
    if !crate::tulis::is_admin() {
        return Err(Pesan::baru("Perlu menjalankan Phoron sebagai Administrator."));
    }
    let res = shell::run(
        "certutil.exe",
        &format!("-addstore -f Root \"{}\"", ca_crt_path().display()),
        Some(&paths::root()),
        60_000,
        None,
        None,
    )
    .unwrap_or_else(|e| shell::RunResult { exit_code: -1, stderr: e.to_string(), ..Default::default() });
    // Gudangnya baru saja berubah.
    lupakan_kepercayaan();
    if !res.ok() {
        return Err(Pesan::dengan("certutil gagal: {0}", &[&res.all()]));
    }
    buang_akar_lama();
    Ok(())
}

/// Buang akar Phoron lama dari Trusted Root: self-signed, O=Phoron Local
/// Development, dan bukan CA yang sekarang. Gagal diam-diam - bersih-bersih,
/// bukan syarat HTTPS jalan.
fn buang_akar_lama() {
    use windows_sys::Win32::Security::Cryptography::*;
    let Some(sidik) = capi::muat(&ca_crt_path()).and_then(|c| capi::sidik_jari(c.0)) else {
        return;
    };
    let tanda = format!("O={ORGANISASI}");
    for mesin in [true, false] {
        let store = capi::buka_root(mesin, true);
        if store.is_null() {
            continue;
        }
        unsafe {
            let mut c: *mut CERT_CONTEXT = std::ptr::null_mut();
            loop {
                c = CertEnumCertificatesInStore(store, c);
                if c.is_null() {
                    break;
                }
                let milik = capi::self_signed(c) && capi::subjek(c).contains(&tanda);
                if milik && capi::sidik_jari(c).is_some_and(|s| s != sidik) {
                    // Hapus salinannya: CertDeleteCertificateFromStore selalu
                    // membebaskan konteks yang diberikan, sedangkan `c` masih
                    // dipakai untuk melanjutkan penelusuran.
                    CertDeleteCertificateFromStore(CertDuplicateCertificateContext(c));
                }
            }
            CertCloseStore(store, 0);
        }
    }
}

#[cfg(test)]
pub(crate) mod uji {
    use super::*;
    use crate::model::BinKind;

    /// openssl.exe sungguhan dari paket Apache yang ada di mesin ini (hanya
    /// dibaca). `PHORON_OPENSSL_UJI` boleh menunjuk folder paket Apache.
    pub(crate) fn apache_uji() -> Option<BinPackage> {
        let mut calon: Vec<PathBuf> = Vec::new();
        if let Ok(p) = std::env::var("PHORON_OPENSSL_UJI") {
            calon.push(PathBuf::from(p));
        }
        for akar in [r"C:\laragon\bin\apache", r"C:\Phoron\bin\apache", r"C:\Phoron\bin"] {
            if let Ok(rd) = std::fs::read_dir(akar) {
                let mut v: Vec<PathBuf> = rd.flatten().map(|e| e.path()).collect();
                v.sort();
                calon.extend(v.into_iter().rev());
            }
        }
        calon.into_iter().find(|d| d.join(r"bin\openssl.exe").is_file()).map(|d| {
            BinPackage::baru(BinKind::Apache, &d.file_name().unwrap().to_string_lossy(), &d.to_string_lossy(), "")
        })
    }

    #[test]
    fn batasan_nama_seperti_csharp() {
        assert_eq!(
            batasan_nama(" .Lokal. ", &["test", "", "dev", "LOKAL"]),
            "critical, permitted;DNS:lokal, permitted;DNS:test, permitted;DNS:localhost, permitted;DNS:dev, \
             permitted;IP:127.0.0.1/255.255.255.255, permitted;IP:0:0:0:0:0:0:0:1/FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF"
        );
        assert!(batasan_nama::<&str>("", &[]).starts_with("critical, permitted;DNS:test, permitted;DNS:localhost, permitted;IP"));
    }

    #[test]
    fn batasan_dicocokkan_per_nama_utuh() {
        assert!(batasan_memuat(&["testing", "localhost"], "localhost"));
        assert!(!batasan_memuat(&["testing"], "test"));
        assert!(batasan_memuat(&[".Test"], "test"));
    }

    #[test]
    fn cnf_memuat_san_dan_batasan() {
        let s = isi_cnf_situs("test");
        assert!(s.contains("\r\nDNS.1 = *.test\r\nDNS.2 = test\r\nDNS.3 = localhost\r\nIP.1 = 127.0.0.1\r\nIP.2 = ::1\r\n"));
        assert!(s.contains("basicConstraints = critical, CA:FALSE\r\n") && s.contains("subjectAltName = @alt"));
        let c = isi_cnf_ca("test", &["local"]);
        assert!(c.contains("basicConstraints = critical, CA:TRUE, pathlen:0\r\n"));
        assert!(c.contains("nameConstraints = critical, permitted;DNS:test, permitted;DNS:localhost, permitted;DNS:local,"));
        assert!(!c.contains("-addext"));
    }

    #[test]
    fn seri_acak_tidak_berulang() {
        let (a, b) = (seri_acak(), seri_acak());
        assert_eq!(a.len(), 16);
        assert!(a.chars().all(|c| c.is_ascii_hexdigit()));
        assert_ne!(a, b);
    }

    #[test]
    fn tanpa_openssl_dan_tanpa_sertifikat() {
        let (_g, d) = paths::sandbox_uji("ssl-kosong");
        let palsu = BinPackage::baru(BinKind::Apache, "x", &d.to_string_lossy(), "");
        assert_eq!(
            generate::<&str>(Some(&palsu), "test", &[]),
            Err(Pesan::baru("openssl.exe tidak ditemukan di paket Apache yang dipilih."))
        );
        assert!(!exists() && perlu_dibuat("test") && !ca_mencakup("test") && !is_trusted());
        // Diperiksa SEBELUM hak Administrator: uji tidak pernah memasang apa pun.
        assert_eq!(trust(), Err(Pesan::baru("Sertifikat belum dibuat.")));
        assert_eq!(pastikan::<&str>(Some(&palsu), "apache", "test", &[]), HasilPastikan::TidakPerlu);
        let _ = std::fs::remove_dir_all(&d);
    }

    #[test]
    fn sertifikat_dengan_openssl_sungguhan() {
        let Some(apache) = apache_uji() else {
            eprintln!("(dilewati: tidak ada paket Apache yang membawa openssl.exe)");
            return;
        };
        let (_g, d) = paths::sandbox_uji("ssl-openssl");
        let v0 = crate::tulis::versi_konfig_web();
        assert_eq!(pastikan::<&str>(Some(&apache), "nginx", "test", &[]), HasilPastikan::TidakPerlu);
        assert_eq!(pastikan::<&str>(Some(&apache), "apache", "test", &[]), HasilPastikan::Dibuat);
        assert!(exists() && ca_key_path().is_file());
        assert!(crate::tulis::versi_konfig_web() > v0);
        assert!(ca_mencakup("test") && ca_mencakup("localhost") && !ca_mencakup("com"));
        assert!(!perlu_dibuat("test"));
        // Sandbox CA tidak pernah dipasang, jadi tidak mungkin dipercaya.
        assert!(!is_trusted());

        // Membuat ulang: CA tetap, sertifikat situs berganti, tanpa sisa.
        let ca_lama = std::fs::read(ca_crt_path()).unwrap();
        let situs_lama = std::fs::read(crt_path()).unwrap();
        generate::<&str>(Some(&apache), "test", &[]).unwrap();
        assert_eq!(std::fs::read(ca_crt_path()).unwrap(), ca_lama);
        assert_ne!(std::fs::read(crt_path()).unwrap(), situs_lama);
        assert!(!tambah(&crt_path(), ".baru").exists() && !tambah(&key_path(), ".baru").exists());
        assert!(!paths::tmp().join("phoron.csr").exists());

        // Akhiran di luar batasan: CA dibuat ulang, mencakup keduanya.
        assert!(perlu_dibuat("lokal"));
        assert_eq!(pastikan(Some(&apache), "apache", "lokal", &["test"]), HasilPastikan::CaDiganti);
        assert_ne!(std::fs::read(ca_crt_path()).unwrap(), ca_lama);
        assert!(ca_mencakup("lokal") && ca_mencakup("test"));

        // CA tanpa batasan (model lama): selalu dianggap tidak mencakup.
        std::fs::copy(crt_path(), ca_crt_path()).unwrap();
        assert!(batasan_ca().is_none() && perlu_dibuat("lokal"));
        let _ = std::fs::remove_dir_all(&d);
    }
}
