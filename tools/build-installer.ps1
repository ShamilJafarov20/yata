# Publishes Yata and yata-cli (framework-dependent, single file) and builds the Inno Setup installer into dist/.
# Usage: ./tools/build-installer.ps1 [-Version 1.0.0]
# The release workflow runs it in two steps so publish\Yata.exe can be code-signed in between:
#   -PublishOnly, then -SkipPublish.
param([string]$Version = "1.0.0", [switch]$PublishOnly, [switch]$SkipPublish)
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root "publish"

if (-not $SkipPublish) {
    dotnet publish (Join-Path $root "src\Yata\Yata.csproj") -c Release -r win-x64 --self-contained false `
        -p:PublishSingleFile=true -p:DebugType=none -p:Version=$Version -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
    # yata-cli.exe, the console side for scripts and agents, goes next to it.
    dotnet publish (Join-Path $root "src\Yata.Cli\Yata.Cli.csproj") -c Release -r win-x64 --self-contained false `
        -p:PublishSingleFile=true -p:DebugType=none -p:Version=$Version -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish (yata-cli) failed" }
    if ($PublishOnly) { return }
}

$iscc = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found (winget install JRSoftware.InnoSetup)" }

& $iscc /Q "/DAppVersion=$Version" "/DPublishDir=$publish" (Join-Path $root "installer\yata.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }

Get-ChildItem (Join-Path $root "dist") -Filter "Yata-Setup-$Version.exe"
