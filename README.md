# F1 Speed Manager

made by Willem Plenter (SkaffaWilly)

I made F1 Speed Manager to add an extra speed multiplier to F1 Manager 2023 and 2024 without changing the game's files or editing gameplay values. It is a small standalone Windows application with buttons and global hotkeys. Cheat Engine is not required.

**Version:** 1.0.0 · **Platform:** Windows x64

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

Hotkeys are global while the application is open. If another application has reserved one of these keys, the tool stops before attaching.

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

The release includes a separate `F1-Speed-Manager-v1.0.0-win-x64.zip.sha256` file for the ZIP download. In PowerShell, run:

```powershell
Get-FileHash -LiteralPath '.\F1-Speed-Manager-v1.0.0-win-x64.zip' -Algorithm SHA256
```

Compare the resulting hash with the value in the `.sha256` file. `SHA256SUMS.txt` inside the ZIP lists the hashes of the individual packaged files. These checks detect file changes; they are not a digital signature or a guarantee of safety.

## License

Copyright (c) 2026 Willem Plenter (SkaffaWilly). Released under the [MIT License](LICENSE).

---

Unofficial utility. Not affiliated with Frontier, Formula 1, or Steam.
