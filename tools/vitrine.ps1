# The made-up content of the screenshots and the README animations, and the throwaway profile Yata reads
# it from: dot-sourced by tools/make-shots.ps1 and tools/make-reel.ps1. Expects $root (the repository).
Add-Type -AssemblyName System.Drawing

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
