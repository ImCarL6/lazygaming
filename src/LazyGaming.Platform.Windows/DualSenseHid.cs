using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LazyGaming;

// Shared native input, with a bounded optional Bluetooth feature handshake.
// No virtual controller, exclusive access, rumble, LED or audio configuration.
public sealed class DualSenseHid(FileStream stream, int reportLength, IControllerAdapter? adapter = null) : IControllerConnection, IControllerFeatures
{
    public FileStream Stream { get; } = stream;
    public int ReportLength { get; } = reportLength;
    public IControllerAdapter Adapter { get; } = adapter ?? new DualSenseAdapter();
    Stream IControllerConnection.Input => Stream;
    public void Dispose() => Stream.Dispose();
    public async Task<bool> EnableFullReports(CancellationToken cancellation) {
        if(Adapter.Id!="dualsense" || !Stream.CanWrite || ReportLength!=78)return false;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(500);
        try {
            await Stream.WriteAsync(DualSenseBluetooth.FeatureRequest(),timeout.Token);
            await Stream.FlushAsync(timeout.Token);return true;
        }catch(Exception e)when(e is IOException or UnauthorizedAccessException or OperationCanceledException){return false;}
    }
    static List<string> HidPaths()
    {
        HidD_GetHidGuid(out var guid);
        nint set = SetupDiGetClassDevs(ref guid, 0, 0, 18);
        var paths = new List<string>();
        if (set == -1) return paths;
        try
        {
            for (uint i = 0; ; i++)
            {
                var info = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, 0, ref guid, i, ref info)) break;
                _ = SetupDiGetDeviceInterfaceDetail(set, ref info, 0, 0, out uint size, 0);
                nint detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref info, detail, size, out _, 0)) paths.Add(Marshal.PtrToStringUni(detail + 4) ?? "");
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return paths;
    }
    static bool HasVendor(string path, string vid) =>
        path.Contains("vid_" + vid, StringComparison.OrdinalIgnoreCase) || path.Contains("vid&0002" + vid, StringComparison.OrdinalIgnoreCase);
    /// <summary>Best-effort brand for XInput pads; names only, never changes input handling.</summary>
    public static string? XInputBrand()
    {
        try { var paths = HidPaths(); return paths.Any(p => HasVendor(p, "2dc8")) ? "8BitDo" : paths.Any(p => HasVendor(p, "045e")) ? "Xbox" : null; }
        catch (Exception e) when (e is ExternalException or OutOfMemoryException) { return null; }
    }
    public static DualSenseHid? Open(bool switchPro = false)
    {
        string vid = switchPro ? "057e" : "054c", pid = switchPro ? "2009" : "0ce6";
        var paths = HidPaths().Where(path => HasVendor(path, vid) &&
            (path.Contains("pid_" + pid, StringComparison.OrdinalIgnoreCase) || path.Contains("pid&" + pid, StringComparison.OrdinalIgnoreCase))).ToList();
        // Com USB e vínculo Bluetooth presentes, usar primeiro a interface USB
        // ativa. Read-only fallback preserves input if feature access is unavailable.
        foreach (var path in paths.OrderBy(p => p.Contains("vid&0002054c", StringComparison.OrdinalIgnoreCase) ? 1 : 0)) {
                    bool writable=!switchPro;
                    var handle = CreateFile(path, writable ? 0xc0000000u : 0x80000000u, 3, 0, 3, 0x40000000, 0);
                    if(handle.IsInvalid && writable) { handle.Dispose();writable=false;handle=CreateFile(path,0x80000000,3,0,3,0x40000000,0); }
                    if (handle.IsInvalid) { handle.Dispose(); continue; }
                    ushort length = 0;
                    if (HidD_GetPreparsedData(handle, out var preparsed))
                    {
                        try
                        {
                            if (HidP_GetCaps(preparsed, out var caps) == 0x00110000 && caps.UsagePage == 1 && caps.Usage == 5) length = caps.InputLength;
                        }
                        finally { HidD_FreePreparsedData(preparsed); }
                    }
                    if (length < (switchPro ? 49 : 64) || length > (switchPro ? 512 : 128)) { handle.Dispose(); continue; }
                    return new(new FileStream(handle, writable ? FileAccess.ReadWrite : FileAccess.Read, length, true), length, switchPro ? new SwitchProAdapter() : new DualSenseAdapter());
        }
        return null;
    }
    [StructLayout(LayoutKind.Sequential)] struct InterfaceData { public int Size; public Guid Guid; public uint Flags; public nuint Reserved; }
    [StructLayout(LayoutKind.Explicit, Size = 64)] struct Caps { [FieldOffset(0)] public ushort Usage; [FieldOffset(2)] public ushort UsagePage; [FieldOffset(4)] public ushort InputLength; }
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out nint data);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] static extern bool HidD_FreePreparsedData(nint data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(nint data, out Caps caps);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern nint SetupDiGetClassDevs(ref Guid guid, nint enumerator, nint parent, uint flags);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(nint set, nint device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(nint set, ref InterfaceData data, nint detail, uint size, out uint required, nint device);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(nint set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
}
