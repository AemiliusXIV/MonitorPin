# MonitorPin

Send apps to the monitor and size you want when they open, and force-minimize fullscreen games that won't minimize on their own. A small Windows tray tool.

[![Release](https://img.shields.io/github/v/release/AemiliusXIV/MonitorPin?sort=semver)](https://github.com/AemiliusXIV/MonitorPin/releases)
[![Downloads](https://img.shields.io/github/downloads/AemiliusXIV/MonitorPin/total)](https://github.com/AemiliusXIV/MonitorPin/releases)
[![License](https://img.shields.io/badge/license-source--available-blue)](LICENSE)

Some apps always open on the wrong screen, at the wrong size, or hidden behind whatever you were doing. Windows has no setting to fix that per app. MonitorPin watches for windows as they open and puts each one where you told it to go, and it force-minimizes fullscreen games that ignore the normal minimize.

## What it can do

- Send an app to a chosen screen and decide how it opens: maximized, normal, minimized, or a size you pick. It re-applies a moment later, so apps that jump back to their own spot don't win.
- Pull an app to the front when it likes to open in the background.
- Force-minimize a fullscreen game that won't minimize, from a hotkey or the tray menu. You can also minimize a named app that isn't in focus.
- Move the current window to the next or previous screen with a keyboard shortcut.
- Save where all your windows are right now and put them back later, from the app or a desktop shortcut.
- Name your screens however makes sense to you. Two identical monitors get told apart by where they sit.
- Dark mode, following Windows.
- Start with Windows, so your rules are in place the moment you log in.
- Share a setup with a friend by exporting your rules and sending the file.

## Install

1. Download the latest installer from the [Releases page](https://github.com/AemiliusXIV/MonitorPin/releases).
2. Run it and follow the prompts.
3. The window opens with your monitors already listed. Add your first rule and you're set.

Nothing else needs installing. Your rules live in your user profile, so they survive updates and reinstalls.

### About the Windows warning

MonitorPin isn't signed with a paid certificate, so the first time you run the installer Windows may show a blue "Windows protected your PC" box. Click **More info**, then **Run anyway**. Every build comes straight from this project's automated release, and each release includes a checksum file if you like to verify your download.

## Using it

Open it from the tray icon. Add a rule, pick the app and the screen, choose how it should open, and save. That's it, the rule kicks in the next time the app opens. Hotkeys, layouts, and screen names all live in the same window.

## Privacy

MonitorPin runs entirely on your machine. It looks at which apps are opening and where your monitors are, so it can place windows. None of that leaves your PC. There is no tracking and no keystroke logging.

The only thing it does online is check GitHub once a day for a newer version, and you can turn that off. It never downloads or installs anything without you clicking to allow it.

## License

Copyright (c) 2026 AemiliusXIV

This project is source-available. You may fork and modify it, but the source code may not be copied into other projects, in source or compiled form, without explicit written permission. Forks must preserve this license and credit the original author. See the [LICENSE](LICENSE) file for full terms.

Provided as is, without warranty of any kind. Use at your own risk.
