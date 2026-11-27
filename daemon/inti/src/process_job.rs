//! Fitur dari ProcessJob.cs - Job Object: anak proses ikut mati bersama Phoron.
//!
//! Semua proses anak (httpd, mysqld, php-cgi, node) diikat ke satu Job Object
//! milik daemon dengan `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. Begitu daemon
//! berakhir - dengan cara apa pun, termasuk dihentikan paksa Task Manager atau
//! ditutup pemasang - Windows menutup handle job itu dan ikut mematikan seluruh
//! anggotanya.
//!
//! Daftar periksa (pelajaran dari versi C#):
//! - Tanpa job, anak jadi yatim dan tetap memegang port 80/3306 tanpa ada
//!   jendela yang mengaku memilikinya. Kasus nyatanya: pemasang menutup Phoron,
//!   penutupan dibatalkan "mengecil ke baki", pemasang lalu menghentikan
//!   prosesnya, dan pembersihan normal tidak sempat berjalan. Uji:
//!   `menutup_job_mematikan_anak_dan_cucu`.
//! - Job TANPA nama: hanya proses ini yang memegangnya, jadi dua salinan
//!   Phoron tidak saling membunuh anak masing-masing. Uji:
//!   `dua_job_tidak_saling_mematikan`.
//! - Kegagalan membuat atau mengikat job didiamkan: job hanya jaring
//!   pengaman, layanan harus tetap bisa jalan walau Windows menolak. Uji:
//!   `spawn_tetap_jalan_tanpa_job`.
//! - Penghentian lewat job tidak santun (mysqld tidak sempat menutup InnoDB
//!   dengan rapi); jalur biasa tetap mysqladmin shutdown di ServiceManager.
//!   Ini jaring, bukan jalur utama.
//!
//! Beda yang disengaja dengan C#: C# mengikat proses SESUDAH `Process.Start`,
//! sehingga cmd.exe sempat memunculkan node.exe sebelum masuk job, dan cucu
//! itu lolos dari jaring. Di sini proses dibuat tertangguh
//! (`CREATE_SUSPENDED`), diikat, baru dijalankan - cucu mana pun pasti lahir
//! di dalam job. Uji: `spawn_terikat_sebelum_jalan`.

use std::os::windows::io::AsRawHandle;
use std::os::windows::process::CommandExt;
use std::process::{Child, Command};
use std::sync::OnceLock;
use windows_sys::Win32::Foundation::{CloseHandle, HANDLE, INVALID_HANDLE_VALUE, STILL_ACTIVE};
use windows_sys::Win32::System::Diagnostics::ToolHelp::{
    CreateToolhelp32Snapshot, Process32FirstW, Process32NextW, Thread32First, Thread32Next, PROCESSENTRY32W,
    TH32CS_SNAPPROCESS, TH32CS_SNAPTHREAD, THREADENTRY32,
};
use windows_sys::Win32::System::JobObjects::{
    AssignProcessToJobObject, CreateJobObjectW, IsProcessInJob, JobObjectExtendedLimitInformation,
    SetInformationJobObject, JOBOBJECT_EXTENDED_LIMIT_INFORMATION, JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
};
use windows_sys::Win32::System::Threading::{
    GetExitCodeProcess, OpenProcess, OpenThread, ResumeThread, CREATE_SUSPENDED, PROCESS_QUERY_LIMITED_INFORMATION,
    THREAD_SUSPEND_RESUME,
};

/// Satu Job Object berbendera KILL_ON_JOB_CLOSE. Ditutup (dan anggotanya
/// dimatikan) saat nilai ini di-drop.
pub struct Job(HANDLE);

// HANDLE hanya angka milik kernel; boleh dipakai dari utas mana pun.
unsafe impl Send for Job {}
unsafe impl Sync for Job {}

impl Job {
    /// Job baru tanpa nama. `None` bila Windows menolak.
    pub fn baru() -> Option<Job> {
        // SAFETY: argumen null valid untuk job tanpa nama dan atribut bawaan.
        let h = unsafe { CreateJobObjectW(std::ptr::null(), std::ptr::null()) };
        if h.is_null() {
            return None;
        }
        let job = Job(h);
        let mut info: JOBOBJECT_EXTENDED_LIMIT_INFORMATION = unsafe { std::mem::zeroed() };
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        // SAFETY: info hidup sepanjang panggilan dan ukurannya tepat.
        let ok = unsafe {
            SetInformationJobObject(
                job.0,
                JobObjectExtendedLimitInformation,
                &info as *const _ as *const core::ffi::c_void,
                std::mem::size_of::<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>() as u32,
            )
        };
        // Job tanpa bendera kill-on-close tidak berguna sebagai jaring.
        (ok != 0).then_some(job)
    }

