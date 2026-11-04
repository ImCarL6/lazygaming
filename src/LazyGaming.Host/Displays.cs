using System.Runtime.InteropServices;
using System.Text.Json;

namespace LazyGaming;

public static partial class Displays
{
    const uint Active = 2, All = 1, Supplied = 0x20, Apply = 0x80, Persist = 0x200, AllowChanges = 0x400;
    public sealed record Snapshot(PathInfo[] Paths, ModeInfo[] Modes);
    public sealed record Screen(string Name, string DevicePath, uint Technology, bool Available, bool Active, uint Width, uint Height, double Hz);
    static void Check(int error) { if (error != 0) throw new InvalidOperationException($"O Windows não conseguiu preparar a tela (código {error})."); }
    public static Snapshot Capture(bool all = false)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            uint flags = all ? All : Active;
            Check(GetDisplayConfigBufferSizes(flags, out uint paths, out uint modes));
            var p = new PathInfo[paths]; var m = new ModeInfo[modes];
            int error = QueryDisplayConfig(flags, ref paths, p, ref modes, m, 0);
            if (error == 122) continue;
            Check(error); return new(p[..(int)paths], m[..(int)modes]);
        }
        throw new InvalidOperationException("As telas estão mudando. Tente novamente após o HDMI estabilizar.");
    }
    static TargetName Name(PathInfo path)
    {
        var name = new TargetName { Header = new Header { Type = 2, Size = (uint)Marshal.SizeOf<TargetName>(), Adapter = path.Target.Adapter, Id = path.Target.Id } };
        Check(DisplayConfigGetDeviceInfo(ref name)); return name;
    }
    static PreferredMode Preferred(PathInfo path)
    {
        var mode = new PreferredMode { Header = new Header { Type = 3, Size = (uint)Marshal.SizeOf<PreferredMode>(), Adapter = path.Target.Adapter, Id = path.Target.Id } };
        Check(DisplayConfigGetDeviceInfo(ref mode)); return mode;
    }
    public static List<Screen> List()
    {
        var snapshot = Capture(true);
        return snapshot.Paths.GroupBy(p => (p.Target.Adapter.Low, p.Target.Adapter.High, p.Target.Id)).Select(group =>
        {
            var path = group.OrderByDescending(p => (p.Flags & 1) != 0).First(); var name = Name(path);
            PreferredMode preferred = default;
            if (path.Target.Available) preferred = Preferred(path);
            return new Screen(name.FriendlyName, name.DevicePath, path.Target.Technology, path.Target.Available,
                group.Any(p => (p.Flags & 1) != 0), preferred.Width, preferred.Height,
                preferred.Mode.Signal.VSync.Denominator == 0 ? 0 : (double)preferred.Mode.Signal.VSync.Numerator / preferred.Mode.Signal.VSync.Denominator);
        }).ToList();
    }
    public static Func<PcPreferences> Preferences { get; set; } = () => new();
    public static Screen? SelectTarget(IEnumerable<Screen> screens, string? selected = null) {
        var available = screens.Where(s => s.Available).ToArray();
        if (string.IsNullOrEmpty(selected) && available.Length == 1) return available[0];
        var matches = available.Where(s => string.IsNullOrEmpty(selected)
            ? s.Technology == 5 : string.Equals(s.DevicePath, selected, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    public static Screen Tv() => SelectTarget(List(), Preferences().DisplayPath)
        ?? throw new InvalidOperationException("Escolha a tela de jogo na Central do PC e confira o cabo HDMI.");
    public static void PrepareTv()
    {
        var tv = Tv();
        var current = Capture();
        if (current.Paths.Length == 1 && Name(current.Paths[0]).DevicePath == tv.DevicePath)
        {
            var currentSource = current.Modes[current.Paths[0].Source.ModeIndex].Source;
            if (currentSource.Width == tv.Width && currentSource.Height == tv.Height && Math.Abs(Rate(current.Paths[0].Target.Refresh) - tv.Hz) < 0.2) return;
        }
        var all = Capture(true);
        var candidates = all.Paths.Where(p => p.Target.Available && Name(p).DevicePath == tv.DevicePath).ToArray();
        var path = candidates.OrderByDescending(p => (p.Flags & 1) != 0).First();
        var preferred = Preferred(path);
        path.Flags = 1; path.Source.ModeIndex = 0; path.Target.ModeIndex = 1;
        path.Target.Refresh = preferred.Mode.Signal.VSync;
        var modes = new[] {
            new ModeInfo { Type = 1, Id = path.Source.Id, Adapter = path.Source.Adapter,
                Source = new SourceMode { Width = preferred.Width, Height = preferred.Height, PixelFormat = 4 } },
            new ModeInfo { Type = 2, Id = path.Target.Id, Adapter = path.Target.Adapter, Target = preferred.Mode }
        };
        Check(SetDisplayConfig(1, [path], 2, modes, Supplied | Apply | Persist | AllowChanges));
        var actual = Capture();
        if (actual.Paths.Length != 1 || Name(actual.Paths[0]).DevicePath != tv.DevicePath)
            throw new InvalidOperationException("A TV não ficou como única tela de jogo. A abertura foi interrompida.");
        var source = actual.Modes[actual.Paths[0].Source.ModeIndex].Source;
        if (source.Width != preferred.Width || source.Height != preferred.Height)
            throw new InvalidOperationException("A resolução nativa da TV não foi confirmada. A abertura foi interrompida.");
        if (Math.Abs(Rate(actual.Paths[0].Target.Refresh) - Rate(preferred.Mode.Signal.VSync)) >= 0.2)
            throw new InvalidOperationException("A frequência nativa da TV não foi confirmada. A abertura foi interrompida.");
    }
    static double Rate(Rational value) => value.Denominator == 0 ? 0 : (double)value.Numerator / value.Denominator;
    public static string GameDisplay()
    {
        var tv = Tv();
        var path = Capture().Paths.Single(p => Name(p).DevicePath == tv.DevicePath);
        var name = new SourceName { Header = new Header { Type = 1, Size = (uint)Marshal.SizeOf<SourceName>(), Adapter = path.Source.Adapter, Id = path.Source.Id } };
        Check(DisplayConfigGetDeviceInfo(ref name)); return name.GdiName;
    }
    public sealed record DesktopScreen(string DevicePath, string GdiName, System.Drawing.Rectangle Bounds);
    public static DesktopScreen[] DesktopLayout() {
        var snapshot = Capture();
        return snapshot.Paths.Select(path => {
            var name = new SourceName { Header = new Header { Type = 1, Size = (uint)Marshal.SizeOf<SourceName>(), Adapter = path.Source.Adapter, Id = path.Source.Id } };
            Check(DisplayConfigGetDeviceInfo(ref name));
            var source = SourceOf(snapshot, path);
            return new DesktopScreen(Name(path).DevicePath, name.GdiName, new(source.Position.X, source.Position.Y, (int)source.Width, (int)source.Height));
        }).ToArray();
    }
    public static SourceMode StreamSource(bool tv)
    {
        var capture = Capture();
        if (tv) {
            var device = Tv();
            var paths = capture.Paths.Where(p => Name(p).DevicePath == device.DevicePath).ToArray();
            if (paths.Length != 1) throw new InvalidOperationException("A tela de jogo está inativa ou ambígua.");
            return capture.Modes[paths[0].Source.ModeIndex].Source;
        }
        var sources = capture.Modes.Where(m => m.Type == 1 && m.Source.Position.X == 0 && m.Source.Position.Y == 0).ToArray();
        if (sources.Length != 1) throw new InvalidOperationException("Tela principal de captura ambígua.");
        return sources[0].Source;
    }
    public static void Restore(Snapshot snapshot) {
        if (SameLayout(Capture(), snapshot)) return;
        Check(SetDisplayConfig((uint)snapshot.Paths.Length, snapshot.Paths, (uint)snapshot.Modes.Length, snapshot.Modes, Supplied | Apply | Persist | AllowChanges));
    }
    public static void Save(Snapshot snapshot, string path) => File.WriteAllText(path, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { IncludeFields = true }));
    public static Snapshot Load(string path) => JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path), new JsonSerializerOptions { IncludeFields = true })!;

    [StructLayout(LayoutKind.Sequential)] public struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] public struct Rational { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Size { public uint Width, Height; }
    [StructLayout(LayoutKind.Sequential)] public struct SourceInfo { public Luid Adapter; public uint Id, ModeIndex, Status; }
    [StructLayout(LayoutKind.Sequential)] public struct TargetInfo
    { public Luid Adapter; public uint Id, ModeIndex, Technology, Rotation, Scaling; public Rational Refresh; public uint Scanline; [MarshalAs(UnmanagedType.Bool)] public bool Available; public uint Status; }
    [StructLayout(LayoutKind.Sequential)] public struct PathInfo { public SourceInfo Source; public TargetInfo Target; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] public struct SourceMode { public uint Width, Height, PixelFormat; public Point Position; }
    [StructLayout(LayoutKind.Sequential)] public struct SignalInfo { public ulong PixelRate; public Rational HSync, VSync; public Size ActiveSize, TotalSize; public uint VideoStandard, Scanline; }
    [StructLayout(LayoutKind.Sequential)] public struct TargetMode { public SignalInfo Signal; }
    [StructLayout(LayoutKind.Explicit, Size = 64)] public struct ModeInfo
    { [FieldOffset(0)] public uint Type; [FieldOffset(4)] public uint Id; [FieldOffset(8)] public Luid Adapter; [FieldOffset(16)] public SourceMode Source; [FieldOffset(16)] public TargetMode Target; }
    [StructLayout(LayoutKind.Sequential)] struct Header { public uint Type, Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct TargetName
    { public Header Header; public uint Flags, Technology; public ushort Manufacturer, Product; public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath; }
    [StructLayout(LayoutKind.Sequential)] struct PreferredMode { public Header Header; public uint Width, Height; public TargetMode Mode; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct SourceName
    { public Header Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string GdiName; }
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths, ref uint modeCount, [Out] ModeInfo[] modes, nint topology);
    static int SetDisplayConfig(uint pathCount, PathInfo[] paths, uint modeCount, ModeInfo[] modes, uint flags) {
        int result = NativeSetDisplayConfig(pathCount, paths, modeCount, modes, flags);
        try {
            var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LazyGaming", "display-operations.jsonl");
            if (File.Exists(file) && new FileInfo(file).Length > 131072) File.Move(file, file + ".previous", true);
            File.AppendAllText(file, JsonSerializer.Serialize(new { time = DateTimeOffset.UtcNow, pid = Environment.ProcessId, flags, result,
                paths = paths.Select(p => new { target = p.Target.Id, source = p.Source.Id, refresh = p.Target.Refresh.Numerator, denominator = p.Target.Refresh.Denominator }) }) + Environment.NewLine);
        } catch { }
        return result;
    }
    [DllImport("user32.dll", EntryPoint="SetDisplayConfig")] static extern int NativeSetDisplayConfig(uint pathCount, [In] PathInfo[] paths, uint modeCount, [In] ModeInfo[] modes, uint flags);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref TargetName name);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref PreferredMode mode);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref SourceName name);
}
