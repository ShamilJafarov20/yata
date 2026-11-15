# The README's animations: Yata plays a short scripted tour (Yata.exe --bench reel, MainWindow.Reel.cs)
# over the colorful scene of the Liquid Glass shots, with the same made-up content as the screenshots,
# and ffmpeg records it from the screen. The recording is cut at the scenes Yata marks and turned into
# GIFs in docs\reel. The screen is covered for about half a minute; a running Yata is closed and reopened.
#
# Usage: ./tools/make-reel.ps1 [-Exe path\Yata.exe] [-Video recording.mkv]
#   -Video skips the recording and cuts an earlier one again (marks.txt and ready.txt next to it).
param(
    [string]$Exe,
    [string]$Video,
    [string]$OutDir
)
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
if (-not $OutDir) { $OutDir = Join-Path $root "docs\reel" }
if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) { throw "ffmpeg not found (winget install Gyan.FFmpeg)" }
$work = Join-Path ([IO.Path]::GetTempPath()) "yata-reel"

. (Join-Path $PSScriptRoot "vitrine.ps1")

function Invoke-FFmpeg([string[]]$arguments) {
    # ffmpeg writes its progress to stderr, which Windows PowerShell would turn into an error under Stop.
    $ErrorActionPreference = "Continue"
    $output = & ffmpeg -hide_banner -loglevel error -y @arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed: $output" }
}

