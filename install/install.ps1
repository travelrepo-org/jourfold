# Install or update Jourfold for the current user on Windows.
#
#   irm https://github.com/travelrepo-org/jourfold/releases/latest/download/install.ps1 | iex
#
# With options:
#   & ([scriptblock]::Create((irm https://github.com/travelrepo-org/jourfold/releases/latest/download/install.ps1))) -Version v0.2.0
#   & ([scriptblock]::Create((irm https://github.com/travelrepo-org/jourfold/releases/latest/download/install.ps1))) -Uninstall
#
# Environment variables JOURFOLD_REPO, JOURFOLD_VERSION, JOURFOLD_DOWNLOAD_BASE and JOURFOLD_ARCH work as well,
# which is convenient with `irm ... | iex`. No administrator rights are needed.
param(
    [string]$Version = $(if ($env:JOURFOLD_VERSION) { $env:JOURFOLD_VERSION } else { 'latest' }),
    [string]$Repo = $env:JOURFOLD_REPO,
    [string]$DownloadBase = $env:JOURFOLD_DOWNLOAD_BASE,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Release builds replace this marker with the repository that published them.
$DefaultRepo = '@JOURFOLD_REPOSITORY@'
$UninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{A4F1077F-857B-4CFA-8BCB-A12F80773B56}_is1'

function Fail([string]$Message) {
    Write-Host "Jourfold installer: $Message" -ForegroundColor Red
    throw $Message
}

# x64 or arm64. The system environment in the registry names the native processor even when this PowerShell
# runs under x64 emulation on an ARM64 PC. JOURFOLD_ARCH=x64 or arm64 overrides the detection.
function Get-Architecture {
    if ($env:JOURFOLD_ARCH -in 'x64', 'arm64') { return $env:JOURFOLD_ARCH }
    $names = @($env:PROCESSOR_ARCHITECTURE, $env:PROCESSOR_ARCHITEW6432)
    $names += (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment' -Name PROCESSOR_ARCHITECTURE -ErrorAction SilentlyContinue).PROCESSOR_ARCHITECTURE
    if ($names -contains 'ARM64') { return 'arm64' }
    return 'x64'
}

function Install-Jourfold {
    if ($Uninstall) { Uninstall-Jourfold; return }

    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { Fail 'this installer is for Windows. On Linux use install.sh.' }
    if (-not [Environment]::Is64BitOperatingSystem) { Fail 'Jourfold builds are available for 64-bit Windows only.' }
    $Setup = "jourfold-setup-win-$(Get-Architecture).exe"
    if ([Environment]::OSVersion.Version.Major -lt 10) { Fail 'Jourfold needs Windows 10 or Windows 11.' }
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

    if ($DownloadBase) {
        $base = $DownloadBase.TrimEnd('/')
    }
    else {
        $name = if ($Repo) { $Repo } else { $DefaultRepo }
        if ($name.StartsWith('@') -or -not $name) {
            Fail 'this copy of the installer does not name a repository. Use the install.ps1 attached to a Jourfold release, or set JOURFOLD_REPO=owner/name.'
        }
        if ($name -notmatch '^[^/\s]+/[^/\s]+$') { Fail "JOURFOLD_REPO must look like owner/name, not '$name'." }
        $base = if ($Version -eq 'latest') { "https://github.com/$name/releases/latest/download" } else { "https://github.com/$name/releases/download/$Version" }
    }

    $temp = Join-Path ([IO.Path]::GetTempPath()) ('jourfold-install-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temp | Out-Null
    try {
        Write-Host "Downloading Jourfold ($Version)..."
        $installer = Join-Path $temp $Setup
        $sums = Join-Path $temp 'SHA256SUMS'
        Invoke-WebRequest -UseBasicParsing -Uri "$base/$Setup" -OutFile $installer
        Invoke-WebRequest -UseBasicParsing -Uri "$base/SHA256SUMS" -OutFile $sums

        Write-Host 'Verifying the download...'
        $line = Get-Content $sums | Where-Object { $_ -match "^\s*([0-9a-fA-F]{64})\s+\*?$([regex]::Escape($Setup))\s*$" } | Select-Object -First 1
        if (-not $line) { Fail "SHA256SUMS does not list $Setup." }
        $expected = ($line -split '\s+')[0].ToLowerInvariant()
        $actual = (Get-FileHash -Algorithm SHA256 -Path $installer).Hash.ToLowerInvariant()
        if ($expected -ne $actual) { Fail 'checksum mismatch. The download is incomplete or was altered; nothing was installed.' }

        Write-Host 'Installing...'
        $process = Start-Process -FilePath $installer -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CLOSEAPPLICATIONS' -Wait -PassThru
        if ($process.ExitCode -ne 0) { Fail "the installer exited with code $($process.ExitCode)." }

        $location = (Get-ItemProperty -Path $UninstallKey -ErrorAction SilentlyContinue).InstallLocation
        Write-Host ''
        Write-Host 'Jourfold is installed. Start it from the Start menu.' -ForegroundColor Green
        if ($location) { Write-Host "Location: $location" }
        Write-Host 'Git for Windows is included, so no separate Git installation is needed.'
    }
    finally {
        Remove-Item -Recurse -Force -Path $temp -ErrorAction SilentlyContinue
    }
}

function Uninstall-Jourfold {
    $entry = Get-ItemProperty -Path $UninstallKey -ErrorAction SilentlyContinue
    if (-not $entry) { Write-Host 'Jourfold is not installed for this user.'; return }
    $uninstaller = ($entry.UninstallString -replace '^"|"$', '')
    if (-not (Test-Path $uninstaller)) { Fail "the uninstaller was not found at $uninstaller." }
    $process = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait -PassThru
    if ($process.ExitCode -ne 0) { Fail "the uninstaller exited with code $($process.ExitCode)." }
    Write-Host 'Jourfold was removed. Your trip folders and settings were not touched.'
}

Install-Jourfold
