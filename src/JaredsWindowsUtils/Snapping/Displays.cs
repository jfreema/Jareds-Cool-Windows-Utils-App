using System.Runtime.InteropServices;
using JaredsWindowsUtils.Core;
using static JaredsWindowsUtils.Interop.Native;

namespace JaredsWindowsUtils.Snapping;

/// <summary>A monitor, in pixels</summary>
/// <param name="Frame">All of it</param>
/// <param name="Visible">Without the taskbar</param>
/// <param name="IsInternal">A laptop's own screen</param>
readonly record struct Monitor(IntPtr Handle, RectD Frame, RectD Visible, string Device, bool IsPrimary,
    bool IsInternal);

static class Displays
{
    const uint MONITORINFOF_PRIMARY = 1;

    // DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY values for a screen built into the computer
    const uint OutputLvds = 6;
    const uint OutputDisplayPortEmbedded = 11;
    const uint OutputUdiEmbedded = 13;
    const uint OutputInternal = 0x80000000;

    public static List<Monitor> All()
    {
        var internalDevices = InternalDevices();
        var monitors = new List<Monitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = MonitorInfo(monitor);
            monitors.Add(new Monitor(monitor, info.rcMonitor.ToRect(), info.rcWork.ToRect(), info.szDevice,
                (info.dwFlags & MONITORINFOF_PRIMARY) != 0, internalDevices.Contains(info.szDevice)));
            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    /// <summary>The device names (\\.\DISPLAY1) of the screens built into the computer, which Windows tells apart
    /// by how they're connected</summary>
    static HashSet<string> InternalDevices()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0)
                return names;
            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
                return names;
            for (int index = 0; index < pathCount; index++)
            {
                var path = paths[index];
                if (path.targetInfo.outputTechnology is not (OutputInternal or OutputLvds or OutputDisplayPortEmbedded
                    or OutputUdiEmbedded))
                    continue;
                var request = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = path.sourceInfo.adapterId,
                        id = path.sourceInfo.id,
                    },
                };
                if (DisplayConfigGetDeviceInfo(ref request) == 0) names.Add(request.viewGdiDeviceName);
            }
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException or DllNotFoundException)
        {
            // Treated as external
        }
        return names;
    }
}
