<#
.SYNOPSIS
    Builds the application icon from a source image and installs it.

.DESCRIPTION
    Takes a square PNG and produces a multi-resolution .ico with the same frame
    set the original shipped with: 16, 20, 24, 32, 40, 48, 64, 128, 256.

    Writes it to Source\assets\magpietrove.ico and Source\app.ico, then
    regenerates the MSIX tiles from it.

        .\New-Icon.ps1 -Source ..\artwork\magpie.png

    Frames of 48 and below are stored as uncompressed 32-bit DIBs and larger
    ones as PNG, which is what icon tooling conventionally emits and what
    Explorer, the taskbar and the .NET apphost all handle without complaint.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Source,
    [switch] $SkipTiles
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function ConvertTo-Dib {
    param([System.Drawing.Bitmap] $Bitmap, [int] $Size)

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)

    # Each AND-mask row is padded to a 4-byte boundary.
    $maskStride = [int]([Math]::Floor(($Size + 31) / 32) * 4)

    # BITMAPINFOHEADER. biHeight is doubled because the structure nominally
    # covers the colour bitmap and the AND mask stacked on top of each other.
    $writer.Write([uint32] 40)
    $writer.Write([int32]  $Size)
    $writer.Write([int32]  ($Size * 2))
    $writer.Write([uint16] 1)
    $writer.Write([uint16] 32)
    $writer.Write([uint32] 0)          # BI_RGB, uncompressed
    $writer.Write([uint32] ($Size * $Size * 4 + $maskStride * $Size))
    $writer.Write([int32]  0)
    $writer.Write([int32]  0)
    $writer.Write([uint32] 0)
    $writer.Write([uint32] 0)

    # Colour data: BGRA, bottom-up.
    $rect = New-Object System.Drawing.Rectangle(0, 0, $Size, $Size)
    $data = $Bitmap.LockBits($rect,
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $row = New-Object byte[] ($Size * 4)
        for ($y = $Size - 1; $y -ge 0; $y--) {
            $rowStart = [IntPtr]::Add($data.Scan0, $y * $data.Stride)
            [System.Runtime.InteropServices.Marshal]::Copy($rowStart, $row, 0, $row.Length)
            $writer.Write($row)
        }
    }
    finally { $Bitmap.UnlockBits($data) }

    # AND mask: ignored for 32-bit icons, but the rows still have to be there.
    $writer.Write((New-Object byte[] ($maskStride * $Size)))

    $writer.Flush()
    $bytes = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()

    # The leading comma stops PowerShell unrolling the array on output. Without
    # it the caller receives 1128 loose objects, Length still reads 1128, and
    # BinaryWriter quietly matches a single-byte overload.
    return ,$bytes
}

$root    = $PSScriptRoot
$sizes   = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngFrom = 64

$original = [System.Drawing.Image]::FromFile((Resolve-Path $Source))
try {
    Write-Host "source: $($original.Width)x$($original.Height)" -ForegroundColor Cyan
    if ($original.Width -ne $original.Height) {
        Write-Warning "Source is not square; it will be squashed. Crop it first for best results."
    }

    $frames   = @{}
    $payloads = @{}
    foreach ($size in $sizes) {
        $bitmap   = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.InterpolationMode  = 'HighQualityBicubic'
            $graphics.PixelOffsetMode    = 'HighQuality'
            $graphics.SmoothingMode      = 'AntiAlias'
            $graphics.CompositingQuality = 'HighQuality'
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.DrawImage($original, 0, 0, $size, $size)
        }
        finally { $graphics.Dispose() }
        $frames[$size] = $bitmap

        if ($size -ge $pngFrom) {
            $ms = New-Object System.IO.MemoryStream
            $bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $payloads[$size] = $ms.ToArray()
            $ms.Dispose()
        }
        else {
            $payloads[$size] = ConvertTo-Dib $bitmap $size
        }
    }

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)

    $writer.Write([uint16] 0)               # reserved
    $writer.Write([uint16] 1)               # type: icon
    $writer.Write([uint16] $sizes.Count)

    # Image data begins after the directory, so offsets are known up front.
    $offset = 6 + (16 * $sizes.Count)
    foreach ($size in $sizes) {
        $dimension = if ($size -ge 256) { 0 } else { $size }   # 0 encodes 256
        $writer.Write([byte]   $dimension)
        $writer.Write([byte]   $dimension)
        $writer.Write([byte]   0)           # palette entries
        $writer.Write([byte]   0)           # reserved
        $writer.Write([uint16] 1)           # colour planes
        $writer.Write([uint16] 32)          # bits per pixel
        $writer.Write([uint32] $payloads[$size].Length)
        $writer.Write([uint32] $offset)
        $offset += $payloads[$size].Length
    }
    foreach ($size in $sizes) { $writer.Write([byte[]] $payloads[$size]) }

    $writer.Flush()
    $ico = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()

    foreach ($relative in 'assets\magpietrove.ico', 'app.ico') {
        $destination = Join-Path (Join-Path $root '..\Source') $relative
        $full = [System.IO.Path]::GetFullPath($destination)
        [System.IO.File]::WriteAllBytes($full, $ico)
        Write-Host ("wrote {0}  ({1:N1} KB, {2} frames)" -f $full, ($ico.Length / 1KB), $sizes.Count) -ForegroundColor Green
    }

    foreach ($bitmap in $frames.Values) { $bitmap.Dispose() }
}
finally { $original.Dispose() }

if (-not $SkipTiles) {
    Write-Host 'Regenerating MSIX tiles...' -ForegroundColor Cyan
    & (Join-Path $root 'New-Assets.ps1') | Out-Null
    Write-Host 'Tiles regenerated.' -ForegroundColor Green
}
