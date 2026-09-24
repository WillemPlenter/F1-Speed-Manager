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

## Optional overlay

The overlay is a separate topmost Windows Forms window with no activation, no taskbar entry, and layered/click-through window styles. It never reads or writes game memory, patches graphics functions, or changes the requested multiplier. F7 toggles only display visibility. Windows documents mouse pass-through for layered windows with `WS_EX_TRANSPARENT`: [Microsoft: layered windows](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features).

The controller publishes immutable snapshots containing the verified process ID, confirmed active multiplier, and real QPC timestamp after each successful heartbeat. The UI polls those snapshots every 100 ms. A missing, invalid, or more than 0.4-second-old snapshot hides the badge rather than displaying an unconfirmed multiplier. The foreground window must belong to that same verified process and have a visible, non-minimized client area.

Only the overlay HWND is created in a temporary per-monitor DPI context; the main controller window keeps its existing scaling behavior. Native client/monitor coordinates are queried in that context and the overlay uses its own current-monitor DPI. The previous thread context is restored after each scoped operation. This avoids mixing a game's DPI coordinates with DPI virtualization in the controller: [Microsoft: mixed-mode DPI scaling](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-improvements-for-desktop-applications).

The badge is clamped inside the visible client area and can be placed in four corners or top center. Top center uses the horizontal midpoint and the same top HUD clearance as the upper corners. Overlay errors disable its display without changing speed control. The overlay is hidden immediately when closing starts and disposed with the controller. External desktop windows are not guaranteed visible over true exclusive fullscreen; windowed or borderless mode is preferred. The overlay requires Windows 10 version 1607 or later.

## Configurable input

All nine actions use validated configurable bindings. The application reserves each standalone shortcut and each unique chord prefix with RegisterHotKey before starting the game controller. A shared root is reserved once, and its single action is handled on release instead of accepting duplicate WM_HOTKEY events.

A dedicated input thread with a message loop installs WH_KEYBOARD_LL and WH_MOUSE_LL hooks. These callbacks run in the installing application, not inside the game. The keyboard hook handles held-prefix chords and the input guard; the mouse hook handles only left-button press/release for the input guard. They do not record pointer coordinates. Modifier state is initialized before installing the keyboard hook and tracked from key events because the current asynchronous key state is not yet updated in the callback. [Microsoft: LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc), [Microsoft: LowLevelMouseProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc).

When a fresh snapshot confirms that the verified game owns the foreground window, a plain Left/Right or A/D event or a left-button event can activate a 175 ms real-time input guard. If the selected extra multiplier is above 1x, the callback synchronously validates the resident clock state and requests extra 1x before forwarding the original event. Repeated/down/up events extend the guard. The controller then silently restores the selected multiplier. No event is consumed or reinjected, controller input is untouched, and the selected speed mode shown by the UI remains stable. This avoids applying an accelerated QPC interval to Unreal's keyboard/mouse repeat window while keeping the existing clock wrapper and supported multiplier set unchanged.

The input state machine retains only necessary modifier bits, the configured held prefix, its focus window and consumed chord keys. It never records ordinary typing. Matching position chords require the foreground tool window or a fresh confirmed snapshot of the verified game's PID. Consumed arrow repeats/releases stay consumed until release, even if the prefix is released first. A chord suppresses the standalone action on prefix release; focus changes and modifier introduction cancel that fallback.

Opening settings unregisters all shortcuts and disposes the prefix hook before recording begins. Read-only recording fields intercept their own focused key messages before text editing, navigation or dialog submission. They record direct keys, Ctrl/Alt/Shift modifiers and function-key prefixes; repeated events and key release never overwrite a captured chord. Focus changes clear pending recording state. No second global recording hook is installed.

Saving temporarily registers all required shortcuts to validate availability, releases them again while the dialog remains open, then persists the validated configuration. Registration or persistence failure retains the previous settings. Closing or cancelling the dialog re-registers the currently saved configuration. Clock heartbeats continue throughout editing, and editing does not request a speed change. An unrecoverable input failure stops the controller and requests normal speed; the native clock lease remains the independent fallback. Closing unregisters all keys and removes both input hooks. Address discovery and compatibility checks remain in the controller; the hooks modify only the already-validated wrapper's requested multiplier through the same guarded engine methods. They do not modify the renderer, gameplay values or game files.

## Interface colour preference

The compact colour menu is a local Windows Forms preference and does not take part in process detection, clock installation, multiplier writes, hotkey handling or overlay validation. Red is the default. Red, Green, Orange, Blue, Purple and Cyan apply one accent throughout the interface; Multicolor uses the existing per-speed and per-action mapping. The external overlay receives only the selected colour mode alongside the already-confirmed speed snapshot and still performs no game-memory access.

The selected mode is stored in `F1 Speed Manager.theme.ini` beside the executable. The parser accepts one named `Color` value from the fixed mode list and rejects missing, duplicate, numeric, unknown or oversized settings. Saving uses a temporary file and replacement so a failed write does not silently apply an unsaved interface state. Invalid saved settings stop startup before any game controller or hotkey manager is started.

## Limitations

QPC can also feed engine frame pacing and profiling. Clock scaling cannot guarantee proportional simulation throughput or identical behavior in every subsystem. The game's own speed changes remain effective. Higher multipliers are experimental; audio synchronization, long-session stability, and deterministic race outcomes are not guaranteed. Only the executable fingerprints above are supported.
