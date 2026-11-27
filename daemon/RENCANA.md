# Rencana: Core Phoron di Rust (daemon)

Branch: `rust-daemon`. Tidak masuk rilis sampai lulus uji setara (lihat
"Gerbang rilis"). Rilis 1.41.x tetap dari kode C# di `main`.

## Bentuk akhir

```
phorond.exe (Rust, selalu hidup)            Phoron.exe (WPF, hanya saat dibuka)
- ikon baki + menu baki                <--> - semua halaman
- seluruh Core: profil, setelan,            - klien pipe, tanpa logika layanan
  pemindai bin, ConfigWriter, hosts,        - keluar penuh saat jendela ditutup
  SSL, start/stop httpd/nginx/mysqld/
  php-cgi/Node, Job Object, log
        ^ named pipe \\.\pipe\phoron (JSON per baris)
```

UI yang crash tidak ikut mematikan Apache/MySQL. Di baki, yang hidup hanya
`phorond.exe`.

## Prinsip

- **Per fitur, bukan per baris.** Sejak tahap B, Rust ditulis mengikuti cara
  kerja dan fitur Phoron, bukan terjemahan baris demi baris kode C#. Kebiasaan
  .NET yang tidak terasa pengguna tidak ditiru, dan cara yang lebih baik di
  Rust dipakai (mis. port dan PID dari `GetExtendedTcpTable`, bukan `netstat`).
- **Yang tetap wajib sama:**
  1. Format berkas milik pengguna (`phoron.ini`, `profiles\*.ini`, `apps.ini`)
     identik byte demi byte - orang bisa pindah bolak-balik antara versi C#
     dan Rust.
  2. Hasil yang dilihat pengguna: daftar paket hasil pindai, daftar situs dan
     nama hostnya, config yang membuat Apache/PHP/MySQL jalan. Dibuktikan
     dengan membandingkan HASIL (uji setara, uji live), bukan kodenya.
  3. Pelajaran bug lama. Komentar "kenapa" di C# adalah kasus nyata; tiap
     kasus jadi butir daftar periksa fitur dan punya uji, walau kodenya ditulis
     ulang dengan cara lain.
- **Dependensi seminimal mungkin.** `serde`/`serde_json` untuk protokol,
  `windows-sys` untuk Win32, `unicode-normalization` untuk nama host. Tidak ada
  runtime async.
- **Satu fitur selesai = daftar periksa terpenuhi + uji Rust + hasil setara
  dengan Phoron C# pada data nyata.**

## Tata letak

```
daemon/
  Cargo.toml          workspace
  inti/               crate lib `phoron_inti` - port Phoron.Core
  phorond/            crate bin - pipe server, baki, siklus hidup (tahap D)
  inti/tests/setara.rs  uji setara; bahannya dibangkitkan Phoron.Tests saat uji
  uji_setara.ps1      menjalankan uji setara C# -> Rust
```

## Urutan port

| Tahap | Modul C# | Isi | Status |
|---|---|---|---|
| A | `Ini`, `AtomicFile`, `Paths` | dasar baca/tulis berkas | selesai |
| A | `Settings`, `Rahasia` (DPAPI), `Deteksi` | setelan global | selesai |
| A | `Models` (`Profile`, `BinPackage`), `ProfileStore` | profil (tanpa `Seed`) | selesai |
| A | protokol pipe | tipe pesan JSON (v1) | selesai |
| B | `BinScanner`, `BinProbe`, `RuntimeVc` | pemindai versi (`bin.rs`) | selesai |
| B | `SiteScanner`, `NodeApps`, `PortCheck` | situs, proyek Node, port (`situs.rs`, `node_apps.rs`, `port.rs`) | selesai |
| C | `ConfigWriter` (1.473 baris) | httpd.conf, nginx, php.ini, my.ini, vhost | |
| C | `HostsFile`, `HostsTool`, `SslTool`, `HstsPeriksa` | hosts, sertifikat | |
| D | `ProcessJob`, `ServiceManager` (1.114) | start/stop, Job Object | |
| D | `NodeRunner`, `Engine` (1.062), `LogFile`, `Crash` | orkestrasi, log | |
| D | `phorond`: pipe server, baki, sinyal keluar, mutex | daemon (`phorond/`, Engine dipasang lewat trait `Pelaksana`) | kerangka selesai |
| E | UI WPF jadi klien pipe | `Engine` diganti proxy | |
| E | `Updater`, `Downloader`, `MySqlKlien`, `MySqlSkema` | sisa | |

`Lang*`/`Bantuan` (teks UI) tetap di C#: daemon hanya mengirim kunci pesan
dan argumennya, UI yang menerjemahkan.

## Uji setara

Untuk tiap modul yang menulis berkas: jalankan versi C# dan Rust pada sandbox
yang sama (`PHORON_HOME`), bandingkan berkas hasilnya byte demi byte. Fixture
di `daemon/setara/` dibangkitkan dari data demo, bukan data pengguna.

## Gerbang rilis

1. Semua uji Rust lulus, dan uji setara identik untuk semua profil demo.
2. Uji live (`build.bat live`) lulus dengan daemon Rust: Apache tiap kombinasi
   PHP, MySQL dari folder data kosong.
3. Uji asap UI lulus dengan UI sebagai klien pipe.
4. Dipakai harian minimal satu minggu tanpa regresi.

## Menjalankan uji

```powershell
cd daemon
cargo test                                   # uji unit Rust
powershell -ExecutionPolicy Bypass -File uji_setara.ps1   # setara dengan C#
```

Uji setara juga bisa dijalankan pada SALINAN data sungguhan: salin
`phoron.ini` dan `profiles\` ke folder sementara, setel
`PHORON_SETARA_NYATA` ke folder itu, lalu `cargo test --test setara`.

## Catatan perbedaan yang disengaja

- Urutan berkas profil meniru urutan budaya .NET secara perkiraan
  (`teks::urut_budaya`): tanda hubung diabaikan dulu, lalu tidak peka
  besar-kecil. Cukup untuk nama berkas buatan `Slug`.
- `char.IsLetterOrDigit` vs `is_alphabetic || is_numeric`: beda hanya untuk
  angka romawi/pecahan Unicode di nama profil.
- `cek_terakhir` berzona selain UTC ditulis ulang sebagai UTC (C# hanya
  pernah menulis UTC).
