using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using JaredsWindowsUtils.Core;
using JaredsWindowsUtils.Core.Temperature;
using JaredsWindowsUtils.Temperature;
using JaredsWindowsUtils.Ui;

namespace JaredsWindowsUtils;

static partial class SelfTest
{
    /// <summary>What the menu's pictures show, as the Mac's README picture does: a processor at 41°C, and a
    /// motherboard at 33°C</summary>
    internal static readonly TemperatureReport PictureTemperatures = new(
        new TemperatureRow(HeatPart.Processor, 41, "The processor, from the PC's sensor CPUZ"),
        new TemperatureRow(HeatPart.Motherboard, 33, "The motherboard, from the PC's sensor PCHZ"));

    /// <summary>Reads what this PC shares, twice, then checks the menu shows it, when it's asked for and what happens
    /// when a source makes trouble, and draws every level's pill. GitHub's runners are virtual machines, with no
    /// sensors to read, so they show Can't read it.</summary>
    static async Task Temperatures(App app, string folder, Action<string, bool, string> add,
        Dictionary<string, string> facts)
    {
        using (var sensors = new WindowsSensors())
        {
            var guard = new SourceGuard(new Settings(), null, WindowsSensors.Clock);
            // Every source, though the app itself asks only the thermal zones for now (SafeToAsk.Sources), so that
            // the rest stays tried here, on a machine of GitHub's
            var thermometer = new Thermometer(guard, WindowsSensors.Clock, sensors, every: TimeSpan.Zero,
                asks: SafeToAsk.EverySource);
            var reading = await WithTimeout(thermometer.ReadAsync(), 20000);
            add("reading the temperatures works", reading != null, reading?.Sources ?? "took over 20 seconds");
            if (reading == null) return;
            facts["temperatures found"] = reading.Sources;
            facts["temperatures shown"] = reading.Report.ToString();
            var again = await WithTimeout(thermometer.ReadAsync(), 20000);
            add("and reading them again", again != null && !ReferenceEquals(again, reading),
                again?.Sources ?? "took over 20 seconds");
            add("each source answered fast enough to be asked again", guard.LeftOut.Count == 0,
                guard.LeftOut.Count == 0 ? "none left out" : string.Join(", ", guard.LeftOut));
            string cards = sensors.Note(TemperatureSource.Graphics) ?? "every card was asked and answered";
            facts["graphics cards"] = cards;
            add("each graphics card is asked, or left alone for a reason",
                !cards.Contains("didn't list") && !cards.Contains("listed no") && !cards.Contains("what kind it is"),
                cards);
            string drive = sensors.Note(TemperatureSource.Drive)
                ?? (again?.Drive is { } answered ? $"{answered.Name} answered" : "not needed");
            facts["drive"] = drive;
            add("the drive says what it is before it's asked how hot",
                again?.Drive != null || drive.Contains("bus type") || drive == "not needed", drive);
        }

        Marker(add);

        // The menu, with what the app read
        app.PictureTemperatures = null;
        app.ShowMenu();
        var menu = await Until(() => app.Menu);
        var shown = menu == null ? null : await Until(() => menu.TemperatureRows[1].Row != null ? menu : null);
        if (menu == null || shown == null || app.Temperatures is not { } read)
        {
            add("the menu shows the temperatures", false, menu == null ? "no menu" : "nothing read");
            menu?.Dismiss();
            return;
        }
        var rows = menu.TemperatureRows;
        var report = read.Report;
        add("the menu shows the processor's temperature",
            rows[0].Title == "Processor" && rows[0].Row == report.Processor
            && AutomationProperties.GetItemStatus(rows[0]) == report.Processor.Spoken,
            $"{rows[0].Title}: {AutomationProperties.GetItemStatus(rows[0])}");
        add("and one more inside the PC",
            rows[1].Title is "Motherboard" or "Graphics Card" or "Inside Your PC" or "Drive"
            && rows[1].Row == report.Other && AutomationProperties.GetItemStatus(rows[1]) == report.Other.Spoken,
            $"{rows[1].Title}: {AutomationProperties.GetItemStatus(rows[1])}");
        add("the app itself asks only the thermal zones for now",
            read.Sources.Contains("graphics: not asked in this version")
            && (read.Sources.Contains("drive: not asked in this version") || read.Sources.Contains("drive: not needed")),
            read.Sources);
        add("each says where it comes from when you point at it",
            rows[0].ToolTip is string first && first == report.Processor.Explanation
            && rows[1].ToolTip is string second && second == report.Other.Explanation,
            $"\"{rows[0].ToolTip}\" and \"{rows[1].ToolTip}\"");
        await Task.Delay(300);
        Save(Render(menu, 2), Path.Combine(folder, "menu-temperatures@2x.png"));
        menu.Dismiss();
        await Task.Delay(300);

        await Cadence(app, add);
        await NoAnswer(app, folder, add);
        await Picture(HeatLevels(), folder, "heat-levels");
    }

