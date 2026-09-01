<#
.SYNOPSIS
    Builds the portable zip release: one self-contained MagpieTrove.exe plus README.

.DESCRIPTION
    Output: ..\dist\MagpieTrove-<version>-win-x64.zip

    This is the sideload channel, for handing someone a file directly. It is
    unsigned, so Smart App Control will refuse to run it on machines that have
    SAC enabled (clean Windows 11 installs). The Store package is the channel
    that solves that — see PUBLISHING.md.

    Unlike the MSIX build this one IS single-file, since there is no package
    container to do the job.
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$root    = $PSScriptRoot
$project = Join-Path $root '..\Source\MagpieTrove.csproj'
$stage   = Join-Path $root 'zip-stage'
$dist    = Join-Path $root '..\dist'
$zip     = Join-Path $dist "MagpieTrove-$Version-win-x64.zip"

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null

Write-Host 'Publishing single-file...' -ForegroundColor Cyan
& dotnet publish $project -c Release -r win-x64 --self-contained true -o $stage -v q -nologo `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

# Import libraries from the ONNX Runtime package; link-time only, dead weight here.
Get-ChildItem $stage -Include *.lib, *.pdb -Recurse | Remove-Item -Force

Copy-Item (Join-Path $root 'README.zip.txt') (Join-Path $stage 'README.txt')

New-Item -ItemType Directory -Force $dist | Out-Null
if (Test-Path $zip) { Remove-Item $zip }

Compress-Archive -Path (Get-ChildItem $stage | ForEach-Object { $_.FullName }) `
                 -DestinationPath $zip -CompressionLevel Optimal

Remove-Item -Recurse -Force $stage

$item = Get-Item $zip
Write-Host ''
Write-Host "Zip:    $($item.FullName)  ($([math]::Round($item.Length / 1MB, 1)) MB)" -ForegroundColor Green
Write-Host "SHA256: $((Get-FileHash $zip -Algorithm SHA256).Hash)" -ForegroundColor Green
