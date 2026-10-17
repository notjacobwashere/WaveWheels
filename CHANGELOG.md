# Changelog

## 0.6.0

- Added Download and install on the Updates page, with progress, cancellation, retry, and the existing release-page fallback.
- Accepts only the official versioned Windows installer from this repository, with a bounded download and SHA-256 verification before launch.
- Saves pending settings before handing off to Setup. Download, verification, or launch failures keep the current app running.
- In-app upgrades reopen configuration automatically and retain the installation location, settings, and startup preference. Older versions need a one-time Setup upgrade to gain the button.
- Added updater download/lifecycle regressions and clean-runner upgrade/reopen checks.

## 0.5.0

- Added opt-in automatic profiles based on the focused application's executable, with manual override and fallback behavior.
- Added scroll step, acceleration, and overlay hide-delay controls in Settings.
- Added per-wheel maximum volume limits, including consistent limits for duplicate channel/mix targets.
- Added current-profile and all-profile export, plus validated additive import with independent identities and unique names.
- Added regression checks for foreground rules, scroll accumulation, volume limits, import validation, settings persistence, and compiled UI controls.

## 0.4.0

- Edge activation consumes scroll events, including the physical outermost pixels outside the desktop work area. Cached monitor geometry keeps Windows calls off the low-level hook thread.
- Edge scrolling only reveals or keeps the overlay open; it never changes the last hovered wheel. Only actual wheel bounds select a volume target.
- Added independent wheel profiles with create, duplicate, rename, remove, and quick tray switching. Existing settings migrate into Default.
- Added four-edge and monitor selection, position along the edge, spacing controls, and matching slide/scale direction.
- Added optional GitHub update notifications, manual checking, and trusted release links. No automatic downloads or installation.
- Added regression checks for edge event suppression, profile migration/identity, placement, all-edge motion, and stable update parsing.
## 0.3.1 — first public release

- Per-user Windows installer with bundled runtime, Start menu/configuration shortcuts, graceful upgrades, and an uninstaller that retains preferences.
- Native Windows tray app with optional sign-in startup and a separate configuration studio.
- 1–12 customizable wheels with persistent ordering, names, colors, channel/mix bindings, volume, and mute controls.
- Wave Link 3 and legacy protocol discovery, live updates, and automatic reconnection.
- Transparent overlay with slide-and-grow entrance and slide-and-shrink exit.
- Settings tab with 50–180% wheel sizing and an animation toggle.
- Opening scroll reveals the wheels without changing any volume. A 250 ms quiet gap enables subsequent adjustments.
- Wheel hover tooltips removed.
- Windows build scripts and regression tests for input routing, WPF controls, animation, settings, and both mixer protocols.
