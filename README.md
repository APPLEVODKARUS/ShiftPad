ShiftPad is a small Windows utility for people who use one PC with both a desk monitor and a TV. When you connect a controller it switches the active display to the TV, routes audio to HDMI, and opens Steam Big Picture (or any custom launcher). When you disconnect — display, audio and the launcher all return to the desktop setup.
## Features

- **Automatic display switching** when a gamepad connects: target-only, extend, or clone.
- **Automatic audio routing** with HDMI auto-detect after the display change. If your TV's audio endpoint only appears once the display is active, ShiftPad waits and switches when it shows up.
- **Optional launcher startup**: Steam Big Picture, or any custom executable (Epic, RetroArch, Battle.net, your own front-end).
- **Optional HDMI-CEC** via Pulse-Eight USB-CEC adapter — wakes the TV from standby and selects the correct HDMI input.
- **Configurable activation combo** (sequence of buttons) so just plugging the controller in to charge doesn't trigger anything.
- **Activation/deactivation delays** (0–60 seconds) with a tray countdown — change your mind, cancel from the tray icon.
- **Boot-time recovery** for the case where a previous gaming session ended uncleanly (forced shutdown via Big Picture's "Turn off" button, BSOD, power loss). Without recovery you'd boot into a black monitor with the TV remembered as the only display.
- **HDR survival**: HDR (advanced color) state is captured and re-applied after topology changes — `SetDisplayConfig` doesn't preserve it on its own.
- **Multi-controller disambiguation**: two identical Joy-Cons or two Xbox pads are tracked by their stable `NonRoamableId`, so picking your "primary" gamepad does what you'd expect.
- **30-day free trial**, then a one-time lifetime license. No subscription, no telemetry, no analytics. The app talks to a server only when you activate or refresh your license token.

## Compatibility

| | |
|---|---|
| **Operating system** | Windows 10 (build 1903+, May 2019 Update) and Windows 11 |
| **Architecture** | x64 (works on ARM64 via x64 emulation) |
| **Controllers** | Xbox One/Series, DualShock 4, DualSense (PS5), Switch Pro Controller and Joy-Cons, 8BitDo, Flydigi, GameSir — anything XInput-compatible plus Nintendo HID |
| **Connection** | USB, wireless dongle, Bluetooth — all the same |
| **Displays** | Any number of monitors, any port (HDMI, DisplayPort, USB-C, etc.) |
| **Audio** | Any device that appears in Windows Sound → Output |
| **TV CEC** (optional) | Pulse-Eight USB-CEC Adapter |

## Install

### Via winget

```powershell
winget install ShiftPad
```

(Pending Microsoft mainline merge — see [microsoft/winget-pkgs#380222](https://github.com/microsoft/winget-pkgs/pull/380222).)

### Manual installer

Download `ShiftPad-Setup-X.Y.Z.exe` from [Releases](https://github.com/APPLEVODKARUS/ShiftPad/releases) or [shiftpad.ru](https://shiftpad.ru) and run it.

The installer is a per-machine install into `C:\Program Files\ShiftPad\`. It creates a Start Menu shortcut, optionally a desktop shortcut and an autostart entry. It also registers a small companion `ShiftPadRescue.exe` as a Task Scheduler task that runs at logon to recover display topology if the previous session ended dirty.

## Quick tour

After install, open ShiftPad once and configure:

1. **Target display** — which monitor counts as the "TV"
2. **Display mode** — target-only, extend, or clone
3. **Primary gamepad** — which controller triggers the switch (multiple controllers? assign one as primary)
4. **Audio device** — auto-detect HDMI after the switch, or pick a specific endpoint
5. *(optional)* **Activation combo** — record a 2–4 button sequence so a charging controller doesn't trigger anything
6. *(optional)* **Launchers** — Steam Big Picture and/or a custom path

Close the window. The app sits in the tray. Plug your controller — done.
