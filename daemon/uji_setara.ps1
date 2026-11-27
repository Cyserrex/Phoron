# Uji setara Core C# dan Core Rust.
#
# 1. Phoron.Tests membangkitkan berkas masukan dan menyimpan ulang dengan Core C#.
# 2. cargo test menjalankan Core Rust pada masukan yang sama dan menuntut
#    keluaran yang identik byte demi byte.
#
# Pakai:  powershell -ExecutionPolicy Bypass -File daemon\uji_setara.ps1
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$bahan = Join-Path ([IO.Path]::GetTempPath()) ("phoron-setara-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))

Write-Host "== Membangkitkan bahan dengan Core C# =="
dotnet build (Join-Path $repo 'src\Phoron.Tests') -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "build Phoron.Tests gagal ($LASTEXITCODE)" }
& (Join-Path $repo 'src\Phoron.Tests\bin\Debug\net48\Phoron.Tests.exe') setara $bahan
if ($LASTEXITCODE -ne 0) { throw "Phoron.Tests setara gagal ($LASTEXITCODE)" }

Write-Host "== Menguji Core Rust =="
$env:PHORON_SETARA = $bahan
Push-Location $PSScriptRoot
try {
    cargo test --test setara '--' --nocapture
    $kode = $LASTEXITCODE
}
finally {
    Pop-Location
    Remove-Item Env:\PHORON_SETARA
}
if ($kode -eq 0) { Remove-Item -Recurse -Force $bahan }
else { Write-Host "Bahan disimpan untuk diperiksa: $bahan" }
exit $kode
