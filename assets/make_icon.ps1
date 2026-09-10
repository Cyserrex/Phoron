# Membuat assets\phoron.ico dari assets\phoron-logo.png.
# Jalankan ulang setiap kali logonya diganti:  powershell -File assets\make_icon.ps1
Add-Type -AssemblyName System.Drawing

$source = Join-Path $PSScriptRoot 'phoron-logo.png'
$target = Join-Path $PSScriptRoot 'phoron.ico'
if (-not (Test-Path $source)) { throw "Logo tidak ditemukan: $source" }

$src = New-Object System.Drawing.Bitmap($source)

# --- Potong pinggiran yang sepenuhnya tembus pandang -------------------------
# Logo diekspor dengan margin kosong lebar. Kalau margin itu ikut terbawa, ikon
# di taskbar tampak jauh lebih kecil daripada ikon aplikasi lain di sebelahnya.
# Ambang 12 dipakai, bukan 0, supaya ekor bayangan yang nyaris tak terlihat
# tidak menahan batas potongan.
$minX = $src.Width; $minY = $src.Height; $maxX = -1; $maxY = -1
$rect = New-Object System.Drawing.Rectangle(0, 0, $src.Width, $src.Height)
$data = $src.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                      [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$bytes = New-Object byte[] ($data.Stride * $src.Height)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
$src.UnlockBits($data)
for ($y = 0; $y -lt $src.Height; $y++) {
    $row = $y * $data.Stride
    for ($x = 0; $x -lt $src.Width; $x++) {
        # Format32bppArgb tersimpan sebagai BGRA, jadi alfa ada di byte keempat.
        if ($bytes[$row + $x * 4 + 3] -gt 12) {
            if ($x -lt $minX) { $minX = $x }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }
}
if ($maxX -lt 0) { throw "Logo kosong seluruhnya." }

# --- Jadikan bujur sangkar --------------------------------------------------
# Ikon Windows selalu bujur sangkar; kalau potongan tadi tidak, isinya akan
# ditarik gepeng. Sisi terpanjang dipakai, sisanya diberi ruang seimbang.
$w = $maxX - $minX + 1
$h = $maxY - $minY + 1
$side = [Math]::Max($w, $h)
$offX = [int](($side - $w) / 2)
$offY = [int](($side - $h) / 2)

$square = New-Object System.Drawing.Bitmap($side, $side,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$gs = [System.Drawing.Graphics]::FromImage($square)
$gs.Clear([System.Drawing.Color]::Transparent)
$gs.DrawImage($src, (New-Object System.Drawing.Rectangle($offX, $offY, $w, $h)),
              (New-Object System.Drawing.Rectangle($minX, $minY, $w, $h)),
              [System.Drawing.GraphicsUnit]::Pixel)
$gs.Dispose()
$src.Dispose()
Write-Output "Potongan: ${w}x${h} -> bujur sangkar ${side}px"

# --- Perkecil ke tiap ukuran ------------------------------------------------
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.SmoothingMode = 'AntiAlias'
    $g.CompositingQuality = 'HighQuality'
    # Digambar via ImageAttributes dengan mode Tile: tanpa itu, bikubik menarik
    # piksel di luar tepi dan meninggalkan garis tembus pandang di keempat sisi.
    $attr = New-Object System.Drawing.Imaging.ImageAttributes
    $attr.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
    $g.DrawImage($square, (New-Object System.Drawing.Rectangle(0, 0, $s, $s)),
                 0, 0, $side, $side, [System.Drawing.GraphicsUnit]::Pixel, $attr)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $bmp.Dispose()
}
$square.Dispose()

# --- Rakit berkas .ico ------------------------------------------------------
# Header 6 byte, satu entri direktori 16 byte per ukuran, lalu data PNG-nya.
# PNG di dalam ICO didukung Windows Vista ke atas.
$out = New-Object System.IO.MemoryStream
$w2 = New-Object System.IO.BinaryWriter($out)
$w2.Write([UInt16]0); $w2.Write([UInt16]1); $w2.Write([UInt16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    # Ukuran 256 ditulis sebagai 0 - itu cara format ICO menyatakan 256.
    $b = [Byte]$(if ($s -ge 256) { 0 } else { $s })
    $w2.Write($b); $w2.Write($b)
    $w2.Write([Byte]0); $w2.Write([Byte]0)
    $w2.Write([UInt16]1); $w2.Write([UInt16]32)
    $w2.Write([UInt32]$pngs[$i].Length)
    $w2.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w2.Write($p) }
$w2.Flush()
[System.IO.File]::WriteAllBytes($target, $out.ToArray())
$w2.Dispose()
Write-Output "Ikon ditulis: $target ($((Get-Item $target).Length) bytes, $($sizes.Count) ukuran)"
