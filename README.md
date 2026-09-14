# F1 Speed Manager

made by Willem Plenter (SkaffaWilly)

![F1 Speed Manager logo](source/assets/Logo.png)

I made F1 Speed Manager to add an extra speed multiplier to F1 Manager 2023 and 2024 without changing the game's files or editing gameplay values. It is a small standalone Windows application with buttons and global hotkeys. Cheat Engine is not required.

**Version:** 2.0 · **Platform:** Windows x64

Version 2.0 includes the logo, optional in-game speed display, five positions and configurable hotkeys with direct key recording. The title bar displays **F1 Speed Manager 2.0**. The clock controller is unchanged. Input and placement behavior has been checked locally; compatibility with every in-game panel still depends on the race HUD.

One executable supports both games. It automatically detects the running game and shows its name in the window.

| Game | Supported Steam build |
| --- | --- |
| F1 Manager 2023 | 16843164 |
| F1 Manager 2024 | 17356935 |

## Getting started

1. Extract the ZIP into a writable folder outside the game's installation.
2. Launch `F1 Speed Manager.exe` and start either F1 Manager 2023 or F1 Manager 2024, in either order.
3. Wait for **Connected** and check the detected game name. Every connection starts at extra **1x**.
4. Select a multiplier using the buttons or hotkeys.
5. Optionally enable **Overlay** or press **F7** to show the extra multiplier in the game.

Keep only one supported game running at a time. If both games or multiple instances are detected, control stops and any connected clock is reset. Close the extra game, then reopen the tool. To switch games normally, close the current game before starting the other; the tool can remain open and the new connection starts at 1x. Close older editions of the tool before using this release.

Only the executable is needed to run the tool. The source and technical documentation are included for anyone who wants to inspect or rebuild it.

| Hotkey | Extra multiplier |
| --- | --- |
| F1 | 1x / normal |
| F2 | 2x |
| F3 | 3x |
| F4 | 5x |
| F5 | 10x |
| F6 | Reset to 1x |
| F7, release alone | Show / hide overlay; speed stays unchanged |
| Hold F7 + Up | Previous overlay position |
| Hold F7 + Down | Next overlay position |

These are the default bindings. Hotkeys are global while the application is open. If another application has reserved a required key, the tool stops before attaching. Position chords consume their second key only while this tool or its freshly verified game is foreground; plain arrows pass through normally. A held arrow moves once per press, without auto-repeat.

## Custom hotkeys

Open **Hotkeys…** to change any of the nine actions. **Click a field, then press the desired key or combination**; do not type its name. Press F9 to record `F9`, or hold Ctrl and press F2 to record `Ctrl+F2`. For `F7+Up`, hold F7, press Up, then release both keys. Select **Save** with the mouse. The game does not need to be running. A held prefix must be a function key; letters/navigation keys without a prefix require Ctrl, Alt or Shift. Escape, Windows keys and modifiers alone are not supported. Windows may reserve some function keys or combinations.

A shortcut shared by a single action and a held-prefix chord runs its single action only when released alone. For example, F7 + Up moves the badge and suppresses the F7 show/hide action when F7 is released. Press F7 first, then the arrow. A short press and a long press without an arrow both toggle on release. Changing focus or introducing another modifier cancels the pending single action.

**Restore defaults** fills the default bindings; **Cancel** leaves active bindings unchanged. Save rejects invalid/duplicate bindings and unavailable registrations. While the dialog is open, the application's global reservations and prefix hook are removed so they cannot intercept the keys being recorded. This pauses hotkey actions, without changing the extra multiplier or interrupting clock heartbeats. On Save, registrations are temporarily checked and then released again until the dialog closes. Closing or cancelling restores the saved or previous bindings. If they cannot be restored, speed control stops and requests extra 1x. Recording fields consume navigation/dialog keys while focused; click Save or Cancel instead of using Enter, Tab or Escape to navigate there.

Bindings are saved locally in `F1 Speed Manager.hotkeys.ini` next to the executable, using an atomic replacement when the file already exists. Keep the application in a writable folder. Invalid saved settings stop startup before game access; rename the settings file to restore defaults. Personal settings and logs are not included in the download or source repository.

## In-game overlay

The overlay starts off. The **Overlay: On/Off · F7** button and releasing **F7** alone control the same toggle with the default bindings. Turning the display off does not reset or change the selected speed.

The small badge shows the last confirmed extra clock multiplier, for example **Extra 5×**. It does not read the game's own x1–x16 selector or claim to measure effective race speed. Mouse clicks pass through it, and it does not take keyboard focus.

Use the position dropdown, or hold F7 with Up/Down, to cycle through **Top right, Top left, Bottom right, Bottom left, Top center**. Top center is horizontally centered near the top edge, not in the middle of the screen. The default is top right, with space left below the top edge for the race HUD. Position changes leave visibility and speed unchanged. The badge follows the game window and monitor scaling. No position can be guaranteed clear of every dynamic panel; select another position or use F7 if it overlaps an important element.

