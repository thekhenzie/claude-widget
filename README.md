# Claude Usage Widget

A small Windows taskbar widget that shows your **Claude usage**: the current 5-hour session and the weekly (7-day) limit. Each has a countdown to when it resets. It sits just to the left of the system tray and updates every minute.

It reuses your existing **Claude Code** login, so there's nothing extra to sign in to.

## Requirements

- Windows 10 or 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Claude Code](https://claude.com/claude-code), installed and logged in at least once (this creates `~/.claude/.credentials.json`)

## Build and run

```powershell
dotnet build ClaudeUsageWidget\ClaudeUsageWidget.csproj -c Release
.\ClaudeUsageWidget\bin\Release\net10.0-windows\ClaudeUsageWidget.exe
```

Only one copy runs at a time. Launching it again exits straight away.

### Updating after a code change

The running widget locks its own `.exe`, so stop it before rebuilding:

```powershell
Stop-Process -Name ClaudeUsageWidget
dotnet build ClaudeUsageWidget\ClaudeUsageWidget.csproj -c Release
.\ClaudeUsageWidget\bin\Release\net10.0-windows\ClaudeUsageWidget.exe
```

To start it with Windows, put a shortcut to the `.exe` in `shell:startup`.

## How it works

- **Login:** the widget reads Claude Code's OAuth tokens from `~/.claude/.credentials.json` and refreshes them when they expire. It keeps its own copy in `%LOCALAPPDATA%\ClaudeUsageWidget\token-cache.json` and **never writes to Claude Code's credentials file**.
- **Usage:** once a minute it makes a one-token Haiku request and reads the `anthropic-ratelimit-unified-5h/7d-utilization` headers from the response. Each check uses a tiny amount of your quota. (`count_tokens` doesn't return these headers.)
- **Errors:** if a check fails, the bar keeps showing the last good numbers.
- **Placement:** the bar is 244×42 and repositions itself against the taskbar every 2 seconds.

No credentials are stored in this repository. Tokens only ever live in the two locations above, on your own machine.

## Project layout

| Path | What it does |
|---|---|
| `App.xaml.cs` | Startup. Wires up the HTTP client, credential store, usage client and the 1-minute poller, then shows the bar. |
| `Auth/CredentialStore.cs` | Reads, refreshes and caches the Claude Code OAuth login. |
| `Usage/UsageClient.cs` | Makes the usage check and reads the rate-limit headers. |
| `Usage/UsagePoller.cs` | Background poll loop. Keeps the last good result on failure. |
| `UI/DockedBarWindow.xaml(.cs)` | The bar itself: usage, clock and reset countdowns. |
| `Native/` | Win32 interop for finding the taskbar and staying on top of it. |
| `Settings/ClaudeModelSetting.cs` | Code for the optional model toggle below. It is built into the app but not used yet. |

## Optional: Opus / Sonnet toggle (off by default)

There's a finished but disabled feature that adds **Opus** and **Sonnet** buttons to the bar. They switch Claude Code's default model by changing only the `"model"` key in `~/.claude/settings.json`:

- Opus writes `opus[1m]`.
- Sonnet writes `sonnet`. Change `SonnetAlias` in `ClaudeModelSetting.cs` to `"sonnet[1m]"` if you want 1M context on Sonnet too.

Other keys in the file are kept as they are, and the file is written safely (to a temp file first, then swapped in).

To turn it on:

```powershell
git apply patches/model-toggle-ui.patch
```

Then stop the widget, rebuild and relaunch it. This widens the bar to 294px.

- The setting only applies to **new** Claude Code sessions. In a running session, use `/model`.
- If `settings.json` can't be written, the widget shows a warning and leaves the file unchanged.

## Note

This is an unofficial personal tool. It isn't made or supported by Anthropic.
