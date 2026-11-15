# Screenshots of the notch for the site and the README (Yata.exe --bench vitrine --shots): Yata goes
# through its states with made-up content (a song with lyrics, a day of meetings, tasks, a week of
# water and focus, files on the shelf, a build in the bubble) and draws each one to a PNG itself,
# with a transparent background and at twice the size. Once per language, in a throwaway profile
# (YATA_DATA_DIR), like tools/measure.ps1; a running Yata is closed and reopened. The song cover is
# cut from docs/yata-hero.png, so no image needs a license.
#
# Usage: ./tools/make-shots.ps1 [-Languages en,pt-BR] [-OutDir docs\screenshots] [-SiteDir site\img\shots] [-Exe path\Yata.exe]
#   Writes <OutDir>\<language>\<state>.png, e.g. docs\screenshots\en\aberto-foco.png, and the
#   ones the site shows, made lighter, in <SiteDir>\<en|pt>\. The README is English only, so
#   pt-BR (for the site) is drawn only when asked for.
param(
    [string[]]$Languages = @("en"),
    [string]$OutDir,
    [string]$SiteDir,
    [string]$Exe
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
if (-not $OutDir) { $OutDir = Join-Path $root "docs\screenshots" }
if (-not $SiteDir) { $SiteDir = Join-Path $root "site\img\shots" }

# What the site shows (site/index.html), and how wide: the big shot at twice its 554 px, the ones in
# the feature cards at twice ~320 px, and the closed notch in the hero at its full size (2x), since
# it's shown there as big as Yata draws it.
$siteShots = [ordered]@{
    "aberto-musica" = 1108; "aberto-agenda" = 640; "aberto-foco" = 640; "aberto-tarefas" = 640
    "aberto-prateleira" = 640; "fechado-volume" = 640
    "fechado-relogio" = 1000; "fechado-reuniao" = 1000; "fechado-musica" = 1000; "fechado-agente-aviso" = 1000
    "fechado-fora" = 1000
}

function Save-Smaller([string]$from, [string]$to, [int]$width) {
    $image = [Drawing.Bitmap]::FromFile($from)
    $height = [int][math]::Round($image.Height * $width / $image.Width)
    $small = New-Object Drawing.Bitmap $width, $height, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($small)
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($image, 0, 0, $width, $height)
    $small.Save($to, [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $small.Dispose(); $image.Dispose()
}
$work = Join-Path ([IO.Path]::GetTempPath()) "yata-shots"
New-Item -ItemType Directory -Force $work | Out-Null

if (-not $Exe) {
    # Same build as the installer (tools/build-installer.ps1).
    $publish = Join-Path $work "app"
    dotnet publish (Join-Path $root "src\Yata\Yata.csproj") -c Release -r win-x64 --self-contained false `
        -p:PublishSingleFile=true -p:DebugType=none -o $publish --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
    $Exe = Join-Path $publish "Yata.exe"
}

. (Join-Path $PSScriptRoot "vitrine.ps1")

$userYata = Get-Process Yata -ErrorAction SilentlyContinue | Select-Object -First 1
$userYataPath = if ($userYata) { $userYata.Path } else { $null }
if ($userYata) {
    Write-Host "Fechando o Yata aberto ($userYataPath); ele volta no fim."
    Get-Process Yata | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

try {
    foreach ($language in $Languages) {
        Write-Host "$language..."
        $c = $content[$language]
        if (-not $c) { throw "Sem conteúdo para '$language'" }
        $profileDir = Join-Path $work "profile"
        $shots = Join-Path $work "shots-$language"
        Write-Profile $profileDir $language $c
        if (Test-Path $shots) { Remove-Item -Recurse -Force $shots }
        New-Item -ItemType Directory -Force $shots | Out-Null
        Write-Vitrine $shots $c

        $env:YATA_DATA_DIR = $profileDir
        $process = Start-Process $Exe -ArgumentList "--bench", "vitrine", "--shots", "`"$shots`"" -PassThru
        Remove-Item Env:YATA_DATA_DIR
        $done = Join-Path $shots "done.txt"
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while (-not (Test-Path $done) -and $clock.Elapsed.TotalMinutes -lt 8) { Start-Sleep -Milliseconds 500 }
        if (-not $process.HasExited) { $process.WaitForExit(10000) | Out-Null }
        if (-not $process.HasExited) { $process | Stop-Process -Force }
        $result = if (Test-Path $done) { Get-Content $done } else { "sem resposta em 8 min" }
        if ($result -ne "ok") { throw "$language`: $result" }

        $target = Join-Path $OutDir $language
        New-Item -ItemType Directory -Force $target | Out-Null
        Get-ChildItem $shots -Filter *.png | Where-Object Name -ne "cover.png" | Copy-Item -Destination $target
        Write-Host ("  {0} imagens em {1}" -f (Get-ChildItem $target -Filter *.png).Count, $target)

        $site = Join-Path $SiteDir $(if ($language -eq "en") { "en" } else { "pt" })
        New-Item -ItemType Directory -Force $site | Out-Null
        foreach ($name in $siteShots.Keys) {
            $from = Join-Path $target "$name.png"
            $image = [Drawing.Image]::FromFile($from); $full = $image.Width; $image.Dispose()
            Save-Smaller $from (Join-Path $site "$name.png") ([math]::Min($siteShots[$name], $full))
        }
        Write-Host ("  {0} imagens do site em {1}" -f $siteShots.Count, $site)
    }
}
finally {
    if ($env:YATA_DATA_DIR) { Remove-Item Env:YATA_DATA_DIR }
    if ($userYataPath) {
        Start-Process $userYataPath
        Write-Host "Yata do usuário reaberto."
    }
}
