# Protokol UI <-> daemon (v2)

Sumber kebenaran: `daemon/inti/src/protokol.rs`. Dokumen ini ringkasannya
untuk sisi C#.

## Sambungan

- Named pipe `\\.\pipe\phoron`, dua arah, mode byte. Deskriptor keamanannya
  eksplisit: akses penuh hanya untuk SID pengguna yang menjalankan daemon,
  SYSTEM, dan Administrators (tanpa Everyone/anonymous), label integritas
  Medium. Jadi UI tanpa hak admin tetap bisa menyambung ke daemon yang
  ber-Administrator. Mutex `Phoron.Daemon` sama; sinyal
  `Phoron.KeluarSekarang` berlabel Low (seperti UI WPF lama).
- Satu pesan = satu baris JSON UTF-8 diakhiri `\n`. Baris > 4 MiB ditolak.
- Satu daemon melayani banyak klien sekaligus (UI, menu baki tidak perlu pipe).
- Pesan pertama klien WAJIB `halo`. Daemon menjawab `protokol_beda` bila
  versinya lain; UI lalu menampilkan pesan "Phoron perlu dipasang ulang".
- Klien yang tersambung tapi belum mengirim `halo` dalam 10 detik diputus.
- Pesan pertama selain `halo` dijawab `gagal` lalu sambungan diputus. Baris
  rusak SESUDAH `halo` dijawab `tidak_terbaca` (dengan `id` bila terbaca,
  selain itu `id` 0) dan sambungan tetap hidup.
- Klien yang berhenti membaca (antrean kabar penuh, atau satu tulisan tidak
  selesai dalam 5 detik) diputus; UI menyambung ulang lalu meminta `keadaan`.
- Sisi C#: buka pipe dengan `PipeOptions.Asynchronous`. Pada pegangan
  sinkron Windows menjalankan baca dan tulis satu per satu, jadi baca yang
  menunggu kabar akan menahan pengiriman permintaan.
