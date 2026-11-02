<p align="center"><img src="docs/icon.png" width="128" alt="Jared's Cool Windows Utilities App icon"></p>

<h1 align="center">Jared's Cool Windows Utilities App</h1>

<p align="center">A Windows tray app with only the features I use. The Windows version of <a href="https://github.com/jfreema/Jareds-Cool-Mac-Utils-App">Jared's Cool Mac Utilities App</a>.</p>

<p align="center"><a href="https://github.com/jfreema/Jareds-Cool-Windows-Utils-App/releases/latest/download/JaredsWindowsUtils-Setup.exe"><img src="docs/download.svg" width="204" alt="Download Jared's Cool Windows Utilities App"></a></p>

<p align="center">Windows 10 or 11 · 64-bit · <a href="https://github.com/jfreema/Jareds-Cool-Windows-Utils-App/releases/latest/download/JaredsWindowsUtils.exe">Portable version</a> · <a href="https://github.com/jfreema/Jareds-Cool-Windows-Utils-App/releases">All versions</a></p>

<p align="center"><img src="docs/menu.png" width="403" alt="The app's menu: Open at Login, a switch for window snapping with Ctrl+Alt+arrow keys, Thirds on External Displays, and About, Check for Updates and Quit buttons"></p>

<table>
  <tr>
    <td width="50%"><b><a href="#window-snapping">Window snapping</a></b><br>Ctrl+Alt and an arrow key put the window you're in into halves, thirds, quarters or sixths.</td>
    <td width="50%"><b><a href="docs/roadmap.md">Coming next</a></b><br>The Mac app's other features, one release at a time: the screenshot tool is next.</td>
  </tr>
</table>

## Install

1. Open the **JaredsWindowsUtils-Setup.exe** you downloaded. If your browser warns that it isn't commonly downloaded, choose to keep it.
2. If Windows says **Windows protected your PC**, click **More info**, then **Run anyway**. The app isn't signed with a paid certificate, so Windows doesn't recognize it yet.
3. Click through the installer. It installs for you alone, so it doesn't ask for an administrator's password. The app opens and shows its menu.
4. Turn on **Open at Login** at the top of the menu.

The app's icon is in the taskbar's corner, by the clock: the J in shades. Click it for the menu. If Windows tucked it away under **^**, drag it from there onto the taskbar to keep it in view. Opening the app again from the Start menu also shows its menu.

To update, choose **Check for Updates** in the app's menu. It installs the newest version and restarts, keeping your settings.

**Without installing:** download the [portable version](https://github.com/jfreema/Jareds-Cool-Windows-Utils-App/releases/latest/download/JaredsWindowsUtils.exe), JaredsWindowsUtils.exe, and open it from any folder. Check for Updates replaces it where it is.

---

## Window snapping

Hold Ctrl and Alt and press an arrow key to put the window you're working in into halves, thirds, quarters or sixths of its display.

**Turn it on:** it's on from the start. Its switch is **Snap Windows with Ctrl+Alt+arrow keys**, under **Window Snapping** in the menu.

**Use it:**

- **Ctrl+Alt+Left and Ctrl+Alt+Right** snap the window into the column it's mostly in, then move it a column at a time, across displays.
- **Ctrl+Alt+Up** widens it to fill the display. From a third, it goes to two thirds first.
- **Ctrl+Alt+Down** moves it clockwise through the display's quarters, or sixths.

Each display has two columns, or three on external displays with **Thirds on External Displays** on. A laptop's own screen keeps two, since it's too narrow for thirds.

| Ctrl+Alt+Left and Ctrl+Alt+Right | Ctrl+Alt+Down |
|---|---|
| <img src="docs/snapping-columns.svg" width="400" alt="A laptop's own screen in two halves, the left one lit up, next to a larger external display in three thirds, the middle one lit up"> | <img src="docs/snapping-cells.svg" width="400" alt="A laptop's own screen in four quarters, the top left one lit up, next to a larger external display in six sixths, the top right one lit up"> |

Windows' own snapping, with the Windows key and the arrow keys or by dragging a window to an edge, keeps working too.

If the menu says **Another app is using a Ctrl+Alt+arrow key**, another app has taken one of the shortcuts. See [troubleshooting](docs/troubleshooting.md#another-app-is-using-a-ctrlaltarrow-key).

---

## Uninstall

Uninstall **Jared's Cool Windows Utilities App** in **Settings → Apps → Installed apps**. That quits the app and takes it out of Open at Login. Your settings stay in `%LocalAppData%\JaredsWindowsUtils`: delete that folder to remove them too.

For the portable version, turn off **Open at Login** if it's on, choose **Quit** in its menu, and delete JaredsWindowsUtils.exe.

Having problems? See [Troubleshooting](docs/troubleshooting.md).
