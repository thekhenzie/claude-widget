# Claude Usage Widget

A small WPF (.NET 10, `net10.0-windows`) taskbar widget that docks just left of the
system tray and shows Claude session (5h) and weekly (7d) usage.

## Build & run

```
dotnet build ClaudeUsageWidget\ClaudeUsageWidget.csproj -c Release
```

The widget normally runs from `ClaudeUsageWidget\bin\Release\net10.0-windows\ClaudeUsageWidget.exe`.
It holds a lock on its exe, so to pick up a change: stop the process
(`Stop-Process -Name ClaudeUsageWidget`), rebuild Release, relaunch the exe. It is
single-instance (global mutex), so a second launch just exits.

## Layout

- `App.xaml.cs`: composition root. It builds `HttpClient`, `CredentialStore`,
  `UsageClient` and `UsagePoller` (1-minute interval), then shows `DockedBarWindow`.
- `Auth/CredentialStore.cs`: reuses Claude Code's OAuth login from
  `~/.claude/.credentials.json`, refreshes it when needed, and caches it in
  `%LOCALAPPDATA%\ClaudeUsageWidget\token-cache.json`. It never writes back to
  Claude Code's credentials file.
- `Usage/UsageClient.cs`: makes a 1-token Haiku `/v1/messages` call and reads the
  `anthropic-ratelimit-unified-*` response headers. `count_tokens` does not return
  those headers.
- `Usage/UsagePoller.cs`: runs the background poll loop. On failure it keeps the
  last good snapshot.
- `UI/DockedBarWindow.xaml(.cs)`: the 244x42 bar. It repositions every 2s against
  the taskbar and runs a clock and reset countdowns aligned to the minute.
- `Native/`: Win32 interop for finding the taskbar and handling z-order.

## Parked feature: Opus / Sonnet default-model toggle

**Status:** the code is kept but the UI is disabled. It was built and working on
2026-09-23, then the UI was reverted until it's wanted.

It adds two small stacked buttons, **Opus** and **Sonnet**, on the left of the bar.
They switch Claude Code's default model by rewriting only the `"model"` key in
`~/.claude/settings.json`. Opus writes `opus[1m]`, which is the user's normal
setting; Sonnet writes `sonnet`.

What's in place:

- `ClaudeModelSetting.cs` is **still in the project and compiles, but nothing
  calls it**. It handles:
  - reading and writing `settings.json` as `JsonObject`, so other keys and key
    order are preserved. It uses relaxed escaping so em dashes etc. aren't
    turned into `\u` escapes.
  - an atomic write (temp file, then `File.Move` with overwrite).
  - a `FileSystemWatcher` that raises `Changed` so the highlight follows
    external edits.
  - the aliases, which are the `OpusAlias` / `SonnetAlias` constants. Change
    `SonnetAlias` to `"sonnet[1m]"` if 1M context is wanted on Sonnet too.
- `patches/model-toggle-ui.patch` holds the UI wiring:
  - the button XAML, with the bar widened to 294px (`Width` in XAML plus
    `BarWidth` in code-behind).
  - the click handlers and active/inactive styling in `DockedBarWindow.xaml.cs`.
  - constructing and disposing `ClaudeModelSetting` in `App.xaml.cs`, with it
    passed into the window constructor.

### To re-enable

```
git apply patches/model-toggle-ui.patch
```

Then stop the widget, rebuild Release and relaunch. If the patch no longer applies
because those three files changed in the meantime, reapply it by hand from the
patch contents.

Notes:

- The setting only affects **new** Claude Code sessions. A running session keeps
  its model; use `/model` there.
- If `settings.json` can't be written, the click shows a warning `MessageBox` and
  leaves the file untouched.

## Conventions

- Don't write unit tests unless asked.
- Match the existing style: file-scoped namespaces, `sealed` classes, XML doc
  comments explaining *why* (see `UsageClient`, `CredentialStore`).
