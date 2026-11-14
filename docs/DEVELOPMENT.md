# Developing Yata

How to build, test and extend Yata: running it from source, the activities API and the AI agent hooks, translations, the code's structure and the roadmap. For what Yata is and how to install it, see the [README](../README.md).

## Requirements

- Windows 10 (2004+) or 11, x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build

## Build and run

```powershell
dotnet run --project src/Yata
```

Google Calendar and Tasks need your own OAuth client, which isn't in the repository. Without it, the rest of the app works normally. See [docs/google-calendar.md](google-calendar.md).

Automated tests (Pomodoro, lyrics, tasks, water, presence, player choice, version notice, calendar and translations, with a simulated clock and no network). They also run on GitHub Actions on every push:

```powershell
dotnet test
```

### Activities

Any script or program running as you on this PC can put an activity beside the notch with `yata-cli.exe`, installed next to `Yata.exe` (`%LOCALAPPDATA%\Programs\Yata`). The installer can add that folder to your PATH (an option, off by default) so `yata-cli` works from any terminal:

```powershell
yata-cli activity set build --title "Build" --text "compiling" --progress 40% --icon code
yata-cli activity set build --title "Build" --progress 0.8
yata-cli activity done build --title "Build" --text "ok"     # a check, then it goes away
yata-cli activity attention agent --title "Claude Code" --text "waiting for you" --icon agent
yata-cli activity end agent
```

It's a console program, so PowerShell, cmd, bash and AI agents wait for it and get its exit code and error messages; `Yata.exe` itself doesn't take commands. `yata-cli --help` lists everything.

- Actions: `set` (running), `attention` (needs you: warning color, and a notice that waits to be seen), `done`, `fail` (failed: warning color), `end`.
- **Send `--title` every time.** A running activity nobody updates goes away after 10 minutes (`--ttl` changes it, up to 24 h), and an id that's gone needs its title to come back. `attention` and `fail` stay until ended (up to 1 h); `done` stays 12 s. `end` on an id that's already gone is fine.
- Icons: `activity`, `code`, `build`, `download`, `upload`, `sync`, `agent`, `bell`, `timer`, and the agents' marks `claude`, `cursor`, `codex`. A fixed set: programs can't send images or arbitrary glyphs.
- Text is one plain line (titles up to 60 characters, text up to 120); control, invisible and direction-changing characters are removed.
- At most 8 activities at a time, and 60 requests per 10 s. Nothing is printed on success.
- Exit codes: 0 shown, 1 bad arguments (unknown option, icon or value), 2 Yata isn't running, 3 refused by Yata (the reason is printed).
- How it works: a named pipe that only your Windows user can open and that refuses network access. Activities live in memory only; their text isn't logged.

### AI agents: Claude Code, Cursor and Codex

Yata can show what your coding agent is doing — **working**, **waiting for you** (needs a permission, or your answer) and **done** — one bubble per session, titled with the program it runs in and the project folder ("Orca · yata", "Windows Terminal · api") and marked with the agent's logo (never recolored), so you tell them apart at a glance; a small ✓, ✗ or ! in its corner says done, failed or waiting for you. The logos only say where a notice comes from: Yata isn't affiliated with Anthropic, Anysphere or OpenAI (see [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md)). The agent's hooks call `yata-cli hook <agent>`; only the state goes to the notch, never your prompts, commands or the agent's answers. If Yata isn't running, the hook returns at once, and it never blocks or changes the agent (it always exits 0 and prints only the neutral answer some events expect).

