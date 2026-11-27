# Uji setara satu fitur: Core C# membangkitkan bahan dan keluarannya, lalu
# Core Rust harus menghasilkan hasil yang sama.
#
#   C#   : src\Phoron.Tests\UjiSetara<Nama>.cs, kelas UjiSetara_<nama>,
#          metode statis int Jalankan(string folder)
#   Rust : daemon\inti\tests\setara_<nama>.rs, membaca folder dari
#          variabel PHORON_SETARA_<NAMA> (huruf besar); dilewati bila tidak ada.
#
# Pakai:  powershell -ExecutionPolicy Bypass -File daemon\uji_setara_modul.ps1 config
param([Parameter(Mandatory = $true)][string]$Nama)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$bahan = Join-Path ([IO.Path]::GetTempPath()) ("phoron-setara-$Nama-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))

dotnet build (Join-Path $repo 'src\Phoron.Tests') -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "build Phoron.Tests gagal ($LASTEXITCODE)" }
& (Join-Path $repo 'src\Phoron.Tests\bin\Debug\net48\Phoron.Tests.exe') setara-modul $Nama $bahan
if ($LASTEXITCODE -ne 0) { throw "Phoron.Tests setara-modul $Nama gagal ($LASTEXITCODE)" }

$var = "PHORON_SETARA_" + $Nama.ToUpperInvariant()
Set-Item "Env:$var" $bahan
Push-Location $PSScriptRoot
try {
    cargo test --test "setara_$Nama" '--' --nocapture
    $kode = $LASTEXITCODE
}
finally {
    Pop-Location
    Remove-Item "Env:$var"
}
if ($kode -eq 0) { Remove-Item -Recurse -Force $bahan }
else { Write-Host "Bahan disimpan untuk diperiksa: $bahan" }
exit $kode