- Nama uji: env `PHORON_NAMA_UJI` atau `phorond --pipe-uji <akhiran>` memberi
  akhiran pada pipe, mutex, dan sinyal keluar. Env `PHORON_PIPE` (atau
  `--pipe <nama>`) mengganti nama pipe utuh (tanpa `\\.\pipe\`); tanpa
  akhiran eksplisit, nama itu juga menurunkan akhiran mutex dan sinyal
  (`phoron-uji-x` -> `Phoron.Daemon-uji-x`), supaya daemon uji UI tidak
  bertabrakan dengan Phoron yang sedang dipakai.
- Perintah yang panik di dalam daemon dijawab `gagal` (kunci "Perintah {0}
  gagal karena galat tak terduga di Phoron. ..." dengan nama perintahnya);
  berkas `logs\crash-phorond-*.txt` ditulis dan daemon tetap hidup.

## UI -> daemon: Permintaan

```json
{"id":7,"perintah":"ganti_profil","data":{"file_name":"php-8-1-10-apache-2-4-54","restart":true}}
{"id":1,"perintah":"keadaan"}
```

`id` bilangan bulat buatan UI, naik terus. Perintah tanpa data tidak punya
kunci `data`.

| perintah | data | hasil (`hasil`) |
|---|---|---|
| `halo` | `{versi_ui, protokol: 2}` | `{versi_daemon, protokol}` |
| `keadaan` | - | `Keadaan` |
| `muat_ulang` | - | - (membaca ulang profil dan apps.ini, memindai ulang paket dan situs; phoron.ini TIDAK dibaca ulang - setelan di memori daemon sumber kebenaran) |
| `simpan_setelan` | `{setelan: Settings}` | - (`active_profile` dan `situs_terakhir` di dalamnya diabaikan) |
| `ubah_setelan` | `{perubahan: {medan: nilai, ...}}` hanya medan yang diubah | - (dipakai UI; mencegah menimpa perubahan dari menu baki). Menyebut `active_profile` = `gagal` "Profil aktif diganti lewat Ganti profil, bukan lewat setelan."; `situs_terakhir` juga ditolak (pakai `catat_situs_dibuka`) |
| `mulai_ulang_admin` | - | - (daemon baru ber-Administrator; layanan hidup dinyalakan lagi; lihat Siklus hidup). UAC dibatalkan = `gagal` "Hak Administrator tidak diberikan." |
| `terapkan` | - | - (peringatan di `peringatan`) |
| `ganti_profil` | `{file_name, restart}` | - |
| `nyalakan_semua` / `matikan_semua` | - | - |
| `nyalakan_web` / `matikan_web` / `nyalakan_db` / `matikan_db` | - | - |
| `jalankan_node` | `{path, port_sekali}` (0 = biasa) | URL (string) atau null |
| `hentikan_node` | `{path}` | - |
| `hentikan_semua_node` | - | - |
| `riwayat_node` | `{path}` | `["baris", ...]` |
| `riwayat_log` | - | `[BarisLogJarak, ...]` |
| `sisa_proses` | - | `[Usage, ...]` |
| `hentikan_sisa` | - | - (yang gagal di `peringatan`) |
| `catat_situs_dibuka` | `{path}` | - |
| `catat` | `{pesan: Pesan}` | - |
| `pulihkan_hosts` | `{nama}` nama berkas cadangan di `data\hosts-backup` (bukan jalur) | - . Tidak ber-Administrator = `perlu_admin` |
| `bersihkan_hosts` | - | - (blok Phoron dibuang; baris lain tetap) |
| `daftarkan_hosts` | - | daftar nama situs semua profil yang ditulis |
| `buat_sertifikat` | - | - (etc\ssl untuk akhiran profil aktif; config ditulis lewat `terapkan` sesudahnya) |
| `percayai_sertifikat` | - | - (CA ke Trusted Root; tidak ber-Administrator = `perlu_admin`) |
| `keluar` | - | - lalu kabar `pamit` |

## daemon -> UI

Jawaban (punya `id`):

```json
{"id":7,"status":"ok","hasil":{...},"peringatan":[{"kunci":"...","args":["..."]}]}
{"id":7,"status":"galat","galat":{"jenis":"gagal","pesan":{"kunci":"...","args":[]}}}
```

`galat.jenis`: `belum_ada {perintah}`, `protokol_beda {daemon, ui}`,
`tidak_terbaca {sebab}`, `gagal {pesan}`, `perlu_admin {pesan}` (aksi hosts/
sertifikat butuh Administrator; UI menawarkan `mulai_ulang_admin`).

Kabar (tanpa `id`, dikirim ke semua klien):

```json
{"kabar":"status","data":{"web":"Jalan","db":"Berhenti"}}
{"kabar":"log","data":{"waktu":"2026-11-27T07:50:29.1234567Z","pesan":{"kunci":"..."}}}
{"kabar":"node","data":{"path":"D:\\web\\toko","berjalan":true,"url":"http://localhost:3000"}}
{"kabar":"node_keluaran","data":{"path":"D:\\web\\toko","baris":"ready on ..."}}
{"kabar":"perlu_muat_ulang"}
{"kabar":"pamit"}
```

Bedakan jawaban dan kabar dari ada tidaknya kunci `id`.

`node_keluaran` punya `pesan` opsional untuk baris buatan Phoron sendiri
("-- dihentikan --", galat keluar, peringatan HSTS). Bila ada, tampilkan
`Lang.T(pesan.kunci, pesan.args)`; bila tidak, `baris` apa adanya.

## Bentuk data

- `Pesan`: `{kunci, args?}`. `kunci` = teks templat Indonesia persis seperti
  argumen `Lang.T` di C#; UI memanggil `Lang.T(kunci, args)`.
- `ServiceState`: `"Berhenti" | "Menyalakan" | "Jalan" | "Mematikan" | "Gagal"`.
- `StatusLayanan`: `{web, db, galat_web?: Pesan, galat_db?: Pesan}`.
- `StatusNode`: `{path, berjalan, url?, port_bentrok?}`.
- `Keadaan`: `{status, profil_aktif, node: [StatusNode], perlu_restart_web,
  perlu_restart_db, peringatan_terapkan?: [Pesan], masalah_muat?: [Pesan], admin}`.
- `Usage`: `{port, in_use, pid, process_name, jalur}`.
- `Settings` (nama kunci = nama medan Rust): `bin_roots, situs_terakhir,
  active_profile, auto_start_services, minimize_to_tray, auto_vhost, opcache,
  manage_hosts, php_ini_ke_folder_php, log_rinci, beranda_di_akar, tema,
  jendela_lebar, jendela_tinggi, bahasa, cek_pembaruan, cek_terakhir
  ({detik, tick} atau null), terminal, editor, db_pengguna, heidi_sql, db_sandi`.

## Siapa menulis berkas apa

| Berkas | Penulis |
|---|---|
| `phoron.ini` | daemon saja (UI mengirim `simpan_setelan`) |
| `profiles\*.ini`, `apps.ini` | UI, lalu `muat_ulang` |
| `etc\...` (config), hosts, sertifikat | daemon saja |
| `logs\...` | daemon |

## Siklus hidup

- `phorond.exe` memegang ikon baki dan layanan; hidup sepanjang hari. Mutex
  `Phoron.Daemon`; event bernama `Phoron.KeluarSekarang` (AUTO-reset, sama
  seperti UI WPF lama; dipakai pemasang) membuatnya keluar dengan rapi.
- Dalam mode daemon UI TIDAK menunggu `Phoron.KeluarSekarang` (event
  auto-reset hanya membangunkan satu penunggu). Daemon yang menunggunya; UI
  ikut keluar karena menerima kabar `pamit`.
- Jalur keluar tunggal (perintah `keluar`, menu Keluar, sinyal pemasang,
  `taskkill` tanpa `/f` = WM_CLOSE, akhir sesi Windows, panik di utas utama):
  gerbang ditutup -> `sebelum_keluar` (`Engine::tutup`: start dan ganti
  profil baru ditolak, start yang berjalan dibatalkan dan ditunggu berbenah
  paling lama 60 detik, lalu layanan dimatikan - MySQL dengan rapi) -> kabar
  `pamit` -> antrean klien dikuras (maks 2 detik) -> ikon baki dihapus ->
  proses berakhir. Begitu gerbang ditutup, perintah baru (termasuk `halo`
  dari sambungan baru) dijawab `gagal` "Phoron sedang ditutup." dan butir
  menu baki diabaikan.
- Kode keluar phorond: 0 rapi, 2 pipe tidak bisa dibuat, 101 sesudah panik
  yang tidak tertangkap (tetap lewat jalur keluar rapi bila sempat).
- `mulai_ulang_admin`: daemon menanyakan layanan yang hidup ke Engine, lalu
  menjalankan phorond.exe yang sama lewat UAC (`runas`) dengan
  `--baki --tunggu-pid <pid lama> --nyalakan <a|b|..>` (plus `--akar`,
  `--pipe-uji`, `--pipe` bila dipakai; env tidak ikut ke proses yang
  dinaikkan haknya). Berhasil: jawab ok, lalu jalur keluar TANPA `pamit`;
  UI melihat sambungan putus biasa dan menyambung ulang. Daemon baru menunggu
  proses lama berakhir (maks 30 detik) sebelum mengambil mutex, lalu
  menyalakan lagi daftar itu sesudah pipenya siap.
- `phorond.exe --sinkron-hosts [<akar>]`: pembantu ber-Administrator, padanan
  `Phoron.exe --sinkron-hosts`. Menulis blok hosts semua situs lalu keluar
  dengan kode 0 (berhasil), 2 (tidak berhak), 3 (galat). Tanpa mutex, baki,
  maupun pipe.
- `Phoron.exe` (WPF) hanya hidup saat jendela terbuka. Saat mulai: sambung ke
  pipe; bila tidak ada daemon, jalankan `phorond.exe` dari folder yang sama dan
  tunggu pipe-nya (maks 10 detik). Menutup jendela = UI keluar; daemon tetap.
- Menu baki "Buka Phoron" menjalankan `Phoron.exe` (atau memajukan jendelanya).
- Autostart Windows menunjuk `phorond.exe --baki`.
