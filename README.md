<p align="center"><img src="docs/icon.png" width="128" alt="Jared's Cool Windows Utilities App icon"></p>

<h1 align="center">Jared's Cool Windows Utilities App</h1>

<p align="center">A Windows tray app with only the features I use. The Windows version of <a href="https://github.com/jfreema/Jareds-Cool-Mac-Utils-App">Jared's Cool Mac Utilities App</a>.</p>

<p align="center"><a href="https://github.com/jfreema/Jareds-Cool-Windows-Utils-App/releases/latest/download/JaredsWindowsUtils-Setup.exe"><img src="docs/download.svg" width="204" alt="Download Jared's Cool Windows Utilities App"></a></p>

<p align="center">Windows 10 or 11 · 64-bit · <a href="https://github.com/jfreema/Jareds-Cool-Windows-Utils-App/releases/latest/download/JaredsWindowsUtils.exe">Portable version</a> · <a href="https://github.com/jfreema/Jareds-Cool-Windows-Utils-App/releases">All versions</a></p>

<p align="center"><img src="docs/menu.png" width="403" alt="The app's menu: Open at Login, a switch for window snapping with Win+Alt+arrow keys, Thirds on External Displays, a switch for snipping the screen with Win+Alt+P, Record GIFs with Win+Alt+G, the temperatures, with the Processor at 106°F and the Motherboard at 91°F, both Normal, and About, Check for Updates and Quit buttons"></p>

<table>
  <tr>
    <td width="50%"><b><a href="#window-snapping">Window snapping</a></b><br>Win+Alt and an arrow key put the window you're in into halves, thirds, quarters or sixths.</td>
    <td width="50%"><b><a href="#screenshot-tool">Screenshot tool</a></b><br>Win+Alt+P snips part of the screen to copy, save or mark up, or copies the text in it. Win+Alt+G records it as a GIF.</td>
  </tr>
  <tr>
    <td width="50%"><b><a href="#temperature">Temperature</a></b><br>Shows how hot the processor and the motherboard are, in °F, so you can tell when your PC is running hot.</td>
    <td width="50%"><b><a href="docs/roadmap.md">Coming next</a></b><br>The Mac app's other features, one release at a time: staying awake with the lid closed is next.</td>
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

Hold the Windows key and Alt and press an arrow key to put the window you're working in into halves, thirds, quarters or sixths of its display.

**Turn it on:** it's on from the start. Its switch is **Snap Windows with Win+Alt+arrow keys**, under **Window Snapping** in the menu.

**Use it:**

- **Win+Alt+Left and Win+Alt+Right** snap the window into the column it's mostly in, then move it a column at a time, across displays.
- **Win+Alt+Up** widens it to fill the display. From a third, it goes to two thirds first.
- **Win+Alt+Down** moves it clockwise through the display's quarters, or sixths.

Each display has two columns, or three on external displays with **Thirds on External Displays** on. A laptop's own screen keeps two, since it's too narrow for thirds.

| Win+Alt+Left and Win+Alt+Right | Win+Alt+Down |
|---|---|
| <img src="docs/snapping-columns.svg" width="400" alt="A laptop's own screen in two halves, the left one lit up, next to a larger external display in three thirds, the middle one lit up"> | <img src="docs/snapping-cells.svg" width="400" alt="A laptop's own screen in four quarters, the top left one lit up, next to a larger external display in six sixths, the top right one lit up"> |

Windows' own snapping, with the Windows key and the arrow keys or by dragging a window to an edge, keeps working too. Windows 11's Win+Alt+Up and Win+Alt+Down, for the top and bottom halves, give way to the app's while snapping is on.

**Why Win+Alt:** the Mac app uses control+option, but on Windows, Ctrl+Alt types accented letters on many keyboards, and VS Code, JetBrains IDEs and Magnifier use Ctrl+Alt+arrow keys. Apps leave the Windows key alone.

---

## Screenshot tool

Press Win+Alt+P to snip part of the screen, then copy it, save it, mark it up with boxes, lines, arrows, highlights, blurs and text, or copy the text in it. Press Win+Alt+G to record part of the screen as a GIF, like scrolling through a page, to paste into a message or a document.

**Turn it on:** it's on from the start. Its switch is **Snip the Screen with Win+Alt+P**, under **Screenshots** in the menu, and **Record GIFs with Win+Alt+G** under it turns GIFs on and off.

**Use it:** press Win+Alt+P and drag across part of the screen. Press Space to pick a whole window instead and click it, or press Esc to cancel. A menu opens where you let go, with **Copy to Clipboard**, **Copy Text**, **Open in Editor** and **Save…**. Click one, or use the arrow keys and Enter. Esc, or a click anywhere else, throws the snip away.

**Copy Text** copies the words in the snip as plain text, line by line, even where you can't select them, like an error message, a paused video or a scanned PDF. Windows reads them itself, on your PC, without sending the snip anywhere. If the app says Windows can't read text in your language, add **Optical character recognition** for it: in **Settings → Time & language → Language & region**, click **…** beside your language, then **Language options**.

In the editor, pick a tool on the left and drag across the snip to draw. Hold Shift to keep lines and arrows to 45° steps, and boxes, highlights and blurs square. Shapes sit on a soft shadow, so they stand out on any snip, and **Opacity** makes them see-through, down to 10%. **Blur** blurs the area you drag across, to hide a name or a password. For text, click where it goes, type, and press Enter. It goes on a rounded label in the color, and **Text Size** sets it from 10 to 24 points. With the **Eraser**, click a shape to take it away: the one under the pointer fades first.

