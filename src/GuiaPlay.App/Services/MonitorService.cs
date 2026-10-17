using System.Runtime.InteropServices;
using GuiaPlay.App.Models;

namespace GuiaPlay.App.Services;

internal sealed record ActiveDisplayCloneGroup(string SourceDeviceName, int PhysicalScreenCount);
internal sealed record ClonePreflightResult(bool? Supported, string Message);

internal static class MonitorService
{
    private const uint MonitorInfoPrimary = 0x00000001;
    private const uint QueryOnlyActivePaths = 0x00000002;
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;

    public static IReadOnlyList<MonitorInfo> GetActiveMonitors()
    {
        var result = new List<MonitorInfo>();
        // Identity probing uses QueryDisplayConfig (native API). In Debug a
        // controlled isolation run can omit only this native step; Win32 screen
        // enumeration remains active and no output is falsely authenticated.
        DevelopmentDiagnostics.Trace("Monitor enumeration begins. DisplayConfig=" +
            (DevelopmentDiagnostics.SkipDisplayConfig ? "isolated" : "enabled"));
        var persistentIdentities = DevelopmentDiagnostics.SkipDisplayConfig
            ? new Dictionary<string, IReadOnlyList<PersistentIdentity>>(StringComparer.OrdinalIgnoreCase)
            : GetPersistentIdentities();
        DevelopmentDiagnostics.Trace("DisplayConfig identity enumeration completed.");
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

        DevelopmentDiagnostics.Trace("Win32 monitor enumeration completed; total=" + result.Count);
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

    /// <summary>
    /// Read-only detection of physical outputs that already share one Windows
    /// desktop source. Never calls SetDisplayConfig or modifies a display.
    /// The Windows UI enumerates clone groups as one logical monitor.
    /// </summary>
    public static IReadOnlyList<ActiveDisplayCloneGroup> GetActiveCloneGroups()
    {
        if (DevelopmentDiagnostics.SkipDisplayConfig)
        {
            return [];
        }

        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var error = GetDisplayConfigBufferSizes(QueryOnlyActivePaths,
                    out var pathCount, out var modeCount);
                if (error != ErrorSuccess)
                {
                    FileLogger.Info($"GP-YT05: QueryDisplayConfig buffer error={error}");
                    return [];
                }

                var paths = new DisplayConfigPathInfo[pathCount];
                var modes = new DisplayConfigModeInfo[modeCount];
                error = QueryDisplayConfig(QueryOnlyActivePaths,
                    ref pathCount, paths, ref modeCount, modes, nint.Zero);
                if (error == ErrorInsufficientBuffer)
                {
                    continue;
                }

                if (error != ErrorSuccess)
                {
                    FileLogger.Info($"GP-YT05: QueryDisplayConfig active paths error={error}");
                    return [];
                }

                return paths.Take((int)pathCount)
                    .Where(path => path.TargetInfo.TargetAvailable && (path.Flags & 1u) != 0)
                    .GroupBy(path => (path.SourceInfo.AdapterId.LowPart,
                        path.SourceInfo.AdapterId.HighPart, path.SourceInfo.Id))
                    .Where(group => group.Select(path =>
                        (path.TargetInfo.AdapterId.LowPart,
                         path.TargetInfo.AdapterId.HighPart,
                         path.TargetInfo.Id)).Distinct().Count() > 1)
                    .Select(group =>
                    {
                        var sourceName = new DisplayConfigSourceDeviceName
                        {
                            Header = new DisplayConfigDeviceInfoHeader
                            {
                                Type = 1,
                                Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                                AdapterId = group.First().SourceInfo.AdapterId,
                                Id = group.First().SourceInfo.Id
                            }
                        };
                        var name = DisplayConfigGetDeviceInfo(ref sourceName) == ErrorSuccess
                            ? sourceName.ViewGdiDeviceName
                            : string.Empty;
                        return new ActiveDisplayCloneGroup(name,
                            group.Select(path =>
                                (path.TargetInfo.AdapterId.LowPart,
                                 path.TargetInfo.AdapterId.HighPart,
                                 path.TargetInfo.Id)).Distinct().Count());
                    })
                    .Where(group => !string.IsNullOrWhiteSpace(group.SourceDeviceName))
                    .ToArray();
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or
            EntryPointNotFoundException or InvalidOperationException)
        {
            FileLogger.Error("GP-YT05: consulta somente leitura da topologia falhou.", exception);
        }

        return [];
    }

