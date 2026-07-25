# Changelog

All notable changes to MonitorPin are recorded here. Versions use
`Major.Minor.Patch`.

## 1.0.0 (2026-07-25)

First public release.

- Placement engine: matched apps get moved to a chosen monitor and window state
  (maximized / normal / minimized / custom size) when their window appears, with
  a re-apply pass to beat apps that reposition themselves right after showing.
- Force to foreground for apps that open in the background.
- Force-minimize (`SW_FORCEMINIMIZE`) for fullscreen games that won't minimize
  normally, on a global hotkey (default Ctrl+Alt+Down) and a tray item.
- Keyboard shortcuts to move the current window to the next/previous screen, and
  to force-minimize a named app even when it isn't focused.
- Per-rule options: "keep re-applying" for apps that shove themselves back, and
  "where my mouse is" to open on whichever screen the pointer is on.
- Window layouts: save where your windows are now and put them back later, from
  the main window or a shortcut.
- Settings window: add/edit/remove rules, pick monitor by hardware id or by
  role/position, capture a window's current position, set the minimize hotkey.
- Monitor matching handles duplicate-model displays by position, so identical
  monitors are told apart.
- Name your screens: by position, by the model name Windows reports, or with a
  name you type. Two identical monitors get the position added so they're
  distinct.
- Apply rules now: re-run rules against every open window without relaunching.
- Diagnostics: a "Save a report" button writes recent activity to a file, and
  says up front what's in it (matched apps and their window titles, where they
  were sent, anything force-minimized; no keystrokes or passwords).
- Elevated logon-task startup (no UAC prompt at login).
- Startup sweep: on launch, rules apply to windows that are already open, so it
  doesn't matter whether MonitorPin started before or after an app.
- Import and export rule sets, for sharing a setup with someone else.
- Rules whose monitor is currently disconnected are flagged in the list.
- Friendlier UI: plain-language labels, hover explanations, app names and icons,
  a custom app icon, and an About box.
- Dark mode, following Windows by default, switchable in Settings.
- Update check against GitHub releases, off-switchable and throttled to once a
  day. It tells you what's new and never downloads or installs anything without
  you clicking to allow it. Rules and settings are kept across an update.
- Self-contained single-file build and an Inno Setup installer for sharing. The
  installer opens with a note that MonitorPin is free and warns against copies
  from anywhere but the official page, and it can switch on start-with-Windows
  for you. Launching by hand opens the window; the sign-in start stays quiet.
