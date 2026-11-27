//! Padanan bagian `Shell.cs` yang dipakai Core: menjalankan perintah singkat
//! dan mematikan pohon proses.
//!
//! Argumen dikirim sebagai SATU string apa adanya (`raw_arg`), sama seperti
//! `ProcessStartInfo(exe, args)`: pemanggil yang sudah menyusun kutipan untuk
//! httpd/mysqld tidak boleh dikutip ulang oleh Rust.

use std::collections::HashMap;
use std::io::{Read, Write};
use std::os::windows::process::CommandExt;
use std::path::Path;
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};

const CREATE_NO_WINDOW: u32 = 0x0800_0000;

#[derive(Debug, Default, Clone)]
pub struct RunResult {
    pub exit_code: i32,
    pub stdout: String,
    pub stderr: String,
    pub timed_out: bool,
}

impl RunResult {
    /// `(StdOut + "\n" + StdErr).Trim()`.
    pub fn all(&self) -> String {
        format!("{}\n{}", self.stdout, self.stderr).trim().to_string()
    }

    pub fn ok(&self) -> bool {
        self.exit_code == 0 && !self.timed_out
    }
}

/// Seperti `OutputDataReceived` + `AppendLine`: tiap baris diakhiri CRLF,
/// pemisah \r\n, \n, atau \r sendirian.
fn per_baris(b: &[u8]) -> String {
    let teks = String::from_utf8_lossy(b);
    let mut s = String::with_capacity(teks.len() + 16);
    let mut baris = String::new();
    let mut cs = teks.chars().peekable();
    let mut ada = false;
    while let Some(c) = cs.next() {
        ada = true;
        match c {
            '\r' => {
                if cs.peek() == Some(&'\n') {
                    cs.next();
                }
                s.push_str(&baris);
                s.push_str("\r\n");
                baris.clear();
                ada = false;
            }
            '\n' => {
                s.push_str(&baris);
                s.push_str("\r\n");
                baris.clear();
                ada = false;
            }
            _ => baris.push(c),
        }
    }
    if ada {
        s.push_str(&baris);
        s.push_str("\r\n");
    }
    s
}

/// Jalankan dan tunggu sampai selesai. `timeout_ms <= 0` berarti tanpa batas.
pub fn run(
    exe: &str,
    args: &str,
    work_dir: Option<&Path>,
    timeout_ms: i64,
    env: Option<&HashMap<String, String>>,
    stdin: Option<&str>,
) -> std::io::Result<RunResult> {
    let mut cmd = Command::new(exe);
    if !args.is_empty() {
        cmd.raw_arg(args);
    }
    let dir = work_dir
        .map(Path::to_path_buf)
        .or_else(|| Path::new(exe).parent().filter(|p| !p.as_os_str().is_empty()).map(Path::to_path_buf))
        .unwrap_or_else(crate::paths::root);
    // Folder kerja yang tidak ada membuat CreateProcess gagal dengan galat yang
    // menyebut exe-nya, bukan foldernya. Lebih baik mewarisi folder kerja kita.
    if dir.is_dir() {
        cmd.current_dir(dir);
    }
    cmd.stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .stdin(if stdin.is_some() { Stdio::piped() } else { Stdio::null() })
        .creation_flags(CREATE_NO_WINDOW);
    if let Some(env) = env {
        for (k, v) in env {
            cmd.env(k, v);
        }
    }
    let mut anak = cmd.spawn()?;

    // Dibaca di utas sendiri: proses yang menulis banyak ke stderr sementara
    // stdout belum dibaca akan menunggu selamanya kalau keduanya dibaca bergiliran.
    let mut so = anak.stdout.take().unwrap();
    let mut se = anak.stderr.take().unwrap();
    let t_out = std::thread::spawn(move || {
        let mut b = Vec::new();
        let _ = so.read_to_end(&mut b);
        b
    });
    let t_err = std::thread::spawn(move || {
        let mut b = Vec::new();
        let _ = se.read_to_end(&mut b);
        b
    });

    if let Some(teks) = stdin {
        // Byte UTF-8 mentah, lalu ditutup: mysql.exe membaca sampai akhir
        // masukan, jadi tanpa penutupan ia menunggu selamanya.
        if let Some(mut i) = anak.stdin.take() {
            let _ = i.write_all(teks.as_bytes());
        }
    }

    let mulai = Instant::now();
    let mut hasil = RunResult::default();
    loop {
        if let Some(status) = anak.try_wait()? {
            hasil.exit_code = status.code().unwrap_or(-1);
            break;
        }
        if timeout_ms > 0 && mulai.elapsed() >= Duration::from_millis(timeout_ms as u64) {
            hasil.timed_out = true;
            kill_tree(anak.id());
            let _ = anak.wait();
            break;
        }
        std::thread::sleep(Duration::from_millis(20));
    }
    hasil.stdout = per_baris(&t_out.join().unwrap_or_default());
    hasil.stderr = per_baris(&t_err.join().unwrap_or_default());
    Ok(hasil)
}

/// Matikan proses beserta anak-anaknya. httpd dan mysqld memunculkan proses
/// anak; membunuh induknya saja meninggalkan anak yang tetap memegang port.
pub fn kill_tree(pid: u32) {
    // Folder Windows, bukan akar Phoron: folder yang pasti ada, supaya
    // mematikan proses tidak pernah gagal gara-gara folder kerjanya.
    let sistem = std::env::var("SystemRoot").unwrap_or_else(|_| r"C:\Windows".into());
    let _ = run("taskkill.exe", &format!("/PID {pid} /T /F"), Some(Path::new(&sistem)), 15_000, None, None);
}

#[cfg(test)]
mod uji {
    use super::*;

    /// Folder kerja yang tidak ikut dihapus uji lain yang berjalan paralel.
    fn tmp() -> std::path::PathBuf {
        std::env::temp_dir()
    }

    #[test]
    fn keluaran_dan_kode_keluar() {
        let r = run("cmd.exe", "/c echo halo& echo galat 1>&2& exit /b 3", Some(&tmp()), 10_000, None, None).unwrap();
        assert_eq!(r.exit_code, 3);
        assert_eq!(r.stdout, "halo\r\n");
        assert_eq!(r.stderr.trim(), "galat");
        assert_eq!(r.all(), "halo\r\n\ngalat");
    }

    #[test]
    fn batas_waktu_mematikan_proses() {
        let r = run("cmd.exe", "/c ping -n 30 127.0.0.1 >nul", Some(&tmp()), 300, None, None).unwrap();
        assert!(r.timed_out);
        assert!(!r.ok());
    }

    #[test]
    fn masukan_baku_utf8() {
        let r = run("findstr.exe", "x", Some(&tmp()), 10_000, None, Some("ax\r\nb\r\n")).unwrap();
        assert_eq!(r.stdout, "ax\r\n");
    }
}