if (-not $Video) {
    if (-not $Exe) {
        $publish = Join-Path $work "app"
        dotnet publish (Join-Path $root "src\Yata\Yata.csproj") -c Release -r win-x64 --self-contained false `
            -p:PublishSingleFile=true -p:DebugType=none -o $publish --nologo -v q | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
        $Exe = Join-Path $publish "Yata.exe"
    }

    $c = $content["en"]
    $profileDir = Join-Path $work "profile"
    $dir = Join-Path $work "run"
    Write-Profile $profileDir "en" $c
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    New-Item -ItemType Directory -Force $dir | Out-Null
    Write-Vitrine $dir $c
    $Video = Join-Path $dir "reel.mkv"

    $userYata = Get-Process Yata -ErrorAction SilentlyContinue | Select-Object -First 1
    $userYataPath = if ($userYata) { $userYata.Path } else { $null }
    if ($userYata) {
        Write-Host "Closing the running Yata ($userYataPath); it comes back at the end."
        Get-Process Yata | Stop-Process -Force
        Start-Sleep -Milliseconds 500
    }

    $recorder = $null
    try {
        $env:YATA_DATA_DIR = $profileDir
        $app = Start-Process $Exe -ArgumentList "--bench", "reel", "--shots", "`"$dir`"" -PassThru
        Remove-Item Env:YATA_DATA_DIR

        $ready = Join-Path $dir "ready.txt"
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while (-not (Test-Path $ready)) {
            if ($clock.Elapsed.TotalSeconds -gt 60 -or $app.HasExited) { throw "Yata didn't get ready" }
            Start-Sleep -Milliseconds 200
        }
        Start-Sleep -Milliseconds 300
        $x, $y, $w, $h = (Get-Content $ready).Split(" ") | ForEach-Object { [int]$_ }
        # The window grows down as the notch opens: room for the open notch and its shadow. Even sizes,
        # for the encoders.
        # Wider, for the bubble beside the closed notch.
        $h = [Math]::Max($h, 400)
        $x += [int](($w - 880) / 2); $w = 880
        $h -= $h % 2

        # Lossless, so the GIFs are made from clean frames. The timestamps are the wall clock (gdigrab),
        # which is what Yata's marks are in.
        $info = New-Object Diagnostics.ProcessStartInfo "ffmpeg"
        # Quiet, so stderr (read only at the end) never fills up and stalls the recording.
        $info.Arguments = "-hide_banner -nostats -loglevel warning -y -f gdigrab -framerate 50 -draw_mouse 0 -offset_x $x -offset_y $y -video_size ${w}x${h} -i desktop -c:v libx264rgb -qp 0 -preset ultrafast -copyts `"$Video`""
        $info.UseShellExecute = $false
        $info.RedirectStandardInput = $true
        $info.RedirectStandardError = $true
        $recorder = [Diagnostics.Process]::Start($info)
        Start-Sleep -Milliseconds 1500
        Set-Content (Join-Path $dir "go.txt") "go"

        $done = Join-Path $dir "done.txt"
        $clock.Restart()
        while (-not (Test-Path $done) -and $clock.Elapsed.TotalMinutes -lt 3) { Start-Sleep -Milliseconds 200 }
        $recorder.StandardInput.Write("q")
        $recorder.StandardInput.Close()
        $stderr = $recorder.StandardError.ReadToEnd()
        $recorder.WaitForExit(15000) | Out-Null
        Set-Content (Join-Path $dir "ffmpeg.log") $stderr
        $result = if (Test-Path $done) { Get-Content $done } else { "no answer in 3 min" }
        if ($result -ne "ok") { throw "reel: $result" }
        if (-not $app.HasExited) { $app.WaitForExit(10000) | Out-Null }
    }
    finally {
        if ($env:YATA_DATA_DIR) { Remove-Item Env:YATA_DATA_DIR }
        if ($recorder -and -not $recorder.HasExited) { $recorder.Kill() }
        if ($userYataPath) {
            Start-Process $userYataPath
            Write-Host "Your Yata is back."
        }
    }
    Write-Host "Recorded $Video"
}

# Where each scene begins, in seconds of the recording.
$dir = Split-Path $Video -Parent
$start = (& ffprobe -v error -select_streams v:0 -show_entries stream=start_time -of csv=p=0 $Video) -as [double]
$marks = [ordered]@{}
foreach ($line in Get-Content (Join-Path $dir "marks.txt")) {
    $ms, $name = $line.Split(" ")
    $marks[$name] = [double]$ms / 1000 - $start
}
$marks.GetEnumerator() | ForEach-Object { "{0,-11} {1,6:0.00} s" -f $_.Key, $_.Value } | Write-Host

# A GIF from one or more stretches of the recording (seconds), cropped to a box (w, h, x, y in the
# recording's 880 px frame). One palette for the whole GIF, and only the changed rectangle per frame.
function New-Gif([string]$name, [object[]]$stretches, [int[]]$box) {
    $w, $h, $x, $y = $box
    # The recording's timestamps are the wall clock: from zero first, like the marks.
    $splits = -join (0..($stretches.Count - 1) | ForEach-Object { "[s$_]" })
    $parts = @("[0:v]setpts=PTS-STARTPTS,split=$($stretches.Count)$splits") + @(for ($i = 0; $i -lt $stretches.Count; $i++) {
        $from, $to = $stretches[$i]
        "[s$i]trim=start=$($from.ToString([cultureinfo]::InvariantCulture)):end=$($to.ToString([cultureinfo]::InvariantCulture)),setpts=PTS-STARTPTS,crop=${w}:${h}:${x}:${y}[p$i]"
    })
    $inputs = -join (0..($stretches.Count - 1) | ForEach-Object { "[p$_]" })
    $graph = ($parts -join ";") + ";${inputs}concat=n=$($stretches.Count):v=1:a=0,fps=25,split[a][b];" +
        "[a]palettegen=max_colors=256:stats_mode=full[pal];[b][pal]paletteuse=dither=sierra2_4a:diff_mode=rectangle"
    New-Item -ItemType Directory -Force $OutDir | Out-Null
    $out = Join-Path $OutDir "$name.gif"
    Invoke-FFmpeg @("-i", $Video, "-filter_complex", $graph, "-loop", "0", $out)
    "{0}: {1:0.0} MB" -f $name, ((Get-Item $out).Length / 1MB) | Write-Host
}

$open = @(760, 330, 60, 0)     # The open notch, with some of the scene around it.
$closed = @(880, 72, 0, 0)     # The closed notch and the bubble beside it.
# The tour starts as the notch springs open: the closed states are in "closed".
New-Gif "tour" @(, @(($marks["open"] - 0.3), ($marks["themes"] - 0.2))) $open
New-Gif "themes" @(, @(($marks["themes"]), ($marks["themes-end"]))) $open
New-Gif "closed" @(@(($marks["idle"]), ($marks["open"] - 0.1)), @(($marks["volume"] - 1.5), ($marks["end"]))) $closed
