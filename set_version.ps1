# Menaikkan nomor versi Phoron di SEMUA berkas yang menyebutnya, sekaligus.
#
# Nomor versi tersebar di enam berkas, dan CI menolak build kalau ada satu yang
# tertinggal - bukan karena rewel, tapi karena rilis diterbitkan berdasarkan
# angka di Models.cs: satu berkas yang tidak ikut naik menghasilkan rilis
# "v1.1.0" berisi installer bernama 1.0.0, dan itu baru ketahuan setelah orang
# mengunduhnya.
#
# Pakai:  .\set_version.ps1 1.1.0
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# Tiap entri: berkas, pola yang dicari, dan penggantinya. Pola sengaja mengikat
# konteksnya (nama properti/konstanta), bukan angka versi telanjang - di
# csproj ada nomor versi lain (WPF-UI) yang tidak boleh ikut berubah.
$edits = @(
    @{ File = 'src\Phoron.Core\Models.cs'
       Pattern = '(public const string Version = ")\d+\.\d+\.\d+(")'
       Replace = "`${1}$Version`${2}" }
    @{ File = 'src\Phoron.Core\Phoron.Core.csproj'
       Pattern = '(<Version>)\d+\.\d+\.\d+(</Version>)'
       Replace = "`${1}$Version`${2}" }
    @{ File = 'src\Phoron.App\Phoron.App.csproj'
       Pattern = '(<Version>)\d+\.\d+\.\d+(</Version>)'
       Replace = "`${1}$Version`${2}" }
    @{ File = 'src\Phoron.Tests\Phoron.Tests.csproj'
       Pattern = '(<Version>)\d+\.\d+\.\d+(</Version>)'
       Replace = "`${1}$Version`${2}" }
    @{ File = 'installer\setup.iss'
       Pattern = '(#define MyAppVersion ")\d+\.\d+\.\d+(")'
       Replace = "`${1}$Version`${2}" }
    @{ File = 'build_installer.bat'
       Pattern = '(set "VERSION=)\d+\.\d+\.\d+(")'
       Replace = "`${1}$Version`${2}" }
)

foreach ($e in $edits) {
    $path = Join-Path $root $e.File
    if (-not (Test-Path $path)) { throw "Berkas tidak ada: $($e.File)" }
    $text = Get-Content $path -Raw
    if ($text -notmatch $e.Pattern) { throw "Pola versi tidak ditemukan di $($e.File)" }
    $baru = [regex]::Replace($text, $e.Pattern, $e.Replace)
    if ($baru -ne $text) {
        # Tanpa BOM: berkas sumber dibaca juga oleh compiler dan ISCC.
        [System.IO.File]::WriteAllText($path, $baru, (New-Object System.Text.UTF8Encoding($false)))
        Write-Output "  diperbarui  $($e.File)"
    } else {
        Write-Output "  sudah $Version  $($e.File)"
    }
}

Write-Output ""
Write-Output "Versi Phoron sekarang $Version."
Write-Output "Commit lalu push ke main - CI akan menerbitkan rilis v$Version sendiri."
