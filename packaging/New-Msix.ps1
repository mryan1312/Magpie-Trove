<#
.SYNOPSIS
    Publishes Magpie Trove and packs it into an MSIX.

.DESCRIPTION
    Produces out\Magpie Trove-<version>-x64.msix.

    For a Store submission, pass the three identity values from Partner Center
    (Product Management > Product identity) and do NOT sign — the Store signs
    the package itself:

        .\New-Msix.ps1 -IdentityName Meryndi.MagpieTrove `
                       -Publisher 'CN=XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX' `
                       -PublisherDisplayName 'Your Publisher Name'

    For a local install test, add -TestSign. That generates a self-signed
    certificate, signs the package, and tells you how to trust it. A test-signed
    package is for verification only; never ship one.

.NOTES
    Store versions must end in .0 — the Store reserves the fourth part.
#>
[CmdletBinding()]
param(
    [string] $IdentityName         = 'MagpieTrove',
    [string] $Publisher            = 'CN=Magpie Trove Test Certificate',
    [string] $PublisherDisplayName = 'Meryndi',
    [ValidatePattern('^\d+\.\d+\.\d+\.0$')]
    [string] $Version              = '1.0.0.0',
    [switch] $TestSign,
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'
$root      = $PSScriptRoot
$project   = Join-Path $root '..\Source\MagpieTrove.csproj'
$layout    = Join-Path $root 'layout'
$output    = Join-Path $root 'out'
$msix      = Join-Path $output "MagpieTrove-$Version-x64.msix"

function Find-SdkTool {
    param([string] $Name)
    $bin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $tool = Get-ChildItem $bin -Directory -ErrorAction SilentlyContinue |
        Where-Object Name -match '^\d+\.\d+\.\d+\.\d+$' |
        Sort-Object { [version] $_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$Name" } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1
    if (-not $tool) { throw "$Name not found. Install the Windows SDK: winget install Microsoft.WindowsSDK.10.0.26100" }
    $tool
}

$makeappx = Find-SdkTool 'makeappx.exe'
$signtool = Find-SdkTool 'signtool.exe'

# --- 1. Publish -------------------------------------------------------------
# Self-contained so the package carries its own runtime, but NOT single-file:
# MSIX is already a container, and single-file would just re-extract to temp
# on every launch.
$app = Join-Path $layout 'app'
if (-not $SkipPublish) {
    if (Test-Path $layout) { Remove-Item -Recurse -Force $layout }
    New-Item -ItemType Directory -Force $app | Out-Null

    Write-Host 'Publishing...' -ForegroundColor Cyan
    & dotnet publish $project -c Release -r win-x64 --self-contained true -o $app -v q -nologo -p:DebugType=none
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

    # Import libraries shipped by the ONNX Runtime package; link-time only, dead weight at runtime.
    Get-ChildItem $app -Include *.lib, *.pdb -Recurse | Remove-Item -Force
}
elseif (-not (Test-Path $app)) {
    throw "-SkipPublish was given but $app does not exist."
}

# --- 2. Assets and manifest -------------------------------------------------
Copy-Item (Join-Path $root 'Images') $layout -Recurse -Force

$manifest = Get-Content (Join-Path $root 'AppxManifest.template.xml') -Raw
$manifest = $manifest.
    Replace('{{IDENTITY_NAME}}',          $IdentityName).
    Replace('{{PUBLISHER}}',              $Publisher).
    Replace('{{PUBLISHER_DISPLAY_NAME}}', $PublisherDisplayName).
    Replace('{{VERSION}}',                $Version)
Set-Content (Join-Path $layout 'AppxManifest.xml') $manifest -Encoding UTF8

# --- 3. Pack ----------------------------------------------------------------
New-Item -ItemType Directory -Force $output | Out-Null
if (Test-Path $msix) { Remove-Item $msix }

Write-Host 'Packing...' -ForegroundColor Cyan
& $makeappx pack /d $layout /p $msix /o
if ($LASTEXITCODE -ne 0) { throw 'makeappx failed.' }

# --- 4. Optional test signing ----------------------------------------------
if ($TestSign) {
    if ($Publisher -notmatch 'Test Certificate') {
        throw 'Refusing to test-sign a package carrying real Store identity. Drop -TestSign, or use the default -Publisher.'
    }

    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $Publisher } | Select-Object -First 1
    if (-not $cert) {
        Write-Host 'Creating self-signed test certificate...' -ForegroundColor Cyan
        $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher `
            -KeyUsage DigitalSignature -FriendlyName 'Magpie Trove MSIX test' `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    }

    & $signtool sign /fd SHA256 /a /sha1 $cert.Thumbprint $msix
    if ($LASTEXITCODE -ne 0) { throw 'signtool failed.' }

    $cerPath = Join-Path $output 'MagpieTroveTest.cer'
    Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

    Write-Host ''
    Write-Host 'Test-signed. To install locally, from an ADMIN PowerShell:' -ForegroundColor Yellow
    Write-Host "  Import-Certificate -FilePath '$cerPath' -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
    Write-Host "  Add-AppxPackage '$msix'"
}

$size = [math]::Round((Get-Item $msix).Length / 1MB, 1)
Write-Host ''
Write-Host "Package: $msix  ($size MB)" -ForegroundColor Green
if (-not $TestSign) {
    Write-Host 'Unsigned, as the Store requires. Upload this file in Partner Center.' -ForegroundColor Green
}