    /// <summary>
    /// Attempts only a Windows SDC_VALIDATE of a mixed topology: operator
    /// extended, selected public displays cloned onto the first public
    /// display's source. Never applies or persists the proposed layout.
    /// Unsupported adapters, missing identity, or a race yield "unknown".
    /// </summary>
    public static ClonePreflightResult ValidateProposedPublicClone(
        IReadOnlyList<string> publicDeviceNames, string? operatorDeviceName)
    {
        if (DevelopmentDiagnostics.SkipDisplayConfig)
        {
            return new(null, "Diagnóstico nativo de monitores desativado nesta execução.");
        }

        if (publicDeviceNames.Count < 2 ||
            string.IsNullOrWhiteSpace(operatorDeviceName) ||
            publicDeviceNames.Any(name => string.Equals(name,
                operatorDeviceName, StringComparison.OrdinalIgnoreCase)) ||
            publicDeviceNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != publicDeviceNames.Count)
        {
            return new(null, "Escolha duas ou mais saídas públicas distintas e configure a tela do operador.");
        }

        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var result = GetDisplayConfigBufferSizes(QueryOnlyActivePaths,
                    out var pathCount, out var modeCount);
                if (result != ErrorSuccess)
                {
                    return new(null, $"Leitura da topologia indisponível (erro {result}).");
                }

                var paths = new DisplayConfigPathInfo[pathCount];
                var modes = new DisplayConfigModeInfo[modeCount];
                result = QueryDisplayConfig(QueryOnlyActivePaths, ref pathCount, paths,
                    ref modeCount, modes, nint.Zero);
                if (result == ErrorInsufficientBuffer)
                {
                    continue;
                }

                if (result != ErrorSuccess)
                {
                    return new(null, $"Leitura da topologia falhou (erro {result}).");
                }

                // Source names are desktop GDI identities (\\.\DISPLAYn).
                // Never assume the order of native paths matches WPF monitors.
                var indexBySource = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < pathCount; index++)
                {
                    var path = paths[index];
                    if ((path.Flags & 1u) == 0 || !path.TargetInfo.TargetAvailable)
                    {
                        continue;
                    }

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
                    if (DisplayConfigGetDeviceInfo(ref sourceName) != ErrorSuccess ||
                        string.IsNullOrWhiteSpace(sourceName.ViewGdiDeviceName))
                    {
                        continue;
                    }

                    // Existing clone groups can have multiple paths sharing
                    // one logical GDI source; those are not a safe new plan.
                    if (!indexBySource.TryAdd(sourceName.ViewGdiDeviceName, index))
                    {
                        return new(null, "Já existe uma topologia duplicada; ajuste-a no Windows.");
                    }
                }

                if (!indexBySource.ContainsKey(operatorDeviceName) ||
                    publicDeviceNames.Any(name => !indexBySource.ContainsKey(name)))
                {
                    return new(null, "Uma das telas não tem identidade de origem ativa identificável.");
                }

                var first = paths[indexBySource[publicDeviceNames[0]]];
                var adapter = first.SourceInfo.AdapterId;
                foreach (var name in publicDeviceNames.Skip(1))
                {
                    var next = paths[indexBySource[name]];
                    if (next.SourceInfo.AdapterId.LowPart != adapter.LowPart ||
                        next.SourceInfo.AdapterId.HighPart != adapter.HighPart)
                    {
                        return new(false, "As saídas públicas estão em adaptadores gráficos diferentes; " +
                            "a clonagem desta configuração não foi considerada segura.");
                    }
                }

                // Copies only: SDC_VALIDATE cannot apply any display changes.
                var candidate = paths.Take((int)pathCount).ToArray();
                var masterIndex = indexBySource[publicDeviceNames[0]];
                var source = candidate[masterIndex].SourceInfo;
                source.ModeInfoIndex = uint.MaxValue; // best-fit source mode
                candidate[masterIndex].SourceInfo = source;

                foreach (var name in publicDeviceNames.Skip(1))
                {
                    var index = indexBySource[name];
                    var clonedSource = candidate[index].SourceInfo;
                    clonedSource.AdapterId = source.AdapterId;
                    clonedSource.Id = source.Id;
                    clonedSource.ModeInfoIndex = uint.MaxValue;
                    candidate[index].SourceInfo = clonedSource;
                }

                const uint sdcValidate = 0x40;
                const uint sdcUseSuppliedDisplayConfig = 0x20;
                const uint sdcAllowChanges = 0x400;
                result = SetDisplayConfig((uint)candidate.Length, candidate,
                    modeCount, modes,
                    sdcValidate | sdcUseSuppliedDisplayConfig | sdcAllowChanges);
                return result == ErrorSuccess
                    ? new(true, "O Windows aceitou a PRÉ-VALIDAÇÃO da duplicação das telas públicas. " +
                        "Nenhuma alteração foi aplicada; a execução e a restauração ainda precisam de testes físicos.")
                    : new(false, $"O Windows recusou a topologia proposta (erro {result}). " +
                        "Tente duplicar as telas no painel de exibição do Windows.");
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or
            EntryPointNotFoundException or InvalidOperationException)
        {
            FileLogger.Error("GP-YT05: pré-validação de duplicação falhou.", exception);
        }

        return new(null, "A pré-validação não pôde ser concluída.");
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

    // SDC_VALIDATE only: never call APPLY or SAVE_TO_DATABASE in this preflight.
    [DllImport("user32.dll", EntryPoint = "SetDisplayConfig")]
    private static extern int SetDisplayConfig(
        uint numPathArrayElements,
        [In] DisplayConfigPathInfo[] pathArray,
        uint numModeInfoArrayElements,
        [In] DisplayConfigModeInfo[] modeInfoArray,
        uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}