To change a shape, click it. The one you just drew is picked up already, so you can change it straight away. Drag it to move it, drag its dots to resize it, pick another color, opacity, thickness or text size for it, or press Delete.

| Keys | What they do |
|---|---|
| R, L, A, H, B, T | Box, line, arrow, highlight, blur, text |
| E | Eraser |
| 1 to 6 | Red, green, blue, yellow, black, white |
| W | Thin or thick lines |
| Delete | Take away the shape you clicked |
| Ctrl+Z, Ctrl+Y | Undo, redo |
| Ctrl+C | Copy it with its shapes. The editor stays open. |
| Ctrl+S | Save it with its shapes. The editor closes. |
| Ctrl+W | Close the editor. If there are shapes you haven't copied or saved, it asks first. |

**Save…** saves a PNG, starting in Pictures\Screenshots, where Windows keeps its own screenshots, then wherever you saved last. Windows' own screenshots, with Windows key+Shift+S and Print Screen, keep working too.

**Record a GIF:** press Win+Alt+G, then drag across part of the screen, or click a window to record all of it. Click off any window for the whole screen, or press Esc to cancel. A red line goes around what's recording: scroll, click and type as you like, then click **Stop** under it or press Win+Alt+G again. It stops by itself after a minute. A menu opens where the pointer is, saying how long and how big the GIF is, with **Copy to Clipboard**, to paste it into Slack, Teams, an email or a document, and **Save…**. It's copied as a file, as File Explorer copies one, so it pastes as a GIF that moves.

GIFs show the pointer, but not the red line or **Stop**. They're 15 frames a second, sized as at 100% display scale, and 1280 pixels across at most. Parts that don't move cost next to nothing, so a smaller area or a shorter recording makes a smaller file. The wait before you start scrolling is cut to a second, and any pause along the way to two, so the GIF doesn't sit still each time it plays.

---

## Temperature

Shows how hot it is inside your PC, in °F, so you can tell when it's running hot or overheating: the processor, and one more, like the motherboard.

**Use it:** click the app's icon. **Temperature**, at the bottom of the menu, has the **Processor**, then the **Motherboard** if your PC shares it, or else another sensor **Inside Your PC**. The pill beside each says how hot that is. Rest the pointer on one to see where it comes from.

The app reads them when you open the menu, then once every 2 minutes while it's open, and not at all while it's closed. Opened again within 2 minutes, the menu shows what it read the last time. Until the first reading is in, a row says **Reading…**

| Pill | Processor | What it means |
|---|---|---|
| **Cool**, blue | Below 98°F | Idle, or close to it. |
| **Normal**, green | 98°F to 154°F | Everyday use. |
| **Warm**, yellow | 155°F to 181°F | Working hard, like on a video call or in a game. Nothing to worry about. |
| **Hot**, orange | 182°F to 204°F | Near its limit. It may slow down to cool off. |
| **Overheating**, red | 205°F to 225°F | At its limit, so it slows down to cool off. Give it air, and quit what's working it hard. |
| **Critical**, red, with a warning sign | Above 225°F | Past the limit. Quit what's running and let it cool. |

A motherboard runs cooler, so its levels start lower: normal from 86°F, warm from 122°F, hot from 140°F, overheating from 158°F and critical above 176°F. When Windows is slowing your PC down to cool it off, the processor's pill says **Hot** at least, and resting the pointer on it says so.

**Can't read it:** the app shows the temperatures your PC's firmware tells Windows. A laptop's often tells it the processor's, but a desktop's often doesn't, and reading those takes a driver, which the app doesn't install.

**The graphics card and the drive are left alone for now.** Version 0.4.0 also asked the graphics card and the drive how hot they are, every 2 seconds, and a PC froze with its menu open. Until it's known which of the two did it, the app asks neither, and reads only what your PC's firmware has already told Windows. If that's ever slow to come, never comes, or your PC stops in the middle of it, the app stops reading it, and resting the pointer on the reading says so. [Troubleshooting](docs/troubleshooting.md#temperature) says how to have it read again.

<details>
<summary>How it works</summary>

The processor's temperature comes from your PC's thermal zones, the sensors its firmware tells Windows about, which Windows shares through its performance counters. A sensor named for the processor goes first, or else the hottest one that doesn't say what it's of, since on most laptops that one follows the processor. The motherboard is a sensor named for it, like the chipset's or the case's. None of it needs an administrator, and Windows already has the numbers, so the app doesn't ask the sensors themselves.

While the app is reading, a small file beside its settings, `temperature-asking.txt`, says what it's reading. If the app finds it still saying so as it starts, your PC or the app stopped in the middle of that, and the app doesn't read it again. A reading that takes a quarter of a second is done once in 10 minutes at most.

</details>

---

## Uninstall

Uninstall **Jared's Cool Windows Utilities App** in **Settings → Apps → Installed apps**. That quits the app and takes it out of Open at Login. Your settings stay in `%LocalAppData%\JaredsWindowsUtils`: delete that folder to remove them too.

For the portable version, turn off **Open at Login** if it's on, choose **Quit** in its menu, and delete JaredsWindowsUtils.exe.

Having problems? See [Troubleshooting](docs/troubleshooting.md).
