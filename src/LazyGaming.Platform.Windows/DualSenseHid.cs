using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LazyGaming;

// Apenas leitura com compartilhamento: não cria controle virtual, não captura o dispositivo
// em modo exclusivo e não escreve rumble, LED ou configuração no DualSense.
public sealed class DualSenseHid(FileStream stream, int reportLength) : IControllerConnection
{
    public FileStream Stream { get; } = stream;
    public int ReportLength { get; } = reportLength;
    public IControllerAdapter Adapter { get; } = new DualSenseAdapter();
    Stream IControllerConnection.Input => Stream;
    public void Dispose() => Stream.Dispose();
    public static DualSenseHid? Open()
    {
        HidD_GetHidGuid(out var guid);
        nint set = SetupDiGetClassDevs(ref guid, 0, 0, 18);
        if (set == -1) return null;
        var paths = new List<string>();
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
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref info, detail, size, out _, 0)) continue;
                    var path = Marshal.PtrToStringUni(detail + 4) ?? "";
                    if (!(path.Contains("vid_054c", StringComparison.OrdinalIgnoreCase) || path.Contains("vid&0002054c", StringComparison.OrdinalIgnoreCase))) continue;
                    if (!(path.Contains("pid_0ce6", StringComparison.OrdinalIgnoreCase) || path.Contains("pid&0ce6", StringComparison.OrdinalIgnoreCase))) continue;
                    paths.Add(path);
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        // Com USB e vínculo Bluetooth presentes, usar primeiro a interface USB
        // ativa. Nunca escrever no controle ou trocar o modo Bluetooth.
        foreach (var path in paths.OrderBy(p => p.Contains("vid&0002054c", StringComparison.OrdinalIgnoreCase) ? 1 : 0)) {
                    var handle = CreateFile(path, 0x80000000, 3, 0, 3, 0x40000000, 0);
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
                    if (length is < 64 or > 128) { handle.Dispose(); continue; }
                    return new(new FileStream(handle, FileAccess.Read, length, true), length);
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
