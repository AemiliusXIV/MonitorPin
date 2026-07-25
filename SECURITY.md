# Security

## Reporting a vulnerability

Found a security issue? Please open a private report through GitHub's Security
advisories ("Report a vulnerability" on the repo's Security tab) rather than a
public issue. Include what you found and how to reproduce it. I'll respond as
soon as I can.

## What the app can access

MonitorPin runs on your machine and works with local window and monitor state:

- Reads the process names and window titles of apps as their windows open, so it
  can match rules. This stays on your PC.
- Reads your monitor layout and window positions to decide where to place things.
- Writes your rules and settings to `%AppData%\MonitorPin`.
- Registers a Task Scheduler logon task (only if you turn on start-with-Windows)
  so it can run elevated without a UAC prompt at login.

There is no telemetry and no keystroke logging.

## Network access

One feature reaches the network, and it is opt-out: the update check. It asks
GitHub's API whether a newer release exists, throttled to once a day. It does not
download or install anything until you approve it. Downloads come only from this
repo's own GitHub release assets, over HTTPS, and are verified against the
release's `SHA256SUMS.txt` before anything runs.

## Secrets

No API keys, tokens or client secrets are committed to this repository. The app
needs none to run.
