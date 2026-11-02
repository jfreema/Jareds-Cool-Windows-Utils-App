# Agents

Notes for any coding agent working on Jared's Cool Windows Utilities App.

## What the app is

The Windows version of [Jared's Cool Mac Utilities App](https://github.com/jfreema/Jareds-Cool-Mac-Utils-App): a tray
app with the same bubble pop menu, getting the Mac app's features one at a time. [docs/roadmap.md](docs/roadmap.md)
tracks which are done: tick items off there as they ship. Jared asked for 0.2.0 to follow 0.1.0 straight away; check
with him before starting the phases after it. 0.1.0 has window snapping. It's C# on .NET 10 with WPF, for Windows 10 and 11, x64. It ships on GitHub Releases as an installer
and a portable exe, not on the Microsoft Store. [README.md](README.md) is the user guide.

The Mac app is the reference for how a feature behaves and looks. Port its logic into pure, tested C# rather than
reinventing it, and keep its labels, with the shortcuts spelled out as Windows writes them: "Win+Alt+arrow keys".

**Shortcuts** are Win+Alt and one key where the Mac app uses control+option: Win+Alt+arrow keys snap windows,
Win+Alt+P snips (0.2.0) and Win+Alt+G will record GIFs (0.7.0). Jared chose them on November 2, 2026, asking for no
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
`--test-window [fixed]`, and the `updateFeed` setting, which points Check for Updates elsewhere.

## Code

| Area | Files |
|---|---|
| Launch, one copy at a time | `Program` (modes for the installer: `--quit`, `--uninstall`; a newer copy takes over from an older one, and a second copy opens the first one's menu), `App` (wires the features to the menu), `AppInfo`, `Log` |
| Menu and windows | `Ui/MenuWindow` (the tray icon's menu), `Ui/Controls` (bubble pop buttons, chips, switches, cards, drawn in `OnRender`), `Ui/Bubble` (colors and fonts), `Ui/BubbleWindows` (alerts and About), `Ui/Frontmost` |
| Tray icon | `Tray/TrayIcon` (its own `Shell_NotifyIcon`, re-added when Explorer restarts, recolored for a light or dark taskbar) |
| Window snapping | `Snapping/WindowSnapper` (shortcuts and status), `Snapping/WindowMover` (moves windows on a thread of its own), `Snapping/Displays`, and in Core `SnapLayout` (pure geometry) |
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
- **The hidden window is a real top-level window,** not a message-only one, so that shutdown and the installer's Restart
  Manager can ask the app to close. It handles `WM_CLOSE` and `WM_ENDSESSION` by quitting properly, which removes the
  tray icon.
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
- **WPF can't be trimmed,** so the app carries all of .NET: the installer is about 45 MB and the portable exe about
  60 MB.

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
