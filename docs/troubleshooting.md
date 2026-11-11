# Troubleshooting

The app keeps a log in `%LocalAppData%\JaredsWindowsUtils\log.txt`: paste that path into File Explorer's address bar to
open the folder. The log says what the app did and why something didn't work.

## Installing

- **"Windows protected your PC."** Microsoft Defender SmartScreen warns about apps it doesn't know yet, and this one
  isn't signed with a paid certificate. Click **More info**, then **Run anyway**.
- **Your browser says the file isn't commonly downloaded.** The same reason. In Edge, open the downloads list, click
  **…** beside the file, then **Keep**. In Chrome, click **Keep** in the downloads list.
- **Windows asks for an administrator's password.** It shouldn't: the installer installs for you alone, in
  `%LocalAppData%\Programs\JaredsWindowsUtils`. If your PC is managed by an organization, it may not allow apps there.
  Try the portable version instead.

## The tray icon

- **The icon isn't in the taskbar's corner.** Windows tucks new icons away under the **^** beside the clock. Click the
  **^**, then drag the app's icon (the J in shades) onto the taskbar. On Windows 11 you can also turn it on in
  **Settings → Personalization → Taskbar → Other system tray icons**.
- **The app isn't running at all.** Open **Jared's Windows App** from the Start menu. Turn on **Open at Login** in its
  menu so it starts each time you sign in.
- **Open at Login is on, but the app doesn't start.** It may be turned off in Task Manager: open Task Manager, choose
  **Startup apps**, and enable **Jared's Cool Windows Utilities App**. Turning **Open at Login** off and on in the menu
  does the same.

## Window snapping

- **Win+Alt+arrow keys do nothing.** Make sure **Snap Windows with Win+Alt+arrow keys** is on in the menu. If the menu
  says **Windows didn't let the app watch for Win+Alt+arrow keys**, security software may be blocking it: the app
  watches the keyboard for its shortcuts, so that it gets them before Windows does. Opening the menu tries again.
- **A window of an app run as administrator doesn't move.** Windows doesn't let other apps move those windows, or see
  the keys you press in them, so Windows' own Win+Alt+Up and Win+Alt+Down work there instead. Run that app normally, or
  snap it with Windows' own Windows key+arrow keys.
- **A window doesn't fill its column exactly.** Some apps keep a minimum size, and some can't be resized at all. The app
  centers those on the column and keeps them on the display.
- **A full-screen window doesn't move,** like a game or a video. Leave full screen first.

## Screenshot tool

- **Win+Alt+P does nothing.** Make sure **Snip the Screen with Win+Alt+P** is on in the menu. If the menu says
  **Windows didn't let the app watch for Win+Alt+P**, security software may be blocking it, as for window snapping.
  Opening the menu tries again.
- **Win+Alt+P does nothing while an app run as administrator is in front.** Windows doesn't let other apps see the keys
  you press in it. Click another window first.
- **PowerToys shows crosshairs instead.** PowerToys' Mouse Pointer Crosshairs also uses Win+Alt+P when it's turned on.
  The app usually gets the shortcut first, but to keep both, give the crosshairs another shortcut in PowerToys.
- **Copy Text says Windows can't read text in your language.** Copy Text uses Windows' own text recognition, which comes
  with a language's optional features. Open **Settings → Time & language → Language & region**, click **…** beside your
  language, choose **Language options**, and add **Optical character recognition** if it's offered.
- **Copy Text finds no text.** Very small or faint text can be too hard to read. Zoom in on it first, or snip a little
  more around it.
- **Closing the editor asks whether to save.** It does when there are shapes you haven't copied or saved. Choose
  **Don't Save** to close it anyway. Quitting the app asks once for all of them.

## Recording GIFs

- **Win+Alt+G does nothing.** Make sure **Snip the Screen with Win+Alt+P** is on in the menu, and **Record GIFs with
  Win+Alt+G** under it. If the menu says **Windows didn't let the app watch for Win+Alt+P and Win+Alt+G**, security
  software may be blocking it, as for window snapping. Opening the menu tries again.
- **Xbox Game Bar saves a clip instead.** Game Bar uses Win+Alt+G to save the last 30 seconds of a game when its
  background recording is on. The app gets the shortcut first while **Record GIFs with Win+Alt+G** is on; when it's
  off, or the app isn't running, Game Bar gets it.
- **The GIF pastes as a picture that doesn't move, or not at all.** Copy to Clipboard copies the GIF as a file, as File
  Explorer copies one. Apps that take files, like Slack, Teams, Outlook and File Explorer, paste it as it is. Apps that
  only take pictures, like Paint, can't: choose **Save…** instead and add the file there.
- **The GIF skips moments.** When the PC is too busy to keep up, the app leaves out frames rather than slow everything
  down. A smaller area helps.
- **The recording stopped by itself.** Recordings stop after a minute.
- **Part of the GIF is black.** Some apps keep their windows out of screenshots and recordings, like streaming video
  with copy protection. Windows' own Snipping Tool sees them the same way.

## Temperature

- **The menu has no Temperature section.** Version 0.4.4 removed it. Version 0.4.0 asked the drive Windows is on how hot
  it is, as it started, and a PC with an Intel RST storage driver froze and stayed frozen until it was turned off.
  Later versions never ask a drive, and the newest asks nothing about temperatures.
- **The PC froze as the app started, or with the menu open.** You have version 0.4.0 or 0.4.1. Don't open its menu:
  download the installer from the [README](../README.md#install) and run it with the old copy closed.

## Updates

- **"Couldn't check for updates."** The app couldn't reach GitHub. Check your internet connection and try again.
- **"Couldn't update."** The message says why. You can always download the newest installer from the
  [releases page](https://github.com/jfreema/Jareds-Cool-Windows-Utils-App/releases) and open it over the copy you have:
  it keeps your settings.
- **The portable version "can't replace itself".** It's in a folder you can't write to, like Program Files. Move
  JaredsWindowsUtils.exe somewhere of your own, like your Documents folder, or download the new version by hand.
