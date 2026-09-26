using System.Runtime.InteropServices;
using GuiaPlay.App.Models;

namespace GuiaPlay.App.Services;

internal static class MonitorService
{
    private const uint MonitorInfoPrimary = 0x00000001;
    private const uint QueryOnlyActivePaths = 0x00000002;
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;

    public static IReadOnlyList<MonitorInfo> GetActiveMonitors()
    {
        var result = new List<MonitorInfo>();
        var persistentIdentities = GetPersistentIdentities();
        EnumDisplayMonitors(nint.Zero, nint.Zero, (handle, _, _, _) =>
        {
            var native = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (!GetMonitorInfo(handle, ref native))
            {
                return true;
            }

            var dpiX = 96u;
            var dpiY = 96u;
            try
            {
                _ = GetDpiForMonitor(handle, 0, out dpiX, out dpiY);
            }
            catch (DllNotFoundException)
            {
                // Windows versions supported by the project normally provide SHCore.
            }
            catch (EntryPointNotFoundException)
            {
            }

            var identityCandidates = persistentIdentities.TryGetValue(native.DeviceName, out var identities)
                ? identities
                : [];
            var identity = identityCandidates.Count == 1 ? identityCandidates[0] : null;
            result.Add(new MonitorInfo(
                handle,
                native.DeviceName,
                identity?.DevicePath,
                identity?.FriendlyName,
                identity is not null,
                native.Monitor.Left,
                native.Monitor.Top,
                native.Monitor.Right - native.Monitor.Left,
                native.Monitor.Bottom - native.Monitor.Top,
                native.WorkArea.Left,
                native.WorkArea.Top,
                native.WorkArea.Right - native.WorkArea.Left,
                native.WorkArea.Bottom - native.WorkArea.Top,
                (native.Flags & MonitorInfoPrimary) != 0,
                dpiX,
                dpiY));
            return true;
        }, nint.Zero);

        var duplicateIdentities = result
            .Where(monitor => !string.IsNullOrWhiteSpace(monitor.PersistentId))
            .GroupBy(monitor => monitor.PersistentId!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return result
            .Select(monitor => duplicateIdentities.Contains(monitor.PersistentId ?? string.Empty)
                ? monitor with { IdentityReliable = false }
                : monitor)
            .OrderByDescending(monitor => monitor.IsPrimary)
            .ThenBy(monitor => monitor.Left)
            .ThenBy(monitor => monitor.Top)
            .Select((monitor, index) => monitor with { Number = index + 1 })
            .ToArray();
    }

    private static Dictionary<string, IReadOnlyList<PersistentIdentity>> GetPersistentIdentities()
    {
        var result = new Dictionary<string, List<PersistentIdentity>>(StringComparer.OrdinalIgnoreCase);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var status = GetDisplayConfigBufferSizes(QueryOnlyActivePaths, out var pathCount, out var modeCount);
            if (status != ErrorSuccess)
            {
                break;
            }

            var paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];
            status = QueryDisplayConfig(QueryOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, nint.Zero);
            if (status == ErrorInsufficientBuffer)
            {
                continue;
            }

            if (status != ErrorSuccess)
            {
                break;
            }

            foreach (var path in paths.Take((int)pathCount).Where(path => path.TargetInfo.TargetAvailable))
            {
                var sourceName = new DisplayConfigSourceDeviceName
                {
                    Header = new DisplayConfigDeviceInfoHeader
                    {
                        Type = 1,
                        Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                        AdapterId = path.SourceInfo.AdapterId,
                        Id = path.SourceInfo.Id
                    }
                };
                var targetName = new DisplayConfigTargetDeviceName
                {
                    Header = new DisplayConfigDeviceInfoHeader
                    {
                        Type = 2,
                        Size = (uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                        AdapterId = path.TargetInfo.AdapterId,
                        Id = path.TargetInfo.Id
                    }
                };
                if (DisplayConfigGetDeviceInfo(ref sourceName) != ErrorSuccess ||
                    DisplayConfigGetDeviceInfo(ref targetName) != ErrorSuccess ||
                    string.IsNullOrWhiteSpace(sourceName.ViewGdiDeviceName) ||
                    string.IsNullOrWhiteSpace(targetName.MonitorDevicePath))
                {
                    continue;
                }

                if (!result.TryGetValue(sourceName.ViewGdiDeviceName, out var candidates))
                {
                    candidates = [];
                    result[sourceName.ViewGdiDeviceName] = candidates;
                }

                var normalizedPath = targetName.MonitorDevicePath.Trim();
                if (!candidates.Any(candidate => string.Equals(candidate.DevicePath, normalizedPath, StringComparison.OrdinalIgnoreCase)))
                {
                    candidates.Add(new PersistentIdentity(normalizedPath, NullIfWhiteSpace(targetName.MonitorFriendlyDeviceName)));
                }
            }

            break;
        }

        return result.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<PersistentIdentity>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record PersistentIdentity(string DevicePath, string? FriendlyName);

    private delegate bool MonitorEnumProc(nint monitor, nint hdc, nint rect, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeLuid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        public NativeLuid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public NativeLuid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public int OutputTechnology;
        public int Rotation;
        public int Scaling;
        public DisplayConfigRational RefreshRate;
        public int ScanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct DisplayConfigModeInfo
    {
        private long _alignment;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public NativeLuid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigTargetDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Flags;
        public int OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string MonitorDevicePath;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint pathCount,
        [Out] DisplayConfigPathInfo[] paths,
        ref uint modeCount,
        [Out] DisplayConfigModeInfo[] modes,
        nint currentTopologyId);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName requestPacket);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName requestPacket);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}