    /// Masukkan proses yang sudah berjalan. Benar bila berhasil.
    pub fn ikat(&self, anak: &Child) -> bool {
        // SAFETY: handle proses milik `anak` masih terbuka selama pinjaman ini.
        unsafe { AssignProcessToJobObject(self.0, anak.as_raw_handle() as HANDLE) != 0 }
    }

    /// Apakah proses itu anggota job ini.
    pub fn berisi(&self, anak: &Child) -> bool {
        let mut hasil = 0;
        // SAFETY: kedua handle valid; hasil ditulis ke variabel lokal.
        let ok = unsafe { IsProcessInJob(anak.as_raw_handle() as HANDLE, self.0, &mut hasil) };
        ok != 0 && hasil != 0
    }

    /// Jalankan `cmd` di dalam job ini: dibuat tertangguh, diikat, lalu
    /// dilanjutkan. `flags` = bendera pembuatan proses lain yang diinginkan
    /// pemanggil (mis. CREATE_NO_WINDOW); `Command::creation_flags` milik
    /// pemanggil akan ditimpa oleh nilai ini.
    pub fn spawn(&self, cmd: &mut Command, flags: u32) -> std::io::Result<Child> {
        spawn_ke(Some(self), cmd, flags)
    }
}

impl Drop for Job {
    fn drop(&mut self) {
        // SAFETY: handle milik kita dan hanya ditutup sekali.
        unsafe { CloseHandle(self.0) };
    }
}

/// Job selebar proses; dibuat saat pertama dibutuhkan dan dibiarkan hidup
/// sampai daemon berakhir - justru penutupannya oleh Windows yang mematikan
/// anak-anak.
pub fn job() -> Option<&'static Job> {
    static JOB: OnceLock<Option<Job>> = OnceLock::new();
    JOB.get_or_init(Job::baru).as_ref()
}

/// Ikat proses yang sudah berjalan ke job daemon. Kegagalan didiamkan
/// (lihat daftar periksa); nilai balik hanya untuk keterangan.
pub fn ikat(anak: &Child) -> bool {
    job().is_some_and(|j| j.ikat(anak))
}

/// Jalankan `cmd` di dalam job daemon, terikat sebelum sempat memunculkan
/// cucu. Bila job tidak tersedia, proses tetap dijalankan tanpa jaring.
pub fn spawn_terikat(cmd: &mut Command, flags: u32) -> std::io::Result<Child> {
    spawn_ke(job(), cmd, flags)
}

fn spawn_ke(job: Option<&Job>, cmd: &mut Command, flags: u32) -> std::io::Result<Child> {
    cmd.creation_flags(flags | CREATE_SUSPENDED);
    let mut anak = cmd.spawn()?;
    if let Some(j) = job {
        // Gagal mengikat bukan alasan membatalkan layanan.
        let _ = j.ikat(&anak);
    }
    if !lanjutkan(anak.id()) {
        let _ = anak.kill();
        let _ = anak.wait();
        return Err(std::io::Error::other("proses tertangguh tidak bisa dilanjutkan"));
    }
    Ok(anak)
}

/// Lanjutkan utas proses yang baru dibuat tertangguh (hanya ada satu).
fn lanjutkan(pid: u32) -> bool {
    // SAFETY: snapshot dibaca dengan struktur berukuran benar lalu ditutup.
    unsafe {
        let snap = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
        if snap == INVALID_HANDLE_VALUE {
            return false;
        }
        let mut te = THREADENTRY32 { dwSize: std::mem::size_of::<THREADENTRY32>() as u32, ..Default::default() };
        let mut berhasil = false;
        let mut ada = Thread32First(snap, &mut te) != 0;
        while ada {
            if te.th32OwnerProcessID == pid {
                let t = OpenThread(THREAD_SUSPEND_RESUME, 0, te.th32ThreadID);
                if !t.is_null() {
                    if ResumeThread(t) != u32::MAX {
                        berhasil = true;
                    }
                    CloseHandle(t);
                }
            }
            ada = Thread32Next(snap, &mut te) != 0;
        }
        CloseHandle(snap);
        berhasil
    }
}

/// PID anak langsung dari sebuah proses (untuk mencari node.exe di bawah
/// cmd.exe).
pub fn anak_dari(pid: u32) -> Vec<u32> {
    let mut hasil = Vec::new();
    // SAFETY: seperti `lanjutkan`.
    unsafe {
        let snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if snap == INVALID_HANDLE_VALUE {
            return hasil;
        }
        let mut pe =
            PROCESSENTRY32W { dwSize: std::mem::size_of::<PROCESSENTRY32W>() as u32, ..Default::default() };
        let mut ada = Process32FirstW(snap, &mut pe) != 0;
        while ada {
            if pe.th32ParentProcessID == pid && pe.th32ProcessID != pid {
                hasil.push(pe.th32ProcessID);
            }
            ada = Process32NextW(snap, &mut pe) != 0;
        }
        CloseHandle(snap);
    }
    hasil
}

