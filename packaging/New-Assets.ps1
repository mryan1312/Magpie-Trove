# Regenerates the MSIX visual assets from the application icon.
# Run this if assets/magpietrove.ico changes; the output is committed alongside the manifest.
[CmdletBinding()]
param(
    [string] $IconPath   = (Join-Path $PSScriptRoot '..\Source\assets\magpietrove.ico'),
    [string] $OutputPath = (Join-Path $PSScriptRoot 'Images'),
    # Matches BgColor in themes/Dark.xaml, so the wide and splash tiles sit on the app's own background.
    [string] $BackgroundHex = '#1B1B1F'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase, System.Drawing

New-Item -ItemType Directory -Force $OutputPath | Out-Null

# GDI+ cannot read this icon, so decode the 256x256 frame with WPF and hand the pixels to System.Drawing.
$stream  = [System.IO.File]::OpenRead((Resolve-Path $IconPath))
try {
    $decoder = New-Object System.Windows.Media.Imaging.IconBitmapDecoder(
        $stream,
        [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
        [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $frame = $decoder.Frames | Sort-Object PixelWidth -Descending | Select-Object -First 1

    $stride = $frame.PixelWidth * 4
    $pixels = New-Object byte[] ($stride * $frame.PixelHeight)
    $frame.CopyPixels($pixels, $stride, 0)

    $source = New-Object System.Drawing.Bitmap($frame.PixelWidth, $frame.PixelHeight,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $rect = New-Object System.Drawing.Rectangle(0, 0, $source.Width, $source.Height)
    $data = $source.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, $source.PixelFormat)
    [System.Runtime.InteropServices.Marshal]::Copy($pixels, 0, $data.Scan0, $pixels.Length)
    $source.UnlockBits($data)
}
finally { $stream.Close() }

$background = [System.Drawing.ColorTranslator]::FromHtml($BackgroundHex)

function Write-Tile {
    param([string] $Name, [int] $Width, [int] $Height, [switch] $Padded)

    $bitmap   = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.InterpolationMode = 'HighQualityBicubic'
        $graphics.PixelOffsetMode   = 'HighQuality'
        $graphics.SmoothingMode     = 'AntiAlias'

        # Square tiles bleed to the edge; wide and splash tiles get the icon centred on the app background.
        if ($Padded) {
            $graphics.Clear($background)
            $side = [Math]::Min($Width, $Height) * 0.55
        }
        else {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $side = [Math]::Min($Width, $Height)
        }

        $graphics.DrawImage($source, [single](($Width - $side) / 2), [single](($Height - $side) / 2), [single]$side, [single]$side)
    }
    finally { $graphics.Dispose() }

    $bitmap.Save((Join-Path $OutputPath $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

Write-Tile 'Square44x44Logo.png'                                  44   44
Write-Tile 'Square44x44Logo.scale-200.png'                        88   88
Write-Tile 'Square44x44Logo.targetsize-24_altform-unplated.png'   24   24
Write-Tile 'Square44x44Logo.targetsize-48_altform-unplated.png'   48   48
Write-Tile 'Square44x44Logo.targetsize-256_altform-unplated.png' 256  256
Write-Tile 'Square150x150Logo.png'                               150  150
Write-Tile 'Square150x150Logo.scale-200.png'                     300  300
Write-Tile 'Square310x310Logo.png'                               310  310
Write-Tile 'Square71x71Logo.png'                                  71   71
Write-Tile 'StoreLogo.png'                                        50   50
Write-Tile 'StoreLogo.scale-200.png'                             100  100
Write-Tile 'Wide310x150Logo.png'                                 310  150 -Padded
Write-Tile 'Wide310x150Logo.scale-200.png'                       620  300 -Padded
Write-Tile 'SplashScreen.png'                                    620  300 -Padded
Write-Tile 'SplashScreen.scale-200.png'                         1240  600 -Padded

$source.Dispose()

Get-ChildItem $OutputPath -Filter *.png |
    Select-Object Name, @{ n = 'KB'; e = { [math]::Round($_.Length / 1KB, 1) } } |
    Sort-Object Name |
    Format-Table -AutoSize