**The easy way: Settings → AI agents.** One switch per agent found on the PC (Claude Code, Cursor, Codex). Yata adds its hooks to the agent's own settings file, next to whatever is there, keeps a copy of the file before each change (`.yata-backup`), and turning the switch off removes only what it added. New sessions pick it up. Also there:
- **Claude Code in WSL → Choose…** lists the WSL distros that have Claude Code; check the ones you want. Each gets the same hooks, calling `yata-cli.exe` through `/mnt/c`, and a `yata-cli` link in `~/.local/bin` for the skill. A distro that can't reach Windows (no `/mnt/c` access) is listed but can't be turned on.
- **yata-cli on PATH**, the same as the installer option (silent updates don't show it), for scripts and Codex.
- Uninstalling Yata takes its hooks out of Claude Code, Cursor and Codex on Windows, so none is left pointing at a removed `yata-cli.exe`. WSL distros keep theirs: turn them off in Settings first.

**By hand**, with `yata-cli` on PATH:

- **Claude Code**: install the plugin, which brings the hooks and the skill below:
  ```
  /plugin marketplace add ShamilJafarov20/yata
  /plugin install yata@yata
  ```
  Or add the hooks yourself: `yata-cli hook setup claude` prints them for `%USERPROFILE%\.claude\settings.json`. If the plugin is on, Settings shows Claude Code as on through it and leaves it alone.
- **WSL by hand**: in the distro, `ln -s /mnt/c/Users/<you>/AppData/Local/Programs/Yata/yata-cli.exe ~/.local/bin/yata-cli`, then install the plugin in that distro's Claude Code.
- **Cursor**: `yata-cli hook setup cursor` prints `%USERPROFILE%\.cursor\hooks.json`. Cursor has no event for "waiting for your approval", so it shows working and done. Yata only uses Cursor's fail-open events, never the ones that decide permissions.
- **Codex**: `yata-cli hook setup codex` prints `%USERPROFILE%\.codex\hooks.json` (review and trust it with `/hooks`). Older Codex without hooks: `notify = ["yata-cli", "hook", "codex"]` in `config.toml` shows when a turn is done.

**Skill**: so the agent puts its long tasks (builds, tests, deploys) in the notch on its own, `yata-cli skill install` copies a `SKILL.md` to `%USERPROFILE%\.claude\skills` (Claude Code, Cursor) and `%USERPROFILE%\.agents\skills` (Codex, Cursor). `yata-cli skill` prints it. The plugin already includes it for Claude Code.

Open the notch on a tab without using the mouse, for N seconds (previews, screenshots and UI tests):

```powershell
Yata.exe --show focus 30     # focus | water | tasks | shelf | calendar | media
```

Measure CPU, wake-ups, memory and startup time in fixed states (nothing playing, music with lyrics, open notch and Pomodoro). Each round uses a throwaway profile, a simulated song and no network, so your data and what you do on the PC during the measurement stay out of the numbers. A running Yata is closed and reopened at the end:

```powershell
./tools/measure.ps1 -Phases             # -States parado,musica -Rounds 3 -Seconds 20 -OutFile medicao.csv
```

Build the installer locally (needs [Inno Setup 6](https://jrsoftware.org/isinfo.php)):

```powershell
./tools/build-installer.ps1 -Version 0.1.0   # output in dist/
```

Releases are built by GitHub Actions when a version tag is pushed:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

What changes in each version goes in [CHANGELOG.md](../CHANGELOG.md) and [CHANGELOG.pt-BR.md](../CHANGELOG.pt-BR.md), written in the version's PR (along with the new number in `Yata.csproj`). The site shows both at [getyata.app/changelog](https://getyata.app/changelog), and the site build fails if the `Yata.csproj` version has no entry in both.

## Translations

Interface text lives in `src/Yata/Strings.resx` (English, the default) and `src/Yata/Strings.pt-BR.resx` (Portuguese). Use it as `Strings.Key` in C# and `{x:Static y:Strings.Key}` in XAML. A test fails when a language misses a key, has one left over or uses different `{0}` placeholders. `Helpers/Lang.cs` picks the language at startup. Log messages stay in Portuguese, since they're for whoever is debugging.

The website ([getyata.app](https://getyata.app)) lives in `site/`: the English page is `site/index.html`, and the Portuguese version comes from the translations in `site/pt.json`. The site's privacy policy mirrors `PRIVACY.md` and `PRIVACY.pt-BR.md`. Build and preview locally (published to GitHub Pages on every push to `main` that touches the site):

```powershell
./tools/make-site.ps1                        # output in _site/
python -m http.server 8000 -d _site          # http://localhost:8000
```

## Structure

```
src/Yata/
  App.xaml(.cs)               startup, single instance, tray icon
  MainWindow.xaml(.cs)        the notch window, shape, animation, compact/expanded modes
  MainWindow.*.cs             parts by topic: media, lyrics, notices, Yata, privacy, hotkey...
  Views/RavenSprite.cs        Yata: pixel-art frames and crisp rendering
  Views/RavenAnimator.cs      Yata's behavior (blink, look, sleep, fly)
  Views/                      Focus, Water, Tasks and Shelf pages
  Services/PresenceTracker.cs away/back and the "while you were away" summary
  Services/MediaService.cs    Windows media session (SMTC)
  Services/LyricsService.cs   lyrics lookup/cache (LRCLIB) and LRC parser
  Services/AudioWatcher.cs    volume and output device (Core Audio)
  Services/BrightnessWatcher.cs  brightness (WMI)
  Services/PowerWatcher.cs    charger and battery
  Services/NetworkWatcher.cs  network connection
  Services/KeyboardWatcher.cs Caps Lock and keyboard language (no hook)
  Services/FullscreenWatcher.cs  detects full-screen apps and keeps the notch on top
  Services/PrivacyWatcher.cs  camera and microphone in use (MicrophoneMonitor.cs: Core Audio)
  Services/UpdateChecker.cs   optional new version notice (GitHub releases)
  Services/UpdateInstaller.cs "Update and restart": download, SHA-256 check, silent setup
  Services/HudQueue.cs        which notice shows, and reminders waiting their turn
  Services/Activities.cs      other programs' activities: request, cleaning, the board
  Services/BrandMarks.cs      Claude, Cursor and Codex marks (Lobe Icons, MIT); Views/IconView.cs draws them
  Services/ActivityPipe.cs    the local pipe (this user only); ActivityClient.cs, the sending side
src/Yata.Cli/                 yata-cli.exe: console side for scripts and agents (compiles Yata's activity files)
  Services/AgentHook.cs       (in src/Yata) Claude Code, Cursor and Codex hook events → activities
integrations/claude-code/     Claude Code plugin: hooks and the yata-notch skill (.claude-plugin/marketplace.json lists it)
  Services/AppStore.cs        local storage (JSON)
  Services/FocusTimer.cs      Pomodoro
  Services/WaterTracker.cs    water and reminders
  Services/TaskList.cs        tasks and reminders
  Services/GoogleTasks.cs     Google Tasks sync (GoogleAccount.cs: account shared with the calendar)
  Services/Shelf.cs           file shelf
  Helpers/                    animation springs, album art color, file icons, sound, paths, language
  Interop/                    Win32 and COM calls (Core Audio)
  Assets/yata.ico             icon (generated by tools/make-icon.ps1)
  SettingsWindow.xaml(.cs)    settings window
  Strings.resx                interface text in English (Strings.pt-BR.resx: Portuguese)
installer/yata.iss            installer (Inno Setup) and its images (wizard-*.png)
docs/                         README images, Google registration and code signing
.signpath/                    SignPath artifact configurations (a copy of what's in the project there)
tools/                        icon, art (PhotoCraft), GIF (EffectCraft), installer and measurement
tests/Yata.Tests/             tests (xUnit)
.github/workflows/ci.yml      build and tests on every push
.github/workflows/release.yml automatic release from a tag
```

## Roadmap

1. ~~Notch base + media~~
2. ~~Synced lyrics (LRCLIB)~~
3. ~~System notices: volume, brightness, battery, headphones, Caps Lock~~
4. ~~Pomodoro, water reminder, to-do~~
5. ~~File shelf (drag and drop)~~
6. ~~Identity (Yata) and companion~~
7. ~~Settings, start with Windows, installer~~
8. ~~Camera/microphone in use, multiple displays, global hotkey, version notice~~
9. ~~English and Portuguese~~

10. ~~Notice queue, bubble beside the notch and activities from other programs~~

Next ideas: [docs/roadmap.md](roadmap.md) (in Portuguese) — code signing (in progress, see [docs/code-signing.md](code-signing.md)), winget, Claude Code integration, Bluetooth battery, call controls and more.
