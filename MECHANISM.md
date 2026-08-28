# Technical overview

F1 Speed Manager uses a clock multiplier rather than editing gameplay values. I chose an external controller so the game's executable, packaged assets, and saves remain untouched.

## Supported executables

Each game has its own compatibility profile. The controller uses the detected executable path to select a profile, then verifies the Steam app, build, and complete executable hash before enabling writes. Launchers at the installation root are ignored.

| Property | F1 Manager 2023 | F1 Manager 2024 |
| --- | --- | --- |
| Steam app | 2287220 | 2591280 |
| Steam build | 16843164 | 17356935 |
| Executable below install folder | `F1Manager23\Binaries\Win64\F1Manager23.exe` | `F1Manager24\Binaries\Win64\F1Manager24.exe` |
| File size | 471,273,984 bytes | 553,445,376 bytes |
| Architecture | AMD64 / PE32+ | AMD64 / PE32+ |
| PE timestamp | 1734356702 | 1739474423 |
| Image size | 480,907,264 bytes | 561,684,480 bytes |
| SHA-256 | `d6e8f3ba892d65e947836f90e81ad590fef4720f6a2e6641832482427b8afc36` | `1198feb7b1f39653fe51f04c9b0acb4d125a67d0e8ba6bda1fa353b3368ab356` |

The complete file hash and Steam build gate compatibility. A game update requires a new review; renaming an executable or changing its manifest does not bypass the hash check.

The controller requires one supported game instance. It rechecks that selection before installation and approximately once per second while connected. An ambiguous selection stops control and triggers reset of the previously verified clock. It never switches an active memory handle to another game.

When the current game exits, the window waits for another supported game. Each new connection starts at extra 1x. The native clock markers remain specific to each game, preserving compatibility with the matching wrappers from earlier releases.

## Clock multiplier

The main executable imports `KERNEL32!QueryPerformanceCounter` (QPC). The controller redirects only that import to a small native wrapper embedded in the application. QPC frequency is left unchanged. [Microsoft: QueryPerformanceCounter](https://learn.microsoft.com/en-us/windows/win32/api/profileapi/nf-profileapi-queryperformancecounter)

```text
virtual_delta = real_delta × active_extra_multiplier
```

Rate changes preserve the accumulated counter value. A native lock serializes updates across threads, and out-of-order samples are clamped to the most recent real sample.

The C# controller renews a real-time lease approximately every 100 ms. The wrapper returns to 1x if the one-second lease expires. When expiry falls between clock calls, the interval is split at the deadline so only the pre-expiry portion uses the previous multiplier. A late controller heartbeat also requests normal speed.

The wrapper contains no Cheat Engine code or runtime dependency. It does not modify Unreal world fields or actor time-dilation values.

## Process and address validation

The controller identifies the target before enabling writes:

1. It requires one matching game process and verifies the Steam app, install path, and build.
2. It holds the executable open read-only and checks its complete SHA-256.
3. It parses the PE import table to find a unique, aligned, named QPC entry. It scans executable sections for the expected counter-to-seconds conversion instructions. Game addresses are discovered at runtime, not hardcoded. [Microsoft: PE format](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format)
4. It compares loaded headers, image size, and timing instruction bytes with the verified file, then checks architecture and records the process creation time.
5. It resolves the real QPC implementation in the corresponding Windows System32 module and compares its code bytes. The import must point to that function or to this tool's exact recognized wrapper.
6. It checks memory regions and revalidates process creation time when opening the write handle, guarding against process-ID reuse.

Installation allocates 8 KiB: one executable/read-only code page and one read/write state page. Code bytes, allocation identity, protections, marker, original function pointer, and allowed multipliers are validated.

An atomic compare/exchange installer changes the single import pointer only if it still has the expected value. The import page's original protection is restored afterwards. Multiplier changes then write only the wrapper's private state.

Heartbeats recheck import ownership and state identity. Multiplier changes and reset perform full wrapper validation. An error disables control; the independent lease provides the fallback.

## Reset and lifetime

Reset restores a 1x clock slope while keeping the accumulated time offset. Immediately removing the wrapper could move the clock backwards or free code that game threads are still executing. The allocation therefore stays resident at normal rate until the game exits.

Windows' system clock and QPC implementation are not patched. Imports in other processes or unrelated DLLs are not redirected. No on-disk game files, save files, gameplay-value addresses, DRM, anti-cheat, or security protections are modified or bypassed.

## Limitations

QPC can also feed engine frame pacing and profiling. Clock scaling cannot guarantee proportional simulation throughput or identical behavior in every subsystem. The game's own speed changes remain effective. Higher multipliers are experimental; audio synchronization, long-session stability, and deterministic race outcomes are not guaranteed. Only the executable fingerprints above are supported.
