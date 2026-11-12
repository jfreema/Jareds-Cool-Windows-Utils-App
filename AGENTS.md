# Agents

Notes for any coding agent working on Jared's Cool Windows Utilities App.

## What the app is

The Windows version of [Jared's Cool Mac Utilities App](https://github.com/jfreema/Jareds-Cool-Mac-Utils-App): a tray
app with the same bubble pop menu, getting the Mac app's features one at a time. [docs/roadmap.md](docs/roadmap.md)
tracks which are done: tick items off there as they ship. 0.1.0 has window snapping, 0.2.0 the screenshot tool, 0.3.0
GIF recording. 0.4.0 added temperatures, which Jared had removed again on November 11, 2026 (see Things that bite).
0.5.0 is keyboard cleaning, which Jared asked for on November 12, ahead of the lid, which moved to 0.6.0.
Jared asked for 0.2.0 to follow 0.1.0 straight away; check with him before starting each phase after that.
It's C# on .NET 10 with WPF, for Windows 10 and 11, x64. It ships on GitHub Releases as an installer and a portable
exe, not on the Microsoft Store. [README.md](README.md) is the user guide.

The Mac app is the reference for how a feature behaves and looks. Port its logic into pure, tested C# rather than
reinventing it, and keep its labels, with the shortcuts spelled out as Windows writes them: "Win+Alt+arrow keys".

**Shortcuts** are Win+Alt and one key where the Mac app uses control+option: Win+Alt+arrow keys snap windows,
Win+Alt+P snips (0.2.0) and Win+Alt+G records GIFs (0.3.0). Jared chose them on November 2, 2026, asking for no
significant clashes and no three-key shortcuts. Ctrl+Alt is AltGr on many keyboard layouts (Ctrl+Alt+P types ö on
US-International), and VS Code, Visual Studio, JetBrains IDEs, Word and Magnifier use Ctrl+Alt+arrow keys or
Ctrl+Alt+P, while apps leave the Windows key alone. Win+Ctrl+Left/Right switch desktops and Win+Shift+arrows move
windows between displays. `Shortcuts` catches the app's shortcuts with a low-level keyboard hook, ahead of Windows: it
takes over Windows 11's Win+Alt+Up and Down (top and bottom halves) on purpose, and Game Bar's Win+Alt+G ("record the
last 30 seconds"). Win+Alt keys Windows, Game Bar and PowerToys already use: B, D, G, H, K, P (PowerToys' optional
Mouse Pointer Crosshairs), R, S, T, Space, period, numbers, PrtScn, Up, Down. Check any new shortcut against Windows,
Game Bar, PowerToys, Office, browsers and code editors.

**Names:** users see Jared's Cool Windows Utilities App (`AppInfo.FullName`), or Jared's Windows App (`AppInfo.Name`) in
alerts, tooltips and the Start menu. The menu's header says Jared's Cool Windows App (`AppInfo.MenuTitle`). On disk
everything is `JaredsWindowsUtils`: the exe, the install folder (`%LocalAppData%\Programs\JaredsWindowsUtils`), the data
folder with the settings and log (`%LocalAppData%\JaredsWindowsUtils`) and the Open at Login entry.

## Build and test

The app runs only on Windows, but it builds anywhere, so Linux can compile it and run the tests:

```sh
dotnet build JaredsWindowsUtils.sln -c Release   # everything, including the WPF app (EnableWindowsTargeting)
dotnet test JaredsWindowsUtils.sln -c Release    # the Core library's tests
```

On Windows, `scripts/build.ps1 -Version 0.1.0` makes the installer, the portable exe and release.json in `out\`, and
`scripts/try-on-windows.ps1` tries them (see its help). In Claude's cloud containers, `apt-get install dotnet-sdk-10.0`
gets the SDK even where Microsoft's download site is blocked.

**Trying it on Windows from elsewhere:** push to a side branch. The Check workflow builds the app and tries it on a
GitHub Windows runner: `JaredsWindowsUtils.exe --self-test <folder>` takes pictures of the menu and dialogs and snaps a
test window from another process around the screen, then the installer, Check for Updates (installed and portable,
against a 99.0.0 build served on the runner) and the uninstaller are tried for real. On `claude/` branches it commits
what it found to `trial/` on the branch (job.txt with how the run went, results.json, selftest/report.json, the
pictures and the logs), since Claude's containers can't download a run's artifacts. Leave `trial/` out of main. To
delete a side branch from a container, where `git push --delete` is refused, push one last commit whose only workflow
deletes the branch with `github.token`.

Other test hooks: `--update-now` (installs a newer version without asking, quietly), `--settings <file>`,
`--test-window [fixed|moving]` (moving has a square going back and forth, to record), `--press <letter>` (Win+Alt and
the letter, pressed from a process of its own, as the keyboard does while you're in another app), and the `updateFeed`
setting, which points Check for Updates elsewhere.

## Code

| Area | Files |
|---|---|
| Launch, one copy at a time | `Program` (modes for the installer: `--quit`, `--uninstall`; a newer copy takes over from an older one, and a second copy opens the first one's menu), `App` (wires the features to the menu), `AppInfo`, `Log` |
| Menu and windows | `Ui/MenuWindow` (the tray icon's menu), `Ui/Controls` (bubble pop buttons, chips, switches, cards, drawn in `OnRender`), `Ui/Bubble` (colors and fonts), `Ui/BubbleWindows` (alerts and About), `Ui/Frontmost` |
| Tray icon | `Tray/TrayIcon` (its own `Shell_NotifyIcon`, re-added when Explorer restarts, recolored for a light or dark taskbar) |
| Window snapping | `Snapping/WindowSnapper` (shortcuts and status), `Snapping/WindowMover` (moves windows on a thread of its own), `Snapping/Displays`, and in Core `SnapLayout` (pure geometry) |
| Screenshot tool | `Snipping/ScreenSnipper` (Win+Alt+P, then the menu's choices), `Snipping/SnipOverlay` (the screen picker), `Snipping/SnipOffer` (the menu after a snip), `Snipping/SnipEditorWindow` (the editors), `Snipping/SnipCanvas` and `Snipping/SnipSidebar` (the editor's snip and tools), `Snipping/SnipRenderer` (shapes, drawn the same on the canvas and in what's copied or saved), `Snipping/SnipOutput` (clipboard and PNG files), `Snipping/TextRecognizer` (Copy Text), `Snipping/ScreenCapture`, `Ui/Toast`, and in Core `Snipping/SnipEditorModel` (the editor's logic, step for step as on the Mac), `Annotation`, `SnipLayout` and `Blur` |
| GIF recording | `Recording/ScreenRecorder` (Win+Alt+G, through the screen picker, then the menu's choices), `Recording/GifRecording` (takes the pictures and writes the GIF, on threads of their own), `Recording/RecordingControls` (the red line and Stop), and in Core `Recording/GifWriter` (the GIF, frame by frame, as on the Mac) and `RecordingLayout` |
| Keyboard cleaning | `Cleaning/KeyboardCleaner` (asks, then runs the session: window, countdown, Caps Lock), `Cleaning/InputBlocker` (the keyboard and mouse hooks, on a thread of their own, which also ends cleaning), `Cleaning/CleaningWindow`, `Cleaning/CapsLock`, and in Core `Cleaning/CleaningSession` (when it ends) and `CleaningKeys` (what's held back, both Shift keys) |
| Shared | `Shortcuts` (every global shortcut, through a keyboard hook), `Interop/MessageWindow` (the hidden window that hears the tray, other copies and Windows), `Interop/Native`, `OpenAtLogin`, `Updater`, and in Core `Settings` and `Release` (the update feed) |
| Checks | `SelfTest`, `tests/JaredsWindowsUtils.Tests`, `scripts/try-on-windows.ps1` |

Put logic that can be pure, like geometry, parsing and audio maths, in `JaredsWindowsUtils.Core` (plain `net10.0`, no
Windows APIs) with xUnit tests, so it's tested on any machine.

## Things that bite

- **Pixels, not WPF's units, for other windows.** The app is per-monitor DPI aware (v2, in `app.manifest`), so Win32
  calls see real pixels on every display. A window's visible frame is `DWMWA_EXTENDED_FRAME_BOUNDS`: `GetWindowRect`
  includes invisible resize borders a few pixels wide, so `WindowMover.Move` adds them back. An app may resize its window
  as it arrives on a display with another scale, so the mover tries twice, then centers what won't fit.
- **The UI thread must never wait on another app.** Moving a window waits for its app, so `WindowMover` has its own
  thread and skips hung windows.
- **The keyboard hook must answer at once.** `Shortcuts` runs it on a thread of its own with a message loop, since every
  key anyone presses waits for it, and Windows silently takes out a hook that's too slow; the hook goes back in every
  minute and when the menu opens. It keeps the shortcut's key from Windows and the app in front, so it presses an unused
  key (0xE8) while Win and Alt are down: Win pressed and let go on its own opens Start, and Alt on its own opens the
  menu bar of the window in front. The self-test checks both with Notepad. The hook doesn't hear keys pressed in apps run
  as administrator.
- **Keyboard cleaning holds everything back with two more hooks,** `WH_KEYBOARD_LL` and `WH_MOUSE_LL` (a touchpad moves
  the pointer as a mouse does), on a thread of their own that never waits on anything. That thread, not the UI thread,
  ends it: at the deadline, on both Shift keys, and when the UI thread hasn't called `Beat` for 4 seconds, which is the
  Mac event tap's "a hung app gives input back" (Windows would only skip a slow hook, and may then take it out without a
  word). The hook lets by the letting go of a key or button that was already down when it began, which Windows would
  otherwise think was held for good, and the app's own Caps Lock press (`CapsLock.Mine`). Caps Lock is read with
  `GetKeyState` from the cleaning window, once it has the focus, and put back at the end and whenever it differs. A
  touchscreen's touches, and perhaps a precision touchpad's gestures, can reach Windows before any hook: untried on real
  hardware, since GitHub's machines have no touchpad. The self-test puts a hook of its own in first, so it hears what the
  cleaning hooks let by.
- **The hidden window is a real top-level window,** not a message-only one, so that shutdown and the installer's Restart
  Manager can ask the app to close. It handles `WM_CLOSE` and `WM_ENDSESSION` by quitting properly, which removes the
  tray icon.
- **The screen picker shows a picture of the screen,** taken when Win+Alt+P is pressed, so nothing moves while you
  choose, and the snip is cut from it. It has a window per display, each put over its display in pixels, since WPF's
  units differ from display to display. While it's open, and while the menu after a snip is, `Shortcuts.Grab` takes Esc,
  Space, the arrow keys and Enter from whatever has the focus, since Windows may leave the focus in the app you were in.
  The menu after a snip opens before the picker closes, so the focus never goes back to that app in between.
- **The editor draws a shape the way it's saved,** with `SnipRenderer` for both, at the snip's own pixels when copied
  or saved. Shadows are WPF's `DropShadowEffect`, whose blur spreads about 1.5 times as far as Core Graphics' for the
  same radius. Copy puts a PNG on the clipboard, which keeps the display's dpi and most apps paste, and a bitmap for
  apps like Paint.
- **The red line and Stop stay out of GIFs** through `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`, which keeps
  them out of every capture, the self-test's pictures of the screen too: draw them with `Render` to see them. The
  pictures are GDI's `BitBlt` without `CAPTUREBLT`, which would make the pointer flicker, with the pointer drawn in
  after. They're taken and written on threads of their own, and when the writing falls behind a picture is left out,
  as on the Mac. A recording waits in `%TEMP%\JaredsWindowsUtils\Recordings` and is copied as a file, which the next
  copy replaces.
- **There is no temperature feature, and a drive must never be asked.** 0.4.0 added a Temperature section; Jared had it
  removed on November 11, 2026 (0.4.4). 0.4.0 froze his PC (a Dell OptiPlex 5070, both drives behind Intel RST's
  iaStorAC 17.11.3.1010, Windows on the NVMe one): it stopped writing to its disk within seconds of the app starting
  after an update (November 9, 2026) and stayed frozen until he turned it off. **Very likely cause, not reproduced:
  asking the drive its temperature, `IOCTL_STORAGE_QUERY_PROPERTY` with `StorageDeviceTemperatureProperty` (52).**
  Windows' class driver passes it to the controller's driver, here Intel's closed one. Precedent: another app's
  identical request froze three RST PCs, one with this driver version and an NVMe boot drive
  (github.com/Niko1221/Strata/issues/4661). 0.4.1 to 0.4.3 asked only the firmware's thermal zones, carefully, and his
  PC then ran fine, but a desktop shares no real processor temperature without a kernel driver, so Jared dropped the
  feature. If sensors ever come back: no drive, only on Jared's say-so, nothing asked at launch, tried first on a PC
  that isn't his, and **never run `--self-test` or `scripts/try-on-windows.ps1` on his PC** (they ask real hardware).
  What stayed from the fixes: `Settings.Save` writes through to the disk, returns whether it worked, and an unfinished
  `.new` file is finished at load; every log line is written through, so the log says what happened before a PC
  stopped; `Quit` always shuts the app down.
- **Copy Text uses `Windows.Media.Ocr`,** through the Windows SDK's .NET projection (the app targets
  `net10.0-windows10.0.19041.0`). It reads only the languages whose text recognition is installed; without any, the app
  says how to add it.
- **Windows tucks new tray icons away under ^,** so the app shows its menu when it's opened by hand (not at login or
  after an update), and opening it again from the Start menu shows the running copy's menu.
- **Only the app just clicked may come to the front.** The menu closes when it loses the focus, so it must get it
  first; `Frontmost.Bring` lifts the limit when Windows says no, as after an update check.
- **Check for Updates** reads release.json from `releases/latest/download/`, which GitHub redirects to the newest
  release. An installed copy (one with `unins000.exe` beside it) runs the new installer with `/VERYSILENT /RELAUNCH`;
  the installer asks the running copy to quit (`--quit`) before it replaces it and opens the new version after. A portable
  copy renames itself to `.old`, puts the download in its place and starts it with `--replace`; the new copy deletes the
  `.old`. Both check the download's SHA-256 against release.json and the version stamped on it.
- **The release assets keep fixed names** (`JaredsWindowsUtils-Setup.exe`, `JaredsWindowsUtils.exe`, `release.json`), as
  the README's download links and Check for Updates find them by name.
- **Unsigned:** there's no code signing certificate, so SmartScreen warns on first run (see the README). Files the
  updater downloads have no mark of the web, so updates don't warn.
- **WPF can't be trimmed,** so the app carries all of .NET, and the Windows SDK's projection for Copy Text: in 0.4.0
  the installer is about 49 MB and the portable exe about 68 MB.

## Versioning

**Every push to main that changes the app publishes it** as a new version on GitHub Releases: the Release workflow
tests it, builds it, tries it on Windows, then tags the release and uploads the installer, the portable exe and
release.json. Pushes that only change docs, tests or the art scripts don't. So push to main only what's ready, and try
anything else on a side branch first.

- **Anything else,** including fixes to an existing feature: leave the version alone. The workflow takes the newest tag
  and raises its last number, for example 0.1.0 → 0.1.1.
- **A new feature,** a phase in the roadmap: raise the middle number in `Directory.Build.props`, with the last at 0, in
  the same push. For example 0.1.4 → 0.2.0. The workflow publishes a raised version as it is.
- The version lives in `Directory.Build.props` and the tags (`v0.1.0`). The workflow doesn't commit anything back.

## Conventions

Commit straight to main, no pull requests, as jfreema (`143004867+jfreema@users.noreply.github.com`). The art in `art/`
is the Mac app's; `scripts/make-icons.py` and `scripts/make-fonts.py` make the app's icons, tray glyphs, installer
pictures and Nunito fonts from it. Nunito stands in for SF Rounded, under the SIL Open Font License
(`src/JaredsWindowsUtils/Assets/Fonts/OFL.txt`).
