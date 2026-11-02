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

## Updates

- **"Couldn't check for updates."** The app couldn't reach GitHub. Check your internet connection and try again.
- **"Couldn't update."** The message says why. You can always download the newest installer from the
  [releases page](https://github.com/jfreema/Jareds-Cool-Windows-Utils-App/releases) and open it over the copy you have:
  it keeps your settings.
- **The portable version "can't replace itself".** It's in a folder you can't write to, like Program Files. Move
  JaredsWindowsUtils.exe somewhere of your own, like your Documents folder, or download the new version by hand.
