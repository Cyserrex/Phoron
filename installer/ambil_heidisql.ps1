# ============================================================
#  Mengambil HeidiSQL portabel untuk disertakan ke installer Phoron.
#
#  KENAPA DIUNDUH, BUKAN DISIMPAN DI REPO. HeidiSQL 28 MB dan bukan milik
#  Phoron. Menyimpan biner orang lain di dalam riwayat git membuatnya ikut
#  terbawa selamanya di setiap klon, dan memperbaruinya berarti menambah 28 MB
#  lagi tiap kali - riwayatnya membengkak tanpa pernah bisa disusutkan.
#
#  VERSINYA DIPATOK, DAN SIDIKNYA DIPERIKSA. Mengunduh "yang terbaru" saat
#  build berarti isi installer berubah tanpa ada yang memutuskannya, dan berkas
#  yang tertukar di tengah jalan tidak akan ketahuan. Sidik di bawah diambil
#  dari dua tempat yang sepakat: API rilis GitHub milik HeidiSQL, dan halaman
#  unduhan di heidisql.com.
#
#  LISENSI. HeidiSQL berlisensi GPL-2.0. Menyertakannya bersama Phoron adalah
#  penggabungan dua program terpisah - Phoron menjalankannya sebagai proses
#  lain, tidak menautnya - jadi lisensi Phoron sendiri tidak ikut berubah.
#  Yang wajib: teks lisensinya ikut dipasang, dan sumbernya disebutkan. Berkas
#  gpl.txt sudah ada di dalam paket portabelnya, dan alamat sumbernya ditulis
#  ke HeidiSQL-SUMBER.txt di sebelahnya.
# ============================================================
[CmdletBinding()]
param(
    # Folder tujuan; isinya dipakai langsung oleh [Files] di setup.iss.
    [string]$Tujuan = (Join-Path $PSScriptRoot 'heidisql')
)

$ErrorActionPreference = 'Stop'

$Versi  = '12.21'
$Berkas = "HeidiSQL_${Versi}_64_Portable.zip"
$Url    = "https://github.com/HeidiSQL/HeidiSQL/releases/download/v$Versi/$Berkas"
$Sidik  = 'fecb76a69e29a53ea05b1d57fc2f7b7aaed5b8f889556c6eca545e2a800df1ab'
$Sumber = "https://github.com/HeidiSQL/HeidiSQL/tree/v$Versi"

# Penanda: kalau folder tujuan sudah berisi versi yang sama, tidak perlu
# mengunduh ulang. Build lokal yang dijalankan berkali-kali tidak boleh
# menghabiskan 28 MB tiap kali.
$Penanda = Join-Path $Tujuan '.versi'
if ((Test-Path $Penanda) -and ((Get-Content $Penanda -Raw).Trim() -eq $Versi) `
    -and (Test-Path (Join-Path $Tujuan 'heidisql.exe'))) {
    Write-Host "HeidiSQL $Versi sudah ada di $Tujuan - tidak diunduh ulang."
    exit 0
}

$tmp = Join-Path $env:TEMP "phoron-heidi-$([Guid]::NewGuid().ToString('N').Substring(0,8))"
New-Item -ItemType Directory -Path $tmp -Force | Out-Null
$zip = Join-Path $tmp $Berkas

try {
    Write-Host "Mengunduh HeidiSQL $Versi ..."
    # TLS 1.2 disebut eksplisit: Windows PowerShell 5.1 di runner lama masih
    # berunding dengan TLS 1.0 lebih dulu, dan GitHub menolaknya.
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $dulu = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'   # tanpa ini unduhan jadi sangat lambat
    Invoke-WebRequest -Uri $Url -OutFile $zip -UseBasicParsing
    $ProgressPreference = $dulu

    $nyata = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($nyata -ne $Sidik) {
        throw ("Sidik SHA-256 tidak cocok." + [Environment]::NewLine +
               "  diharapkan : $Sidik" + [Environment]::NewLine +
               "  didapat    : $nyata" + [Environment]::NewLine +
               "Berkasnya TIDAK dipakai.")
    }
    Write-Host "Sidik cocok: $nyata"

    if (Test-Path $Tujuan) { Remove-Item $Tujuan -Recurse -Force }
    New-Item -ItemType Directory -Path $Tujuan -Force | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $Tujuan)

    # Paket portabelnya kadang berisi satu folder di dalamnya, kadang langsung
    # berisi berkasnya. Yang dituju: heidisql.exe tepat di akar $Tujuan.
    if (-not (Test-Path (Join-Path $Tujuan 'heidisql.exe'))) {
        $dalam = Get-ChildItem $Tujuan -Directory | Select-Object -First 1
        if ($dalam -and (Test-Path (Join-Path $dalam.FullName 'heidisql.exe'))) {
            Get-ChildItem $dalam.FullName -Force | Move-Item -Destination $Tujuan -Force
            Remove-Item $dalam.FullName -Recurse -Force
        }
    }
    if (-not (Test-Path (Join-Path $Tujuan 'heidisql.exe'))) {
        throw "heidisql.exe tidak ketemu di dalam paket yang diunduh."
    }

    # Kewajiban GPL: sebutkan dari mana sumbernya bisa diambil.
    $catatan = @"
HeidiSQL $Versi
Berlisensi GPL-2.0. Lihat gpl.txt di folder ini.

HeidiSQL adalah program TERSENDIRI, bukan bagian dari Phoron. Phoron
menjalankannya sebagai proses lain dan tidak menautnya ke dalam dirinya.

Kode sumber versi ini:
  $Sumber

Berkas yang disertakan diambil dari:
  $Url
  SHA-256: $Sidik
"@
    Set-Content -Path (Join-Path $Tujuan 'HeidiSQL-SUMBER.txt') -Value $catatan -Encoding UTF8
    Set-Content -Path $Penanda -Value $Versi -Encoding ASCII

    $n = (Get-ChildItem $Tujuan -Recurse -File).Count
    $mb = [math]::Round(((Get-ChildItem $Tujuan -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
    Write-Host "HeidiSQL siap di $Tujuan ($n berkas, $mb MB)."
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
