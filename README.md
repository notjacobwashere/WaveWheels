# WaveWheels

An osu!-inspired control surface for **Windows audio and Elgato Wave Link**, one scroll away from the edge of your screen.

[Download the Windows release](https://github.com/notjacobwashere/WaveWheels/releases/latest) · [Report an issue](https://github.com/notjacobwashere/WaveWheels/issues)

![WaveWheels configuration studio with example channels](docs/images/studio.png)

## What it does

- Runs quietly in the Windows system tray; configuration opens when you choose it.
- Reveals transparent volume wheels with a smooth slide-and-grow animation.
- Saves independent profiles, with quick tray switching and optional switching for the focused app.
- Tunes scroll steps, acceleration, and hide delay; supports per-wheel volume limits.
- Imports and exports profiles without replacing existing layouts.
- Supports left, right, top, or bottom edges, a chosen monitor, position along the edge, and wheel spacing.
- Finds newer stable releases and downloads, verifies, and installs them on request.
- Keeps the opening scroll gesture from changing volume. Pause briefly, then scroll over a wheel to adjust it.
- Controls Wave Link input channels live, with independent whole-channel or per-mix volume and mute targets.
- Supports 1–12 wheels with custom names, colors, order, and channel bindings.
- Provides drag reordering, keyboard ordering, 50–180% wheel sizing, and an animation toggle.
- Offers optional startup at sign-in, manageable in Task Manager → Startup apps.
- Retains bindings while disconnected and reconnects automatically.

## Download and run

1. Download the **Setup.exe** installer from [Releases](https://github.com/notjacobwashere/WaveWheels/releases/latest).
2. Run Setup and install. First-time installs include a short, skippable intro to the controls and settings before configuration opens. Existing installations and saved settings bypass it. The runtime is bundled; no separate .NET download or administrator account is needed.
3. Choose **Open WaveWheels configuration** when Setup finishes, then customize the Windows volume wheel, or open Wave Link to bind multiple wheels to its channels. Select **None** for an unbound Wave Link wheel.

Requires Windows 10/11 x64. Wave Link is optional: without a connection, one wheel controls the default Windows playback device. Multiple channel wheels require a connected Wave Link installation. Setup installs for your user in `%LOCALAPPDATA%\Programs\WaveWheels`, adds Start menu shortcuts, and registers an uninstaller in Windows Settings → Apps. An optional desktop shortcut is available.

Use **Configure WaveWheels** in the Start menu or click the system tray icon near the clock to open configuration later. The tray icon may be under the overflow arrow.

For a portable alternative, extract `WaveWheels-0.8.0-windows-x64.zip`, keep the `App` folder together, and run `App/WaveWheels.exe`. Only the smaller portable build requires the separately installed [x64 .NET 9 Windows Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0).

The configuration window stays closed on normal launch and at Windows sign-in. Closing configuration leaves the tray app running. Use **Quit WaveWheels** in the tray menu to exit.

## Use your wheels

Scroll at your configured monitor edge to reveal the overlay. The entire opening scroll burst leaves volume unchanged. Stop scrolling for at least **250 ms**, then hover an actual wheel and scroll again to change its volume. Scrolling on the activation strip never adjusts the last hovered wheel and is consumed so it does not scroll the application underneath. You can also drag the wheel up/down. Changed digits roll and fade for 150 ms while unchanged digits stay still. Reaching the ring endpoint at maximum produces a quick spark burst and a one-second glow; staying capped does not retrigger it. Click its speaker button to mute or unmute the same channel/mix target; mute retains its level.

Open **Settings** to choose wheel size or disable slide-and-grow motion. Larger stacks fit the available display height. Drag the grip in the wheel list and drop on the highlighted insertion line, use the row arrows, or press **Alt+Up/Down** to reorder.

![Settings page showing wheel size and motion controls, using example data](docs/images/settings.png)

Settings save automatically in `%LOCALAPPDATA%\WaveWheels\settings.json`. They are separate from the app folder. No personal settings are bundled in downloads.

## Effects

**Effects** provides a live preview and controls for bloom, sparks, mute movement/timing, and surface colors. **Restore defaults** returns every effect to its original value. Each customization page has its own scoped reset; wheel resets retain channel bindings, and profile resets retain saved layouts.


## Profiles, placement, and updates

Use **Profiles** to create, duplicate, rename, or remove layouts. Each profile keeps its wheel count, ordering, names, colors, and channel/mix bindings. The first launch migrates existing wheels into **Default** without changing mixer volumes. The tray's **Profiles** submenu switches layouts quickly. Size and placement are shared across profiles.

Use **Placement** to choose any of the four edges, all displays or a specific display, the position along that edge, and spacing between wheels. The activation strip is a reveal area, not a fader; move onto a wheel to adjust it.

Use **Updates** to check manually or turn automatic notifications off. Automatic checks contact the public GitHub Releases API at launch and every six hours. No saved wheel settings are sent. Choose **Stable** (default) for official releases or **Beta** to include GitHub prereleases and beta/RC version tags. Only versions newer than your installed build produce update notifications. The **Beta archive** lists published betas from up to 500 recent releases, including older builds; select one and choose **Install selected beta** to install it explicitly. Downgrades keep saved settings, but older builds may not understand newer options. Draft releases are excluded. To leave a beta, use **Back to stable → Install latest stable**. This allows an intentional downgrade and switches future checks to Stable while preserving settings. Choose **Download and install** to download the official Windows Setup with progress and cancellation. WaveWheels checks its exact release asset, expected size, and SHA-256 digest from GitHub before starting Setup, then saves settings and exits. Setup upgrades the existing installation and reopens configuration. Failed or canceled downloads leave the running app unchanged; you can retry. Canceling Setup leaves the current version installed. **Open release page** remains available, including when a release has no verifiable installer. Isolated test profiles cannot install updates. Downloads are cached under `%LOCALAPPDATA%\WaveWheels\updates`.
## Automatic profiles and sharing

On **Profiles**, turn on automatic switching and add executable names such as `discord.exe` or `game.exe` to each profile. Separate names with commas, semicolons, or new lines, or use **Browse app**. A matching app must stay focused briefly before switching. The first matching profile wins. An unmatched app restores your manually selected profile; a manual selection stays in effect until focus changes. Switching layouts does not change mixer volumes. Foreground executable names are checked locally; no window titles or activity history are collected or transmitted.

Use **Export profile** or **Export all** to save a `.wavewheels.json` file. **Import profiles** adds independent copies and resolves duplicate names. Files preserve wheels, bindings, volume limits, and app rules, but exclude live levels and global settings. Check channel bindings when moving between PCs.

In **Settings**, choose 1–10% per scroll notch, enable or disable acceleration, and set the hide delay from 0.5–5 seconds. Fractional hardware input accumulates into complete configured steps, with fresh input state after a volume boundary. Hold **Shift** over a wheel to snap between larger increments, with a 180 ms cooldown. Its separate setting defaults to 20% and supports 5–50%. **Show Shift increment dots** is off by default; enable it for dots at the snap positions, without numeric labels. Animation effects respect the animation setting and Windows motion preference. In each wheel editor, set **Maximum volume**. The cap applies to changes through WaveWheels; external mixer controls can still exceed it. Duplicate wheels targeting the same channel and mix use the lowest cap in the active layout. Setting a limit does not immediately change the channel volume.

## Application blacklist

In **Settings → App blacklist**, add an executable name such as `game.exe`, or use **Browse app**. Names match exactly and case-insensitively, including dotted names; window titles and wildcards are never used. While a listed executable is focused, WaveWheels hides its overlay and ignores volume, mute, and preview interactions. Other apps remain unaffected, and configuration remains accessible from the tray.

**Enable app blacklist** turns suppression on or off without removing entries. It starts enabled with an empty list, which has no effect. Use **Remove selected** to delete an entry. Up to 50 names are saved locally; no foreground-app history is recorded or sent anywhere. Automatic profile switching can stay enabled independently.

## Startup and upgrades

Enable **Start at Windows sign-in** on the **Startup** page to register the per-user Windows Run entry. Startup is off until enabled. Windows starts the app at user sign-in, not before login. Task Manager can enable or disable the entry.

Use the in-app installation button or run a newer Setup to upgrade an installed copy. Releases before 0.6.0 need a one-time Setup upgrade to gain the in-app install button. Setup closes the installed app gracefully and preserves saved preferences. If startup was already enabled, it updates that registration to the installed location. Uninstall removes the app and its startup entry while keeping preferences for a future reinstall.

When switching from an early portable build, quit that older tray instance first. For manual portable upgrades, quit before replacing files; if you move the folder, toggle startup off and on to update its path.

## Audio modes

An amber **Windows audio mode** banner means Wave Link is disconnected. Exactly one wheel controls Windows master volume and mute for the current default playback device, including changes made in Windows or other apps. Its name, color, and volume limit save separately from your Wave Link profiles. Adding and reordering wheels, channel binding, and profile editing are unavailable in this mode. No playback device disables the wheel until a device becomes available.

When Wave Link connects, the banner turns green and your saved channel wheels return automatically. Disconnecting restores the Windows wheel. Switching modes never copies volume or mute between the two mixers. The **Connection** page can find and start installed desktop/Store versions of Wave Link, or lets you locate its executable manually. Discovery continues automatically in the background; Wave Link launches only when requested.

The default fallback name is **Windows Audio**. Channel and mix-scope fields are hidden for this wheel.

Windows master volume uses Microsoft's [Core Audio endpoint-volume API](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nn-endpointvolume-iaudioendpointvolume). Tests use an injected backend; the local native smoke check only reads volume and mute.

By default, speaker buttons are hidden: click anywhere on the wheel or its label to mute/unmute, and scroll to change volume. **Settings → Show a dedicated mute button** restores the speaker control; only that button toggles mute in this mode, and dragging elsewhere adjusts volume. Restore defaults turns button mode off.

Wheels now open in a short stagger, Shift volume steps give a directional spring bounce, and unmuting restores the normal color with a soft pulse. These effects respect the motion setting and Windows reduced motion.

## Wave Link compatibility

WaveWheels discovers the local service port from Wave Link's `ws-info.json` and checks newer and legacy fallback ports. The **Connection** page provides a manual local port override. Connections are limited to `127.0.0.1`; no account or Stream Deck is required.

Wave Link 3 provides overall channel and per-mix targets. Legacy protocol connections provide Monitor and Stream targets. While connected, the app controls Wave Link **input channels**. While disconnected, it hides those wheels and shows one Windows master-volume wheel instead. Channel and mix identifiers are saved, so recreating a channel may require rebinding it.

This is an independent project using an unofficial local protocol. Read-only discovery has been checked with Wave Link 3, and automated simulator tests cover both protocol variants. A topmost overlay may not appear over exclusive-fullscreen applications or Windows secure surfaces. See [validation details](docs/VALIDATION.md) for the limits of testing.

## Build and test

On Windows, install the .NET 9 SDK and Node.js 22 or newer, then run:

```powershell
./Build.ps1
./Test.ps1
```

The app has no third-party NuGet dependencies. Tests use isolated profiles and fake local mixer servers; they do not alter the real mixer or register startup. Compiled application files are written to `App/`, and test output goes to `artifacts/`.

Create the portable release archive with:

```powershell
./Package.ps1
```

To build Setup, install Inno Setup 6 and run `./BuildInstaller.ps1`. This downloads official .NET runtime packs from NuGet and bundles them in the installer. `TestInstaller.ps1` runs only on disposable GitHub Actions runners, where it verifies installation, an upgrade while the app is running, uninstall, shortcuts, startup registration, and retained settings.

For manual testing with a separate settings profile:

```powershell
./App/WaveWheels.exe --settings --data-dir "C:\path\to\test-profile"
```

`--settings` opens configuration explicitly. `--shutdown` requests a graceful exit from the existing instance for the same profile. Startup registration is disabled for isolated profiles. GitHub Actions builds and tests on Windows for pushes and pull requests.

## Credits

Wheel interaction and appearance are inspired by osu!. The drawing code and procedural artwork are original; no osu! code, fonts, logos, or sounds are bundled. Windows supplies the UI fonts and speaker icons.

Protocol references: [Wave Link 3 implementation notes](https://github.com/Agash/WaveLink.Client/blob/main/PROTOCOL.md) and [legacy implementation reference](https://github.com/Professor-Melvin/ElgatoWaveLinkSDK/blob/main/WaveLinkClient.js). WaveWheels is not affiliated with or endorsed by Elgato or osu!.

## License

Copyright 2026 notjacobwashere. Licensed under the [Apache License, Version 2.0](LICENSE). See [NOTICE](NOTICE).
