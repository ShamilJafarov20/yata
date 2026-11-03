<h1><img src="docs/brand/yata-lockup.svg" alt="Yata" height="56"></h1>

**English** · [Português](README.pt-BR.md)

> **About this fork.** This is a fork of [akaneni/yata](https://github.com/akaneni/yata) (MIT) that adds more ways to style and control the notch and a set of everyday tools. Everything below the "What this fork adds" section is the original Yata's README; credit for Yata itself goes to its author.

## What this fork adds

**Look and control**
- **Themes**: 10 ready-made themes (Midnight, Liquid Glass, Graphite, Neon, Ocean, Forest, Sunset, Violet, Sakura, Paper), or your own five colors. **Liquid Glass** blurs and bends what's behind the notch, with a tint that adapts to bright backgrounds. It follows motion behind it smoothly (up to about 120 updates a second on a fast display), with a setting to lower that and save power.
- **Clock styles**: eight number fonts (Thin, Bold, Condensed, Digital, Serif, Handwritten…) and three sizes.
- **Clock companions**: Yata the raven or an original pixel character beside the clock (cat, ghost, robot, alien, frog, bat, masked hero), blinking now and then.
- **Keyboard shortcuts you choose** for: open and close, hide the notch completely, play/pause, next, previous, player volume, like on Spotify, microphone mute, silence call apps. Taken combinations are flagged.
- **Hide completely**: one shortcut (Ctrl+Alt+H by default) or the tray menu takes the whole notch off the screen and back.

**Music**
- **The player's own volume**: a bar in the player that turns down only the app playing (YouTube in the browser, Spotify), not the game or the call. Works for free Spotify accounts.
- **Switch players**: with two players open (Spotify and a YouTube tab), ⇄ switches between them; ✕ takes one off the notch until it plays again. Untitled media sessions (a game's sounds) no longer hide the real player.
- **Cleaner browser titles**: "Song / Artist · Opera" instead of "Artist - Song (Official Video)".
- **Spotify Web API (optional)**: like, shuffle and repeat, with your own Spotify app's Client ID.

**Daily tools** (four new pages in the open notch)
- **Timers and alarms**: type "10m", "pizza 12m" or "7:30 daily"; a stopwatch with laps. Alarms ring during games too, without showing over them.
- **Clipboard**: the last 12 copies (in memory only, password managers skipped); after Win+Shift+S the notch offers Copy / Save / Open.
- **Notes**: a scratchpad saved as you type.
- **Devices and calls**: Bluetooth headphone/mouse batteries, microphone mute and "silence call apps" for any app (Discord, Teams, games), Wi-Fi status and a speed test.

**Automatic**
- **Game summary and game mode**: how long you played when a full-screen game ends; no notices or sounds during games except alarms and timers.
- **Prayer times** (optional), computed on the PC from the weather city.
- **Download progress** for browser downloads in the Downloads folder, beside the notch.
- **Phone and app notifications** (optional) from Phone Link, Telegram, WhatsApp, Discord.
- **Easier file drops**: the shelf stays open while it's the page, and a file dragged near the top of the screen opens it.

**What the new features read or send** (on top of the list further down): the Spotify Web API (only with your own Client ID, signed in), Cloudflare's speed test (only when you press *Test speed*), and, only if you turn them on, the clipboard (in memory) and Windows' own notification list (read-only, `wpndatabase.db`). Battery levels, Wi-Fi and game times are read on the PC; game minutes per day are kept in `data.json`.

---

![Yata, a pixel-art raven, perched in front of a dithered sun](docs/yata-hero.png)

A live notch at the top of the Windows screen, right below the camera: it shows what's playing, the time and your reminders, and gets out of the way when it isn't needed. Website: **[getyata.app](https://getyata.app)**.

The name comes from **Yatagarasu** (八咫烏), the messenger and guide crow of Japanese mythology — a relative of Huginn and Muninn, the ravens who fly over the world and come back to tell what they saw. That's what Yata does: it sits up there and brings you the news.

Open source, no account and no telemetry.

**Local data:** tasks, the water log, focus time per day and shelf paths are kept in `%LOCALAPPDATA%\Yata\data.json`. A diagnostic log (errors and what each part of the app did, with no songs, tasks, events, paths or accounts) is kept in `%LOCALAPPDATA%\Yata\logs` for 7 days, for you to attach to an issue if you want.

**Network access:** only what you turn on, and nothing about the PC is sent:
- lyrics lookup on [LRCLIB](https://lrclib.net) (an open, free service), sending only the artist, title and length of the song. Lyrics are cached in `%LOCALAPPDATA%\Yata\lyrics`. Can be turned off in Settings.
- new version notice: **off by default**. When on, it checks GitHub's [public releases page](https://github.com/ShamilJafarov20/yata/releases) once a day. **Check now** (Settings → Updates, or the tray menu) asks right away, on or off. Updating takes one click (**Update and restart** in Settings): the installer comes from GitHub and is checked against its published SHA-256 before it runs; once Yata is code-signed, it also has to carry the same publisher's signature.
- **weather** from [Open-Meteo](https://open-meteo.com) (open, no account or key), only if you turn it on: sends the chosen city's coordinates, rounded to ~1 km. The city search sends only the name you type.
- **Google Calendar and Google Tasks**, only if you connect your account and allow each one: reading your calendars and today's and tomorrow's events, and syncing one task list, directly between the PC and Google. The access is encrypted with DPAPI in `%LOCALAPPDATA%\Yata\google.dat` and events stay in memory only. Details in [PRIVACY.md](PRIVACY.md).

## Identity

- **Minimal and monochrome**: black notch, white text; the album art's color is the only accent.
- **Yata**: a 1-bit pixel-art raven (16×14), white on the notch's black. Hand-drawn in `Views/RavenSprite.cs`; the app icon is generated from the same sprite (`tools/make-icon.ps1`).
- **Repertoire**: blinks, looks back, settles its feathers and, now and then, looks up, stretches a wing or hops; sleeps breathing slowly; caws when it brings a reminder, shakes off water when the water goal is reached and arrives with a letter when it brings the news.
- **Logo**: "yata" in pixel letters on the raven's grid, with a 2 px stroke; the tail of the "y" goes down at the same angle as Yata's back, and Yata perches on the text's baseline. White, black and light/dark-theme versions in `docs/brand/`, generated by `tools/make-brand.ps1` from the sprite itself (along with the favicon and the site's link preview).
- **Website type**: DotGothic16 for headings (pixel, with the 八咫烏), Geist for text and Geist Mono for details.
- **Larger art** (README and installer): the same stroke at twice the resolution and with three legs, like the Yatagarasu of the myth, in front of a 1-bit dithered sun. Made with PhotoCraft from the ArtCraft suite (`tools/make-art.ps1`); the GIF below comes from the sprite itself and is encoded with EffectCraft (`tools/make-gif.ps1`).

![Yata's repertoire in the notch](docs/yata.gif)

## Screenshots

<p align="center"><img src="docs/screenshots/en/fechado-musica.png" width="448" alt="Yata closed while a song plays: cover, the line being sung, the equalizer and a meeting countdown in a bubble"></p>
<p align="center"><img src="docs/screenshots/en/aberto-musica.png" width="554" alt="Yata open on a song: cover, title, progress, controls and the synced lyrics underneath"></p>
<p align="center"><img src="docs/screenshots/en/aberto-foco.png" width="554" alt="The Focus tab: countdown, the task for the session, today against a 2-hour goal and the week"></p>
<p align="center"><img src="docs/screenshots/en/aberto-tarefas.png" width="554" alt="The Tasks tab: tasks, one with a 3:30 PM reminder and one done"></p>

Drawn by Yata itself, with sample content, by `tools/make-shots.ps1`; every state is in [docs/screenshots](docs/screenshots).

## Features (v0.1)

- **Welcome**: the first time, Settings open with what the notch can do and the choices that are yours (water reminders, new version notice). The tray menu has **Open Yata**.
- **English and Portuguese**: follows the Windows language (Portuguese on any Portuguese Windows, English everywhere else), or pick one in Settings → Language.
- **Compact notch**: a clock when nothing is playing; album art, title and an animated equalizer when media is.
- **Expands on hover** with a spring animation; collapses when the mouse leaves.
- **Media player** for any app that uses the Windows media controls (Spotify, YouTube in the browser, etc.): album art, title, artist, a seekable progress bar, previous/play/next. With several players open, it follows the one that is **actually playing**.
- **Synced lyrics**: under the player in the open notch, three lines with the current one highlighted; clicking a line jumps to it. The compact notch shows the line being sung.
- **System notices** in the compact notch: volume/mute, brightness, charger plugged/unplugged, low battery (20% and 10%), audio output change (headphones/speakers), Caps Lock, keyboard language change and network connection. The keyboard is read by polling its state, **with no hook** — the app never sees what you type.
- **Camera and microphone in use**: an orange (microphone) or green (camera) dot in the closed notch and the apps' names in the open notch. The microphone comes from Windows audio sessions (Core Audio). The camera relies on the registry Windows kept up to version 25H2; on Windows 11 26H2 that data requires administrator rights, so there only the microphone is detected.
- **Ctrl+Alt+Y shortcut** opens and closes the notch from anywhere. **Ctrl+Alt+J** (off by default) goes to the window of the activity that needs you, and pressed again, to the next one. Both use `RegisterHotKey`: Windows reports only those combinations.
- **Multiple displays**: choose in Settings which display the notch sits on; it respects each display's scaling.
- **Tabs in the open notch**: Media, Calendar, Focus, Water, Tasks and Shelf.
- **Calendar (Google)**: connect your Google account (OAuth, read-only) and choose which calendars appear. The tab shows today and tomorrow, highlights the ongoing event and has a **Join** button for Meet, Zoom and Teams calls. The closed notch shows "Standup in 8 min" in the 15 minutes before, and a notice (with sound) arrives a configurable few minutes before, plus another when the event starts. Declined invitations don't appear. To register the app with Google, see [docs/google-calendar.md](docs/google-calendar.md) (in Portuguese).
- **Weather** (off by default): pick the city by name or, if you prefer, with a single reading of the Windows location (rounded to ~1 km right away, never stored precisely). No IP geolocation. 1-bit pixel-art icons in Yata's style. The closed notch shows the icon and temperature next to the clock; open with no music, it shows the condition, high/low and expected rain. A notice arrives when the chance of rain goes over 60% in the next 2 hours. Weather data: [Open-Meteo.com](https://open-meteo.com) (CC BY 4.0).
- **Focus (Pomodoro)**: 25 min of focus / 5 min break, a 15 min long break every 4 sessions, a sound at the end of each phase and a countdown in the compact notch. Explicit controls for each state: **Start**, **Pause**, **Resume**, **Stop** and **Skip break**; when paused, the ring dims and the time pulses gently (and the compact notch shows ⏸). Pick the task the session is for from the Tasks tab and tick it off right there; beside it, today's focus against a daily goal (2 h by default, in Settings) and the last 7 days.
- **Water**: a daily goal, a +250 ml button, a chart of the last 7 days and an optional hourly reminder (8:00–22:00) until the goal is reached (off until you turn it on, on the welcome card or in Settings).
- **Tasks**: a list with check/remove (a removal can be undone for a few seconds); a time at the end of the text ("Call Ana at 3pm", "Meeting 15:30", "ligar às 9h") becomes a reminder. Optionally syncs both ways with a **Google Tasks** list (Google keeps only the day; the reminder time stays in Yata). The notch takes keyboard focus only while you type and gives focus back to the previous window when you're done.
- **File shelf**: drag files or folders onto the notch to keep them and drag them back out into any app; double-click opens. Only the path is kept (nothing is copied), and deleted/moved items disappear on their own. Images get a thumbnail. "Clear" can also be undone for a few seconds.
- **Yata (the companion)**, based on the time since the last mouse/keyboard input (`GetLastInputInfo` — only the time, never the content):
  - **Ambient mode** (2 min idle, nothing playing): the clock gives way to Yata perched, blinking, looking around and settling; at 10 min it naps.
  - **With music playing** while you're away: it takes the equalizer's place and bobs its head.
  - **"While you were away"**: when you come back, Yata flies in with a letter and the notices you missed (reminders, water, battery, paused focus).
  - **Messenger**: reminders (tasks, water, focus, new version) arrive with Yata cawing in place of the icon; small wins get a celebration (a flight at the end of focus, a shake at the water goal).
  - **Automatic focus pause** after 5 min away (giving back the unattended time), unless media is playing.
  - **Demo** from the tray menu (or `Yata.exe --demo`) to see every state in ~30 s.
- **Settings** (the gear in the notch, the menu or a double-click on the tray icon): language, start with Windows, hide in full screen, hover delay, album art color or black and white, lyrics on/off, each kind of notice, Pomodoro times, water goal and reminders, and Yata's behavior. Everything applies immediately.
- **Now, not at the next round**: **Check now** for a new version (Settings → Updates, or the tray menu, even with the daily check off); **↻** on the Agenda and Tasks tabs and **Sync now** in Settings read Google at once; **Refresh now** fetches the forecast. Each shows when it last ran, and a second click within 15 seconds doesn't go online again.
- Reminders that arrive while the notch is open or in full screen wait and show up afterwards, one at a time, instead of being lost. A status blip (volume, Caps Lock) shows at once and the reminder it interrupted comes back right after.
- **Bubble beside the notch**: like the iPhone's island splitting in two, a second small notch, a pill with what it is on one end (the program's icon, the agent's logo) and how it's going on the other, carries what doesn't fit in the first: while music has the notch, the focus countdown (a ring emptying around the minutes left) and the next meeting move there, and so do other programs' activities. One at a time, by priority (what needs you, the meeting, focus, activities); hover it to see what it is and how many others are waiting. Clicking an activity takes you to the window it came from (the terminal where the agent asks for a permission, say), and you answer there. While the notch is open, the bubble grows into a second notch beside it that lists every activity, each with a way to its window and a way to dismiss it.
- **Activities from other programs** (scripts, builds, AI agents): they can show their progress in the bubble with `yata-cli`. See [Activities](#activities) below. Can be turned off in Settings.
- **Hides in full screen** (videos, games, presentations).
- **Out of screen sharing and recordings**: in a Teams/Meet/Zoom call, an OBS stream or a screenshot, the notch isn't captured, so the next meeting, your tasks and your agent's project don't show up for others (you still see it). On by default; `--show` keeps the notch capturable for previews.
- **The chime comes with the reminder**: a reminder held back by a game in full screen chimes when it shows, not in the middle of the match. No chime while the microphone is in use (a call) or Windows says not to disturb, and it can be turned off. A held reminder that's no longer true ("Daily in 5 min" after the meeting started) is dropped instead of shown late.
- Doesn't steal focus and doesn't show in Alt+Tab or the taskbar. Quit from the tray icon.

## Install

Download `Yata-Setup-x.y.z.exe` from [Releases](https://github.com/akaneni/yata/releases) (~3 MB). The installer:

- installs for your user only, without asking for administrator rights;
- if the **.NET 10 Desktop Runtime** is missing, downloads it straight from Microsoft, checks that Microsoft signed it, and installs it (that step asks for administrator rights);
- offers to start with Windows and a desktop shortcut;
- on uninstall, removes the app and the automatic start but **keeps your data** in `%LOCALAPPDATA%\Yata`.

The installer isn't digitally signed yet, so Windows SmartScreen may warn on the first run: choose **More info**, then **Run anyway**. To check the file first:

- **Checksum:** in PowerShell, `Get-FileHash .\Yata-Setup-x.y.z.exe` must match the line in the release's `SHA256SUMS.txt`.
- **Provenance:** every installer is built by GitHub Actions from this repository and carries a build provenance attestation. With the [GitHub CLI](https://cli.github.com), `gh attestation verify Yata-Setup-x.y.z.exe --repo akaneni/yata` confirms which workflow run and commit built it.

The SignPath Foundation, which signs open-source projects for free, declined Yata in October 2026 for lack of public visibility; Yata will apply again as it grows. See the [code signing policy](https://getyata.app/code-signing) and [docs/code-signing.md](docs/code-signing.md) (in Portuguese).

## Requirements

- Windows 10 (2004+) or 11, x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build

## Build and run

```powershell
dotnet run --project src/Yata
```

Google Calendar and Tasks need your own OAuth client, which isn't in the repository. Without it, the rest of the app works normally. See [docs/google-calendar.md](docs/google-calendar.md).

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

Yata can show what your coding agent is doing — **working**, **waiting for you** (needs a permission, or your answer) and **done** — one bubble per session, titled with the program it runs in and the project folder ("Orca · yata", "Windows Terminal · api") and marked with the agent's logo (never recolored), so you tell them apart at a glance; a small ✓, ✗ or ! in its corner says done, failed or waiting for you. The logos only say where a notice comes from: Yata isn't affiliated with Anthropic, Anysphere or OpenAI (see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)). The agent's hooks call `yata-cli hook <agent>`; only the state goes to the notch, never your prompts, commands or the agent's answers. If Yata isn't running, the hook returns at once, and it never blocks or changes the agent (it always exits 0 and prints only the neutral answer some events expect).

**The easy way: Settings → AI agents.** One switch per agent found on the PC (Claude Code, Cursor, Codex). Yata adds its hooks to the agent's own settings file, next to whatever is there, keeps a copy of the file before each change (`.yata-backup`), and turning the switch off removes only what it added. New sessions pick it up. Also there:
- **Claude Code in WSL → Choose…** lists the WSL distros that have Claude Code; check the ones you want. Each gets the same hooks, calling `yata-cli.exe` through `/mnt/c`, and a `yata-cli` link in `~/.local/bin` for the skill. A distro that can't reach Windows (no `/mnt/c` access) is listed but can't be turned on.
- **yata-cli on PATH**, the same as the installer option (silent updates don't show it), for scripts and Codex.
- Uninstalling Yata takes its hooks out of Claude Code, Cursor and Codex on Windows, so none is left pointing at a removed `yata-cli.exe`. WSL distros keep theirs: turn them off in Settings first.

**By hand**, with `yata-cli` on PATH:

- **Claude Code**: install the plugin, which brings the hooks and the skill below:
  ```
  /plugin marketplace add akaneni/yata
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

What changes in each version goes in [CHANGELOG.md](CHANGELOG.md) and [CHANGELOG.pt-BR.md](CHANGELOG.pt-BR.md), written in the version's PR (along with the new number in `Yata.csproj`). The site shows both at [getyata.app/changelog](https://getyata.app/changelog), and the site build fails if the `Yata.csproj` version has no entry in both.

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

Next ideas: [docs/roadmap.md](docs/roadmap.md) (in Portuguese) — code signing (in progress, see [docs/code-signing.md](docs/code-signing.md)), winget, Claude Code integration, Bluetooth battery, call controls and more.
