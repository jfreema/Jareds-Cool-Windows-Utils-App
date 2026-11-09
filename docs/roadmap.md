# Roadmap

The Windows app gets the Mac app's features one at a time, a release each, so each can be tried on a real Windows PC
before the next one starts. This is Jared's plan from November 2, 2026, with GIFs and a temperature readout moved up
on November 9. Tick items off here as they ship.

| Release | Feature | Status |
|---|---|---|
| 0.1.0 | [Window snapping](#010-window-snapping), with the tray icon, the menu, Open at Login, About and Check for Updates | [Released](https://github.com/jfreema/Jareds-Cool-Windows-Utils-App/releases/tag/v0.1.0) November 2, 2026 |
| 0.2.0 | [Screenshot tool](#020-screenshot-tool), without GIFs | [Released](https://github.com/jfreema/Jareds-Cool-Windows-Utils-App/releases/tag/v0.2.0) November 2, 2026 |
| 0.3.0 | [Record GIFs](#030-record-gifs) with the screenshot tool | In progress |
| 0.4.0 | [Temperature](#040-temperature) | In progress |
| 0.5.0 | [Stay awake with the lid closed](#050-stay-awake-with-the-lid-closed) | Not started |
| 0.6.0 | [Keyboard cleaning](#060-keyboard-cleaning) | Not started |
| 0.7.0 | [Guitar tuner](#070-guitar-tuner) | Not started |
| 0.8.0 | [Drum machine](#080-drum-machine) | Not started |

**Not coming to Windows,** which does these itself: App Preview (the taskbar shows an app's windows when you rest the
pointer on it), Show Hidden Icons (the **^** in the taskbar's corner) and the Mac's lock shortcut (Windows key+L).

Each phase ends with a release that Jared installs and tries. He asked on November 2, 2026 for 0.2.0 to follow 0.1.0
straight away, without waiting for his tryout, and on November 9 for GIF recording and a temperature readout next.
Within a phase, fixes go out as 0.x.1, 0.x.2 and so on (see [AGENTS.md](../AGENTS.md#versioning)).

## 0.1.0: Window snapping

The app itself, and the first feature. Shortcuts are Win+Alt+arrow keys, where the Mac app has control+option. 0.1.0
used Ctrl+Alt+arrow keys, which 0.1.1 moved off, since Ctrl+Alt types accented letters on many keyboards and code
editors use it (see [AGENTS.md](../AGENTS.md#what-the-app-is)).

- [x] Tray icon: the J in shades, white on a dark taskbar and black on a light one
- [x] The bubble pop menu, opening beside the tray icon, with Open at Login, About, Check for Updates and Quit
- [x] Win+Alt+Left and Win+Alt+Right: halves, or thirds on external displays, and on to the next display
- [x] Win+Alt+Up: two thirds, then the whole display
- [x] Win+Alt+Down: around the quarters, or sixths
- [x] Open at Login, through the list Task Manager shows as Startup apps
- [x] About, with the version
- [x] Check for Updates, for the installed and the portable version
- [x] Installer, for the current user without an administrator's password, and a portable exe, on GitHub Releases
- [x] A self-test and a trial of the installer, updates and uninstaller on GitHub's Windows machines before each release
- [x] Released
- [x] 0.1.1: the shortcuts move from Ctrl+Alt to Win+Alt, caught ahead of Windows, which has Win+Alt+Up and Down
  ([released](https://github.com/jfreema/Jareds-Cool-Windows-Utils-App/releases/tag/v0.1.1) November 2, 2026)
- [ ] Tried on Jared's Windows PC

## 0.2.0: Screenshot tool

As on the Mac, but without recording GIFs, which came in 0.3.0.

- [x] Win+Alt+P snips part of the screen, or a whole window with Space; Esc cancels
- [x] A menu where you let go: Copy to Clipboard, Copy Text, Open in Editor, Save
- [x] Copy Text reads the words in the snip on the PC itself (Windows' own text recognition)
- [x] The bubble pop editor, its tools down the left: box, line, arrow, highlight, blur, text, eraser
- [x] Colors, opacity, line thickness and text sizes; click a shape to move, resize or delete it
- [x] The editor's single-key shortcuts, and Ctrl+Z, Ctrl+C and Ctrl+S
- [x] The self-test snips, copies, reads text and draws in the editor on GitHub's Windows machines before each release
- [x] Released
- [ ] Tried on Jared's Windows PC

## 0.3.0: Record GIFs

- [x] Win+Alt+G records part of the screen as a GIF, like scrolling through a page, turned on with an option under the
      screenshot tool's switch, as on the Mac
- [x] Drag across part of the screen, or click a window, or off any window for the whole display; Esc cancels
- [x] A red line around what's recording, and Stop under it, both kept out of the GIF; it stops by itself after a minute
- [x] A menu where the pointer is, with how long and how big the GIF is: Copy to Clipboard (as a file) and Save
- [x] The self-test records a window and most of the screen on GitHub's Windows machines before each release
- [ ] Released
- [ ] Tried on Jared's Windows PC

## 0.4.0: Temperature

Jared asked on November 9, 2026 for two readings, the processor and another inside the PC, like the motherboard, in °F
as on the Mac. He chose to go without a driver for now: Windows itself shares only some temperatures, so a PC that
doesn't share one says so.

- [ ] A Temperature section in the menu: the processor, and one more reading, each in °F with the Mac's pill, from Cool
      to Critical
- [ ] What Windows shares without a driver or an administrator: the firmware's thermal zones, the graphics card's
      temperature, as Task Manager shows it, and the drive's
- [ ] Released and tried

## 0.5.0: Stay awake with the lid closed

Keeps a laptop running with its lid shut.

- [ ] Stay Awake with Lid Closed, with Plugged In Only, Online Only and a low power option
- [ ] What it's doing now in the menu, and in the tray icon's tooltip
- [ ] Puts Windows' own lid setting back if the app quits or crashes
- [ ] Released and tried

## 0.6.0: Keyboard cleaning

- [ ] Turns off the keyboard and touchpad for a few minutes while you wipe them, with a countdown window
- [ ] Caps Lock stays as it was
- [ ] Released and tried

## 0.7.0: Guitar tuner

The pitch detection is ported from the Mac app already, with its tests, and waits for this phase.

- [ ] The note you're playing and how sharp or flat it is, in a bubble pop window
- [ ] Listens only to built-in and wired microphones, never a Bluetooth headset's, and plays no sound
- [ ] Released and tried

## 0.8.0: Drum machine

The sequencer and the drum sounds are ported from the Mac app already, with their tests, and wait for this phase.

- [ ] Pads to tap a beat on, with a metronome and a loop to play along to
- [ ] Released and tried
