# Protokol UI <-> daemon (v2)

Sumber kebenaran: `daemon/inti/src/protokol.rs`. Dokumen ini ringkasannya
untuk sisi C#.

## Sambungan

- Named pipe `\\.\pipe\phoron`, dua arah, mode byte. Hanya pengguna Windows
  yang sama yang bisa membukanya (DACL bawaan pembuat pipe).
- Satu pesan = satu baris JSON UTF-8 diakhiri `\n`. Baris > 4 MiB ditolak.
- Satu daemon melayani banyak klien sekaligus (UI, menu baki tidak perlu pipe).
- Pesan pertama klien WAJIB `halo`. Daemon menjawab `protokol_beda` bila
  versinya lain; UI lalu menampilkan pesan "Phoron perlu dipasang ulang".
- Pesan pertama selain `halo` dijawab `gagal` lalu sambungan diputus. Baris
  rusak SESUDAH `halo` dijawab `tidak_terbaca` (dengan `id` bila terbaca,
  selain itu `id` 0) dan sambungan tetap hidup.
- Klien yang berhenti membaca (antrean kabar penuh, atau satu tulisan tidak
  selesai dalam 5 detik) diputus; UI menyambung ulang lalu meminta `keadaan`.
- Sisi C#: buka pipe dengan `PipeOptions.Asynchronous`. Pada pegangan
  sinkron Windows menjalankan baca dan tulis satu per satu, jadi baca yang
  menunggu kabar akan menahan pengiriman permintaan.
- Nama uji: env `PHORON_NAMA_UJI` atau `phorond --pipe-uji <akhiran>` memberi
  akhiran pada pipe, mutex, dan sinyal keluar.

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
| `muat_ulang` | - | - |
| `simpan_setelan` | `{setelan: Settings}` | - |
| `ubah_setelan` | `{perubahan: {medan: nilai, ...}}` hanya medan yang diubah | - (dipakai UI; mencegah menimpa perubahan dari menu baki) |
| `mulai_ulang_admin` | - | - (daemon baru ber-Administrator; layanan hidup dinyalakan lagi) |
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
| `keluar` | - | - lalu kabar `pamit` |

## daemon -> UI

Jawaban (punya `id`):

```json
{"id":7,"status":"ok","hasil":{...},"peringatan":[{"kunci":"...","args":["..."]}]}
{"id":7,"status":"galat","galat":{"jenis":"gagal","pesan":{"kunci":"...","args":[]}}}
```

`galat.jenis`: `belum_ada {perintah}`, `protokol_beda {daemon, ui}`,
`tidak_terbaca {sebab}`, `gagal {pesan}`.

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
  `Phoron.Daemon`; sinyal bernama `Phoron.KeluarSekarang` (dipakai pemasang)
  membuatnya keluar dengan rapi.
- `Phoron.exe` (WPF) hanya hidup saat jendela terbuka. Saat mulai: sambung ke
  pipe; bila tidak ada daemon, jalankan `phorond.exe` dari folder yang sama dan
  tunggu pipe-nya (maks 10 detik). Menutup jendela = UI keluar; daemon tetap.
- Menu baki "Buka Phoron" menjalankan `Phoron.exe` (atau memajukan jendelanya).
- Autostart Windows menunjuk `phorond.exe --baki`.
