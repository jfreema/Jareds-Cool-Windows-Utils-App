using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
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

    /// <summary>Reads what this PC shares, twice, then checks the menu shows it, and draws every level's pill. GitHub's
    /// runners are virtual machines, with no sensors to read, so they show Can't read it.</summary>
    static async Task Temperatures(App app, string folder, Action<string, bool, string> add,
        Dictionary<string, string> facts)
    {
        using (var thermometer = new Thermometer())
        {
            var reading = await WithTimeout(thermometer.ReadAsync(), 10000);
            add("reading the temperatures works", reading != null, reading?.Sources ?? "took over 10 seconds");
            if (reading == null) return;
            facts["temperatures found"] = reading.Sources;
            facts["temperatures shown"] = reading.Report.ToString();
            var again = await WithTimeout(thermometer.ReadAsync(), 10000);
            add("and reading them again", again != null, again?.Report.ToString() ?? "took over 10 seconds");
        }

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
        add("each says where it comes from when you point at it",
            rows[0].ToolTip is string first && first == report.Processor.Explanation
            && rows[1].ToolTip is string second && second == report.Other.Explanation,
            $"\"{rows[0].ToolTip}\" and \"{rows[1].ToolTip}\"");
        await Task.Delay(300);
        Save(Render(menu, 2), Path.Combine(folder, "menu-temperatures@2x.png"));
        menu.Dismiss();
        await Task.Delay(300);

        await Picture(HeatLevels(), folder, "heat-levels");
    }

    /// <summary>A card of rows, one for each level, then two that can't be read: as the menu draws them</summary>
    static Window HeatLevels()
    {
        TemperatureRow[] samples =
        [
            new(HeatPart.Processor, 20),  // 68°F
            new(HeatPart.Processor, 41),  // 106°F
            new(HeatPart.Processor, 72),  // 162°F
            new(HeatPart.Processor, 88),  // 190°F
            new(HeatPart.Processor, 99),  // 210°F
            new(HeatPart.Processor, 110),  // 230°F
            new(HeatPart.Motherboard, null),
            new(HeatPart.Processor, null, null, HeatLevel.Hot, "Windows is slowing your PC down to cool it off."),
        ];
        var stack = new StackPanel();
        for (int index = 0; index < samples.Length; index++)
            stack.Children.Add(new TemperatureRowView("", showsDivider: index > 0) { Row = samples[index] });
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
