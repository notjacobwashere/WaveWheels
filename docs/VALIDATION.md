# Validation · WaveWheels 0.3.1

Checked October 17, 2026 on Windows.

- Clean app publish and test build, zero warnings/errors.
- A two-second continuous opening scroll targets no wheel. Every event extends the 250 ms quiet gap. Direct controls stay blocked before the gap; the first later scroll targets the hovered wheel.
- Opening events keep their ignored result after activation, so delayed dispatch cannot turn them into writes. Moving outside the overlay while scrolling cannot bypass the pause. Reopening resets the guard. Explicit tray/configuration preview permits adjustment immediately.
- The actual overlay controls plus the production input router were driven with fixture channel states. The whole opening gesture leaves all three levels unchanged through both the global routing path and the control's direct Adjust path. After the pause, lower and top wheel inputs affect only the chosen channel, starting with fresh scroll accumulation.
- Actual wheel controls have no ToolTip. Settings navigation and the size slider, preferences persistence, reordering, transparency pixels, and slide/scale animation clocks pass the existing WPF component suite.
- Modern fake Wave Link regression passes: handshake, channel/mix enumeration, scoped volume/mute writes, retained levels on mute, external changes, channel removal, and reconnection. The Wave Link transport implementation is unchanged.

Component renders use fixture channels without controlling desktop input or writing real mixer volumes. Physical edge scrolling, mixed-DPI monitor behavior, and exclusive-fullscreen visibility remain manual checks.