    /// <summary>The file that names a source while it's being asked, which is how the app knows, the next time it
    /// starts, what it was asking when the PC stopped</summary>
    static void Marker(Action<string, bool, string> add)
    {
        string trial = Path.Combine(Path.GetTempPath(), $"jaredswindowsutils-guard-{Environment.ProcessId}");
        try
        {
            Directory.CreateDirectory(trial);
            string settings = Path.Combine(trial, "settings.json");
            string marker = Path.Combine(trial, "temperature-asking.txt");
            var first = new SourceGuard(new Settings(settings), marker, WindowsSensors.Clock);
            bool asked = first.Begin(TemperatureSource.Graphics);
            string during = Said(marker);
            first.End(TemperatureSource.Graphics, TimeSpan.FromMilliseconds(5));
            string after = Said(marker);
            // Not only the first time it's asked
            bool askedAgain = first.Begin(TemperatureSource.Graphics);
            string again = Said(marker);
            byte[] stopped = File.Exists(marker) ? Bytes(marker) : [];
            first.Close();
            bool gone = !File.Exists(marker);
            add("a file says which source is being asked, each time, until it answers or the app quits",
                asked && during == "Graphics" && after == "nothing" && askedAgain && again == "Graphics" && gone,
                $"while asking: {during}; after the answer: {after}; asking again: {again}; "
                + $"after quitting: {(gone ? "no file" : Said(marker))}");

            // As if the PC had stopped mid-question instead, and the app starts again. In a folder of its own, so
            // this doesn't wait for Windows to finish taking the first file away.
            string folder = Path.Combine(trial, "stopped");
            Directory.CreateDirectory(folder);
            settings = Path.Combine(folder, "settings.json");
            marker = Path.Combine(folder, "temperature-asking.txt");
            File.WriteAllBytes(marker, stopped);
            var second = new SourceGuard(new Settings(settings), marker, WindowsSensors.Clock);
            string kept = File.Exists(settings) ? File.ReadAllText(settings).ReplaceLineEndings(" ") : "no settings";
            string then = Said(marker);
            bool others = second.Begin(TemperatureSource.Zones);
            second.End(TemperatureSource.Zones, TimeSpan.FromMilliseconds(5));
            add("a source that was being asked when the PC stopped isn't asked again",
                stopped.Length > 0 && second.IsLeftOut(TemperatureSource.Graphics)
                && !second.Begin(TemperatureSource.Graphics) && kept.Contains("Graphics:Stopped") && then == "nothing"
                && others && !second.IsLeftOut(TemperatureSource.Zones),
                $"the settings say {kept}; the file says {then}; the thermal zones are {(others ? "" : "not ")}asked");
            second.Close();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            add("a source that was being asked when the PC stopped isn't asked again", false, exception.Message);
        }
        finally
        {
            try
            {
                Directory.Delete(trial, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Windows' own clean-up takes it
            }
        }
    }

    /// <summary>The file as it is, though the guard has it open</summary>
    static byte[] Bytes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    static string Said(string path) => !File.Exists(path)
        ? "no file"
        : System.Text.Encoding.ASCII.GetString(Bytes(path)).Trim() is { Length: > 0 } text ? text : "nothing";

    /// <summary>Stand-ins for what's asked, which count how often, and a clock the test moves</summary>
    sealed class StandIn : ISensors
    {
        long now = TimeSpan.FromHours(1).Ticks;
        int zonesAsked;
        int graphicsAsked;

        /// <summary>Holds the thermal zones' answer back until it's set</summary>
        public ManualResetEventSlim? ZonesWait { get; init; }

        public int ZonesAsked => Volatile.Read(ref zonesAsked);
        public int GraphicsAsked => Volatile.Read(ref graphicsAsked);

        public TimeSpan Clock() => TimeSpan.FromTicks(Interlocked.Read(ref now));

        public void Pass(TimeSpan time) => Interlocked.Add(ref now, time.Ticks);

        public void Prepare(TemperatureSource source)
        {
        }

        public IReadOnlyList<ThermalZone> Zones()
        {
            Interlocked.Increment(ref zonesAsked);
            ZonesWait?.Wait();
            return [new ThermalZone(@"\_TZ.CPUZ", 45)];
        }

        public IReadOnlyList<GraphicsTemperature> Graphics()
        {
            Interlocked.Increment(ref graphicsAsked);
            return [new GraphicsTemperature("Stand-in card", 50)];
        }

        public DriveTemperature? Drive() => null;

        public string? Note(TemperatureSource source) => "a stand-in";
    }

    /// <summary>Once when the menu opens, then every 2 minutes while it's open, as Jared asked, and never while it's
    /// closed</summary>
    static async Task Cadence(App app, Action<string, bool, string> add)
    {
        var real = app.TemperatureSources;
        var standIn = new StandIn();
        var guard = new SourceGuard(new Settings(), null, standIn.Clock);
        app.UseTemperatureSources(guard,
            () => new Thermometer(guard, standIn.Clock, standIn, asks: SafeToAsk.EverySource));
        try
        {
            await Task.Delay(300);
            int closed = standIn.GraphicsAsked;
            app.ShowMenu();
            var menu = await Until(() => app.Menu);
            await Until(() => app.Temperatures);
            int opened = standIn.GraphicsAsked;
            // The menu's own timer looks every 2 seconds, and here it's asked 3 times more
            for (int i = 0; i < 3; i++) app.ReadTemperatures();
            await Task.Delay(2500);
            standIn.Pass(Thermometer.Every - TimeSpan.FromSeconds(1));
            app.ReadTemperatures();
            await Task.Delay(300);
            int before = standIn.GraphicsAsked;
            standIn.Pass(TimeSpan.FromSeconds(2));
            app.ReadTemperatures();
            await Until(() => standIn.GraphicsAsked > before ? app : null);
            await Task.Delay(300);
            int after = standIn.GraphicsAsked;
            bool stayed = app.Menu != null;
            add("temperatures are read once when the menu opens, then every 2 minutes",
                menu != null && stayed && closed == 0 && opened == 1 && before == 1 && after == 2
                && standIn.ZonesAsked == after,
                $"asked {closed} times before the menu opened, {opened} as it did, {before} after 1 minute 59 seconds, "
                + $"{after} after 2 minutes 1 second{(stayed ? "" : "; the menu closed by itself")}");
            menu?.Dismiss();
            await Until(() => app.Menu == null ? app : null);
            standIn.Pass(TimeSpan.FromMinutes(10));
            app.ReadTemperatures();
            await Task.Delay(2500);
            add("and never while the menu is closed", app.Menu == null && standIn.GraphicsAsked == after,
                $"asked {standIn.GraphicsAsked - after} times in 10 minutes");
        }
        finally
        {
            app.Menu?.Dismiss();
            app.UseTemperatureSources(real.Guard, real.Make);
        }
    }

    /// <summary>A source that never answers: the menu says Reading… until the app gives up on it, then shows the rest
    /// and says why that one's missing</summary>
    static async Task NoAnswer(App app, string folder, Action<string, bool, string> add)
    {
        var real = app.TemperatureSources;
        using var answer = new ManualResetEventSlim(false);
        var standIn = new StandIn { ZonesWait = answer };
        var guard = new SourceGuard(new Settings(), null, standIn.Clock,
            message => Log.Info("temp", $"Stand-in: {message}"));
        app.UseTemperatureSources(guard,
            () => new Thermometer(guard, standIn.Clock, standIn, asks: SafeToAsk.EverySource));
        try
        {
            await Task.Delay(300);
            app.ShowMenu();
            var menu = await Until(() => app.Menu);
            await Until(() => standIn.ZonesAsked > 0 ? app : null);
            await Task.Delay(300);
            add("the menu says Reading… while a source hasn't answered",
                menu != null && app.Temperatures == null && menu.TemperatureRows[0].Row == null
                && AutomationProperties.GetItemStatus(menu.TemperatureRows[0]) == "reading",
                menu == null ? "no menu" : AutomationProperties.GetItemStatus(menu.TemperatureRows[0]));
            if (menu != null) Save(Render(menu, 2), Path.Combine(folder, "menu-reading@2x.png"));

            // Half a minute with no answer, and the menu's timer comes round
            standIn.Pass(SourceGuard.StuckAfter + TimeSpan.FromSeconds(1));
            app.ReadTemperatures();
            var given = await Until(() => app.Temperatures);
            await Task.Delay(300);
            var rows = menu?.TemperatureRows;
            string tip = rows?[0].ToolTip as string ?? "no tooltip";
            add("a source that never answers is given up on, and the menu shows the rest",
                given is { Report.Processor.Celsius: null, Report.Other: { Part: HeatPart.Graphics, Celsius: 50 } }
                && guard.IsLeftOut(TemperatureSource.Zones) && rows != null && rows[1].Title == "Graphics Card"
                && tip == "The app no longer asks your PC's sensors for their temperatures, since it got no answer.",
                $"{given?.Report.ToString() ?? "nothing read"}; the processor's tooltip: \"{tip}\"");
            if (menu != null) Save(Render(menu, 2), Path.Combine(folder, "menu-given-up@2x.png"));

            // It answers after all, too late to count
            answer.Set();
            await Task.Delay(500);
            add("and its late answer changes nothing",
                given != null && ReferenceEquals(app.Temperatures, given) && standIn.ZonesAsked == 1
                && standIn.GraphicsAsked == 1,
                $"{app.Temperatures?.Report.ToString() ?? "nothing"}; asked the thermal zones {standIn.ZonesAsked} "
                + $"time(s) and the graphics cards {standIn.GraphicsAsked}");
        }
        finally
        {
            answer.Set();
            app.Menu?.Dismiss();
            await Task.Delay(300);
            app.UseTemperatureSources(real.Guard, real.Make);
        }
    }

    /// <summary>A card of rows, one for each level, then two that can't be read and one not read yet: as the menu
    /// draws them</summary>
    static Window HeatLevels()
    {
        TemperatureRow?[] samples =
        [
            new(HeatPart.Processor, 20),  // 68°F
            new(HeatPart.Processor, 41),  // 106°F
            new(HeatPart.Processor, 72),  // 162°F
            new(HeatPart.Processor, 88),  // 190°F
            new(HeatPart.Processor, 99),  // 210°F
            new(HeatPart.Processor, 110),  // 230°F
            new(HeatPart.Motherboard, null),
            new(HeatPart.Processor, null, null, HeatLevel.Hot, "Windows is slowing your PC down to cool it off."),
            null,
        ];
        var stack = new StackPanel();
        for (int index = 0; index < samples.Length; index++)
            stack.Children.Add(new TemperatureRowView("Motherboard", showsDivider: index > 0) { Row = samples[index] });
        var content = new StackPanel { Margin = new Thickness(12) };
        content.Children.Add(new BubbleCard { Child = stack, Margin = new Thickness(0, 0, 2, 3) });
        return new Window
        {
            Title = "Heat levels",
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            UseLayoutRounding = true,
            Left = 40,
            Top = 40,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = new MenuSurface { Child = content },
        };
    }

    static async Task<T?> WithTimeout<T>(Task<T> task, int milliseconds) where T : class =>
        await Task.WhenAny(task, Task.Delay(milliseconds)) == task ? await task : null;
}
