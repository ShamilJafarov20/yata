# Yata privacy policy

*Last updated: October 28, 2026*

Yata is an open-source Windows app that shows a "notch" at the top of the screen. It has no account, no server and no telemetry. This policy explains what data Yata uses and where it goes.

## Data that stays on your computer

Tasks, water log, focus time per day, shelf paths and settings are kept in `%LOCALAPPDATA%\Yata\data.json`. This data never leaves your PC, with one exception you choose: if you turn on Google Tasks sync, your tasks go to your list on Google (see below).

Yata also keeps a diagnostic log in `%LOCALAPPDATA%\Yata\logs`: the time, which part of the app acted and any errors that happened. It does not record song titles, task text, event titles, file names, location or account. Paths, web addresses and email addresses that appear in an error message are removed before writing. Each file is kept for 7 days, with at most 1 MB per day. If Yata closes because of an error, it writes a `crash-*.log` there and exits without triggering Windows Error Reporting. None of this is sent anywhere: you decide whether to attach it to an issue (Settings → Open log or Copy diagnostics).

Other programs on this PC (a build script, an AI agent at work) can show their progress beside the notch with `yata-cli`. They reach Yata through a local channel (a named pipe) that only your Windows user can open and that refuses connections from the network. What they send (a title, a short text, the progress, and which window they run in, so a click on the activity takes you there) is kept in memory only: it is never written to disk or to the log, and it disappears when the activity ends or expires, or when Yata closes. Nothing is sent anywhere. You can turn this off in Settings → Activities.

## External services

Yata only goes online in these cases:

- **LRCLIB** (lrclib.net), to look up song lyrics: sends the artist, title and length of the track that is playing. Can be turned off in Settings.
- **Open-Meteo** (open-meteo.com), for the weather, only if you turn it on: sends the latitude and longitude of the chosen city, rounded to about 1 km. When you search for a city, it sends the name you typed. If you click "Use my location", Yata asks Windows for your position once, rounds it to about 1 km right away and discards the precise value. It does not track your location after that and never works out where you are from your IP. Settings keep only the chosen name and the rounded coordinates.
- **GitHub** (api.github.com), for the new-version notice, only if you turn it on (it starts off): once a day, it asks which Yata version is the latest. The same question goes out once each time you click **Check now** (Settings → Updates, or the tray menu), even with the notice off. The request carries only the name and version of Yata. If you then click **Update and restart** in Settings, Yata downloads the new installer from GitHub, checks it against the SHA-256 that GitHub publishes for it, and runs it; nothing is sent besides the download request.
- **Google Calendar** and **Google Tasks**, only if you connect your account and enable each one (see below).

## Google Calendar

When you connect, you authorize Yata, on Google's own screen, to:

- see your email address, only to show which account is connected;
- see the list of your calendars, so you can choose which ones appear;
- see the events in the chosen calendars (**read-only**).

How the data is used:

- Yata fetches **today's and tomorrow's** events to show them in the notch and in the Agenda tab, and to notify you before each one. From each event, the app uses the title, time, response status and call link (Meet, Zoom, Teams).
- Events are kept **in memory only** and disappear when Yata closes. Nothing is written to disk except the list of calendars you chose.
- The access granted by Google (*refresh token*) and your email are kept in `%LOCALAPPDATA%\Yata\google.dat`, **encrypted with Windows data protection (DPAPI)**: only your Windows user, on this computer, can read it.
- Communication happens **directly** between your PC and Google's servers. No data goes through Yata's or anyone else's servers, because there are none.
- Yata does **not** create, change or delete events, does **not** share your data with anyone, does **not** use it for advertising and does **not** use it to train AI models.

## Google Tasks

If you turn on **Tasks** in Settings, Yata asks Google for permission to **see and edit your tasks**. Editing is needed because the sync works both ways.

- Yata reads **one list** chosen by you (at first, "My Tasks"): open tasks and those completed today. It uses the title, status (open or completed) and date.
- When you create, complete, reopen or remove a task in Yata, it does the same in that list. It does **not** touch other lists, notes or subtasks.
- Synced tasks are kept in `%LOCALAPPDATA%\Yata\data.json`, together with local tasks, so the tab works offline. When you turn off sync or disconnect the account, the tasks that came from Google are deleted from that file (they stay on Google).
- The time of a reminder ("Meeting 15:30") stays in Yata only. Google Tasks receives just the day.
- As with the calendar, communication is direct between your PC and Google, and the data is not shared, used for advertising or used to train AI models.

Yata's use and transfer of information received from Google APIs adheres to the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy), including the Limited Use requirements.

### How to remove access

- In Yata: **Settings → Google → Disconnect**. Access (calendar and tasks) is revoked at Google and the local file is deleted.
- At Google: <https://myaccount.google.com/connections>, removing Yata.
- Uninstalling Yata does not delete the `%LOCALAPPDATA%\Yata` folder. Delete it by hand if you want to remove everything.

## AI and machine learning

Yata does not use, include or connect to any artificial intelligence or machine learning model or service, whether on your PC or online. Data received from Google (events, calendars, tasks, email) is never sent to an AI provider and is never used to create, train or improve any model.

The programs that show progress with `yata-cli` (see "Data that stays on your computer") may be AI agents, but that channel goes one way only: they send progress to Yata and get back just "ok" or an error message. They cannot read your events, tasks or any other data in Yata.

## This website

[getyata.app](https://getyata.app) is a static page hosted on GitHub Pages and served through Cloudflare. It sets no cookies and runs no analytics or trackers. Fonts are served from the site itself, with no third-party requests. GitHub and Cloudflare, as host and delivery network, may keep standard server logs. The same policy is on the website in [English](https://getyata.app/privacy) and [Portuguese](https://getyata.app/pt/privacy).

## Contact

Questions or problems: open an issue at <https://github.com/akaneni/yata/issues>.