The badge appears only over the foreground window of the verified, connected game. It hides on Alt-Tab, minimization, disconnection, errors, closing, or when the last confirmed speed sample is more than 0.4 seconds old. The toggle and position are kept only for the current application session.

Use windowed or borderless windowed mode for the external overlay. True exclusive fullscreen can prevent a separate desktop window from appearing. The tool does not hook the game's graphics renderer or change display settings.

## How speed works

The selected multiplier applies on top of the game's own speed. For example, built-in x16 with an extra 5x targets x80. The displayed number is the extra clock multiplier, not a measurement of the resulting simulation speed. Actual performance varies; the nominal product is not guaranteed.

**Reset removes only the extra acceleration.** If the game is at x16, resetting the tool leaves it at its own x16. The game's pause controls, speed selector, and automatic speed changes remain in control.

The tool does not directly edit money, fuel, tyres, driver statistics, AI parameters, contracts, or saves. Simulation state still advances naturally as the race runs faster.

## Compatibility and safety

- Windows 10/11 x64 with .NET Framework 4.8 or later.
- Either supported Steam game/build from the table above, with its exact executable fingerprint listed in [MECHANISM.md](MECHANISM.md).
- Unknown builds, unexpected clock hooks, and ambiguous process matches are refused. There is no option to bypass these checks.
- Start with 2x in a disposable replay. Higher multipliers are experimental; long-session stability and identical race outcomes are not guaranteed.
- Do not combine this application with another speed tool.
- This executable is unsigned. It allocates memory inside the game and uses a short remote installer thread to redirect the game's QueryPerformanceCounter import. This behavior can resemble process injection to antivirus software. Do not disable security protections to run it.

Closing the window requests extra 1x. A watchdog inside the game also restores extra 1x when the controller's one-second real-time lease expires, including after a controller crash. If the game is suspended, the fallback takes effect on its next clock call.

An **8 KiB clock wrapper** remains in the game's memory at normal rate until the game exits. This preserves clock continuity and avoids making time jump backwards. Reopening the same compatible version reuses the wrapper; restarting the game removes it completely. No game files are modified.

## Troubleshooting

- **Game not detected:** start the actual game, not just its launcher.
- **Multiple game instances:** close all but one supported game, then reopen the tool.
- **Windows error 5:** check that the tool and game run at the same privilege level.
- **Unsupported build or hash:** this release does not support that executable. An update requires compatibility review.
- **Unexpected hook:** close other speed tools and restart the game before retrying.
- **Hotkey unavailable:** close the application using that key, then reopen this tool.

The application creates `F1 Speed Manager.log` next to the executable for diagnostics. Logs are generated locally and are not included in the release. They may contain local installation paths; review them before sharing.

## Building from source

Run `source\build.cmd` to build with the Windows .NET Framework x64 compiler. No modern .NET SDK is required. The script also builds and runs the local self-tests, which never attach to the game.

The original logo is included unchanged as `source\assets\Logo.png` and embedded in the executable. It is also displayed in the upper-right corner of the program window; no separate PNG is needed at runtime. `source\assets\AppIcon.ico` contains seven sizes (16–256 pixels) and is embedded as the Windows and title-bar icon. Both assets are included in the source package; rebuilding the application does not require image conversion.

The native clock is included as `source\Clock.asm` and its assembled `Clock.bin` resource. To reassemble it with NASM, run:

```bat
source\rebuild-clock.cmd "C:\path\to\nasm.exe"
source\build.cmd
```

Optional diagnostic modes:

```bat
"F1 Speed Manager.exe" --inspect
"F1 Speed Manager.exe" --self-test
```

These are the only supported command-line options. `--inspect` performs read-only compatibility checks. `--self-test` runs only in the tool's own process. Speed control is available through the window and hotkeys, not through a command-line write mode. The console executable produced by the build script shows the self-test output.

When adding these files to an existing repository, merge the supplied `.gitignore` rules with your existing rules instead of replacing them.

See [MECHANISM.md](MECHANISM.md) for implementation details and the address-validation checks.

## Download integrity

The download includes a separate `F1-Speed-Manager-v2.0-win-x64.zip.sha256` file for the ZIP download. In PowerShell, run:

```powershell
Get-FileHash -LiteralPath '.\F1-Speed-Manager-v2.0-win-x64.zip' -Algorithm SHA256
```

Compare the resulting hash with the value in the `.sha256` file. `SHA256SUMS.txt` inside the ZIP lists the hashes of the individual packaged files. These checks detect file changes; they are not a digital signature or a guarantee of safety.

## License

Copyright (c) 2026 Willem Plenter (SkaffaWilly). Released under the [MIT License](LICENSE).

---

Unofficial utility. Not affiliated with Frontier, Formula 1, or Steam.