/// Seluruh keturunan sebuah proses, dari anak sampai cicit.
pub fn keturunan(pid: u32) -> Vec<u32> {
    let mut hasil = Vec::new();
    let mut antre = vec![pid];
    while let Some(p) = antre.pop() {
        for a in anak_dari(p) {
            if !hasil.contains(&a) {
                hasil.push(a);
                antre.push(a);
            }
        }
    }
    hasil
}

/// Apakah proses ber-PID itu masih hidup.
pub fn masih_hidup(pid: u32) -> bool {
    // SAFETY: handle dibuka, dibaca, lalu ditutup.
    unsafe {
        let h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid);
        if h.is_null() {
            return false;
        }
        let mut kode = 0u32;
        let ok = GetExitCodeProcess(h, &mut kode);
        CloseHandle(h);
        ok != 0 && kode == STILL_ACTIVE as u32
    }
}

#[cfg(test)]
mod uji {
    use super::*;
    use std::process::Stdio;
    use std::time::{Duration, Instant};

    const CREATE_NO_WINDOW: u32 = 0x0800_0000;

    fn tunggu(syarat: impl Fn() -> bool, ms: u64) -> bool {
        let mulai = Instant::now();
        while mulai.elapsed() < Duration::from_millis(ms) {
            if syarat() {
                return true;
            }
            std::thread::sleep(Duration::from_millis(50));
        }
        syarat()
    }

    /// cmd.exe yang memunculkan cucu ping yang hidup lama.
    fn cmd_dengan_cucu() -> Command {
        let mut c = Command::new("cmd.exe");
        c.raw_arg("/c ping -n 60 127.0.0.1 >nul").stdout(Stdio::null()).stderr(Stdio::null()).stdin(Stdio::null());
        c
    }

    #[test]
    fn spawn_terikat_sebelum_jalan() {
        let job = Job::baru().expect("job");
        let mut anak = job.spawn(&mut cmd_dengan_cucu(), CREATE_NO_WINDOW).unwrap();
        assert!(job.berisi(&anak));
        // Proses benar-benar berjalan (tidak tertinggal tertangguh): ia
        // sempat memunculkan cucunya.
        assert!(tunggu(|| !keturunan(anak.id()).is_empty(), 5000));
        let _ = anak.kill();
        drop(job);
        let _ = anak.wait();
    }

    #[test]
    fn menutup_job_mematikan_anak_dan_cucu() {
        let job = Job::baru().expect("job");
        let mut anak = job.spawn(&mut cmd_dengan_cucu(), CREATE_NO_WINDOW).unwrap();
        assert!(tunggu(|| !keturunan(anak.id()).is_empty(), 5000));
        let cucu = keturunan(anak.id());
        assert!(cucu.iter().all(|p| masih_hidup(*p)));
        drop(job);
        assert!(tunggu(|| !masih_hidup(anak.id()), 5000), "anak masih hidup sesudah job ditutup");
        assert!(tunggu(|| cucu.iter().all(|p| !masih_hidup(*p)), 5000), "cucu masih hidup sesudah job ditutup");
        let _ = anak.wait();
    }

    #[test]
    fn dua_job_tidak_saling_mematikan() {
        let a = Job::baru().expect("job a");
        let b = Job::baru().expect("job b");
        let mut pa = a.spawn(&mut cmd_dengan_cucu(), CREATE_NO_WINDOW).unwrap();
        let mut pb = b.spawn(&mut cmd_dengan_cucu(), CREATE_NO_WINDOW).unwrap();
        assert!(!a.berisi(&pb) && !b.berisi(&pa));
        drop(a);
        assert!(tunggu(|| !masih_hidup(pa.id()), 5000));
        assert!(masih_hidup(pb.id()), "job lain ikut mematikan proses yang bukan miliknya");
        drop(b);
        assert!(tunggu(|| !masih_hidup(pb.id()), 5000));
        let _ = pa.wait();
        let _ = pb.wait();
    }

    #[test]
    fn spawn_tetap_jalan_tanpa_job() {
        let mut c = Command::new("cmd.exe");
        c.raw_arg("/c exit /b 7").stdin(Stdio::null());
        let mut anak = spawn_ke(None, &mut c, CREATE_NO_WINDOW).unwrap();
        assert_eq!(anak.wait().unwrap().code(), Some(7));
    }

    #[test]
    fn job_daemon_satu_dan_mengikat() {
        let j1 = job().expect("job daemon") as *const Job;
        let j2 = job().unwrap() as *const Job;
        assert_eq!(j1, j2);
        let mut c = Command::new("cmd.exe");
        c.raw_arg("/c exit /b 0").stdin(Stdio::null());
        let mut anak = spawn_terikat(&mut c, CREATE_NO_WINDOW).unwrap();
        let _ = anak.wait();
        assert!(!masih_hidup(anak.id()));
    }
}
