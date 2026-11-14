# Screenshots of the notch for the site and the README (Yata.exe --bench vitrine --shots): Yata goes
# through its states with made-up content (a song with lyrics, a day of meetings, tasks, a week of
# water and focus, files on the shelf, a build in the bubble) and draws each one to a PNG itself,
# with a transparent background and at twice the size. Once per language, in a throwaway profile
# (YATA_DATA_DIR), like tools/measure.ps1; a running Yata is closed and reopened. The song cover is
# cut from docs/yata-hero.png, so no image needs a license.
#
# Usage: ./tools/make-shots.ps1 [-Languages pt-BR,en] [-OutDir docs\screenshots] [-SiteDir site\img\shots] [-Exe path\Yata.exe]
#   Writes <OutDir>\<language>\<state>.png, e.g. docs\screenshots\pt-BR\aberto-foco.png, and the
#   ones the site shows, made lighter, in <SiteDir>\<en|pt>\.
param(
    [string[]]$Languages = @("pt-BR", "en"),
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

# The made-up content, per language. Times are minutes from when the shots run.
$content = @{
    "pt-BR" = @{
        Track    = @{ Title = "Voo da manhã"; Artist = "Yata"; Seconds = 214; Position = 63 }
        Lyrics   = @("O sol acorda devagar", "Na beira da janela", "Um corvo conta as horas", "Sem pressa de chegar",
                     "Leva o recado ao vento", "E volta pra cantar", "O dia cabe inteiro", "Num voo sobre o mar",
                     "A cidade ainda dorme", "O café esfria na mesa", "Tem tempo pra tudo", "Quando a mente sossega")
        Events   = @(
            @{ Title = "Planejamento da semana"; In = -90; Minutes = 60 },
            @{ Title = "Daily do time"; In = 8; Minutes = 15; MeetingUrl = "https://meet.google.com/" },
            @{ Title = "Almoço com a Ana"; In = 95; Minutes = 60 },
            @{ Title = "Revisão do design"; In = 210; Minutes = 45; MeetingUrl = "https://meet.google.com/" })
        Activity = @{ Title = "Build"; Text = "Compilando o projeto"; Progress = 0.62 }
        Hud      = @{ Text = "Volume"; Value = "60%"; Level = 0.6 }
        Tasks    = @("Revisar proposta do cliente", "Responder e-mails do suporte", "Ligar para a Ana", "Atualizar planilha de horas")
        Done     = "Reservar passagens"
        Files    = @("Proposta.pdf", "Contrato.docx", "Fotos da viagem.zip", "Notas.txt")
    }
    "en" = @{
        Track    = @{ Title = "Morning Flight"; Artist = "Yata"; Seconds = 214; Position = 63 }
        Lyrics   = @("The sun wakes slowly", "Along the window sill", "A raven counts the hours", "With no need to hurry",
                     "Carries a note on the wind", "And comes back to sing", "The whole day fits", "In one flight over the sea",
                     "The city is still sleeping", "The coffee cools on the table", "There's time for everything", "When the mind settles down")
        Events   = @(
            @{ Title = "Weekly planning"; In = -90; Minutes = 60 },
            @{ Title = "Team standup"; In = 8; Minutes = 15; MeetingUrl = "https://meet.google.com/" },
            @{ Title = "Lunch with Ana"; In = 95; Minutes = 60 },
            @{ Title = "Design review"; In = 210; Minutes = 45; MeetingUrl = "https://meet.google.com/" })
        Activity = @{ Title = "Build"; Text = "Compiling the project"; Progress = 0.62 }
        Hud      = @{ Text = "Volume"; Value = "60%"; Level = 0.6 }
        Tasks    = @("Review the client proposal", "Reply to support emails", "Call Ana", "Update the timesheet")
        Done     = "Book the flights"
        Files    = @("Proposal.pdf", "Contract.docx", "Trip photos.zip", "Notes.txt")
    }
}

function Write-Lrc($lyrics, [int]$position) {
    # One line every 6 s, sung twice through: the song keeps playing while the shots are taken.
    $start = $position - 14
    $lines = for ($i = 0; $i -lt 2 * $lyrics.Count; $i++) {
        $at = $start + 6 * $i
        "[{0:00}:{1:00}.00] {2}" -f [math]::Floor($at / 60), ($at % 60), $lyrics[$i % $lyrics.Count]
    }
    $lines -join "`n"
}

function Write-Profile([string]$dir, [string]$language, $c) {
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    New-Item -ItemType Directory -Force $dir | Out-Null
    $today = (Get-Date).Date
    $key = { param($d) $today.AddDays($d).ToString("yyyy-MM-dd") }

    $water = [ordered]@{}; $focus = [ordered]@{}
    $waterMl = @(1750, 2250, 1500, 2000, 1250, 2500, 1250)
    $focusMin = @(95, 140, 60, 0, 120, 75, 50)
    for ($i = 0; $i -lt 7; $i++) {
        $water[(& $key ($i - 6))] = $waterMl[$i]
        $focus[(& $key ($i - 6))] = $focusMin[$i] * 60
    }

    $taskIds = @($c.Tasks | ForEach-Object { [guid]::NewGuid().ToString() })
    $tasks = for ($i = 0; $i -lt $c.Tasks.Count; $i++) {
        $task = [ordered]@{ Id = $taskIds[$i]; Text = $c.Tasks[$i]; Done = $false }
        # "Call Ana" at 15:30, already announced: shots taken later in the day would otherwise all show
        # it as overdue, the notice taking the place of what each one is about.
        if ($i -eq 2) { $task.Due = $today.AddHours(15.5).ToString("s"); $task.Reminded = $true }
        $task
    }
    $tasks = @($tasks) + @([ordered]@{ Id = [guid]::NewGuid().ToString(); Text = $c.Done; Done = $true })

    # Real files, so the shelf shows the icons Windows has for them.
    $shelfDir = Join-Path $dir "shelf"
    New-Item -ItemType Directory -Force $shelfDir | Out-Null
    $shelf = foreach ($name in $c.Files) {
        $path = Join-Path $shelfDir $name
        Set-Content -Path $path -Value "Yata"
        $path
    }
    $picture = Join-Path $shelfDir "yata.png"
    Copy-Item (Join-Path $root "docs\yata-hero.png") $picture
    $shelf = @($shelf) + $picture

    @{
        Settings = [ordered]@{
            Language = $language
            HideInFullscreen = $false; CompanionEnabled = $false; AwaySummaryEnabled = $false; PauseFocusWhenAway = $false
            ActivitiesEnabled = $true; LyricsEnabled = $true
            HudVolume = $false; HudBrightness = $false; HudPower = $false; HudAudioDevice = $false
            HudCapsLock = $false; HudKeyboardLayout = $false; HudNetwork = $false; HudPrivacy = $false
        }
        Water = @{ RemindersEnabled = $false; Days = $water }
        Focus = @{ Days = $focus; TaskId = $taskIds[0] }
        Tasks = $tasks
        Shelf = $shelf
    } | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 (Join-Path $dir "data.json")
}

function Write-Vitrine([string]$dir, $c) {
    @{
        Track = $c.Track
        Lrc = Write-Lrc $c.Lyrics $c.Track.Position
        Events = $c.Events
        Activity = $c.Activity
        Hud = $c.Hud
    } | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $dir "vitrine.json")

    # The cover: the raven and its dithered sun, a square out of the README hero.
    $hero = [Drawing.Bitmap]::FromFile((Join-Path $root "docs\yata-hero.png"))
    $cover = New-Object Drawing.Bitmap 384, 384
    $g = [Drawing.Graphics]::FromImage($cover)
    $g.DrawImage($hero, (New-Object Drawing.Rectangle 0, 0, 384, 384), (New-Object Drawing.Rectangle 320, 0, 384, 384), [Drawing.GraphicsUnit]::Pixel)
    $cover.Save((Join-Path $dir "cover.png"))
    $g.Dispose(); $cover.Dispose(); $hero.Dispose()
}

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
