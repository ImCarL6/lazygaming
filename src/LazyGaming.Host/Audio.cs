using System.Runtime.InteropServices;

namespace LazyGaming;

public record AudioDevice(string Id, string Name, bool Current);
public record VolumeState(int Level, bool Muted, string Device);

public static class Audio
{
    public static string[] CaptureDefaults()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            var ids = new string[3];
            for (int role = 0; role < 3; role++)
            {
                Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, role, out var device));
                try { Marshal.ThrowExceptionForHR(device.GetId(out ids[role])); }
                finally { Marshal.ReleaseComObject(device); }
            }
            return ids;
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }
    public static void RestoreDefaults(string[] ids)
    {
        var policy = (IPolicyConfig)new PolicyConfig();
        try
        {
            int error = 0;
            for (int role = 0; role < 3; role++) { var result = policy.SetDefaultEndpoint(ids[role], role); if (result < 0) error = result; }
            Marshal.ThrowExceptionForHR(error);
        }
        finally { Marshal.ReleaseComObject(policy); }
    }
    public static string VerifyRoundTrip()
    {
        var tv = List().SingleOrDefault(d => d.Name.StartsWith("SAMSUNG", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Saída Samsung indisponível.");
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        var policy = (IPolicyConfig)new PolicyConfig();
        var previous = new string[3];
        string ReadDefault(int role)
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, role, out var device));
            try { Marshal.ThrowExceptionForHR(device.GetId(out var id)); return id; }
            finally { Marshal.ReleaseComObject(device); }
        }
        try
        {
            for (int role = 0; role < 3; role++) previous[role] = ReadDefault(role);
            try
            {
                Select(tv.Id);
                for (int role = 0; role < 3; role++)
                    if (ReadDefault(role) != tv.Id) throw new InvalidOperationException("A troca de áudio não foi confirmada.");
            }
            finally
            {
                for (int role = 0; role < 3; role++) Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(previous[role], role));
            }
            for (int role = 0; role < 3; role++)
                if (ReadDefault(role) != previous[role]) throw new InvalidOperationException("A restauração de áudio não foi confirmada.");
            return "PASS: saída Samsung selecionada e confirmada nos três papéis de áudio; todas as saídas anteriores restauradas e conferidas. Reprodução audível ainda depende do teste na TV.";
        }
        finally { Marshal.ReleaseComObject(policy); Marshal.ReleaseComObject(enumerator); }
    }
    public static List<AudioDevice> List()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            string? current = null;
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out var def) == 0)
            { try { Marshal.ThrowExceptionForHR(def.GetId(out current)); } finally { Marshal.ReleaseComObject(def); } }
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(0, 1, out var devices));
            try
            {
                Marshal.ThrowExceptionForHR(devices.GetCount(out var count));
                var result = new List<AudioDevice>();
                for (uint i = 0; i < count; i++)
                {
                    Marshal.ThrowExceptionForHR(devices.Item(i, out var device));
                    try
                    {
                        Marshal.ThrowExceptionForHR(device.GetId(out var id));
                        Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out var store));
                        try
                        {
                            var key = new PropertyKey { Format = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 14 };
                            Marshal.ThrowExceptionForHR(store.GetValue(ref key, out var value));
                            try { result.Add(new(id, Marshal.PtrToStringUni(value.Pointer) ?? "Saída de áudio", id == current)); }
                            finally { PropVariantClear(ref value); }
                        }
                        finally { Marshal.ReleaseComObject(store); }
                    }
                    finally { Marshal.ReleaseComObject(device); }
                }
                return result;
            }
            finally { Marshal.ReleaseComObject(devices); }
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }
    public static void Select(string id)
    {
        if (!List().Any(d => d.Id == id)) throw new InvalidOperationException("Saída de áudio desconectada ou indisponível.");
        // PolicyConfig é uma interface Windows não documentada: falha explícita em versões incompatíveis.
        var policy = (IPolicyConfig)new PolicyConfig();
        try { for (var role = 0; role < 3; role++) Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, role)); }
        finally { Marshal.ReleaseComObject(policy); }
    }
    // Volume mestre da saída padrão atual (onde o jogo toca), em passos de 5.
    public static VolumeState Volume() => WithEndpointVolume((volume, name) => {
        Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out var level));
        Marshal.ThrowExceptionForHR(volume.GetMute(out var muted));
        return new VolumeState((int)Math.Round(level * 100), muted, name);
    });
    public static VolumeState SetVolume(int level)
    {
        level = Math.Clamp(level, 0, 100);
        return WithEndpointVolume((volume, name) => {
            var context = Guid.Empty;
            Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar(level / 100f, ref context));
            if (level > 0) Marshal.ThrowExceptionForHR(volume.SetMute(false, ref context));
            Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out var applied));
            Marshal.ThrowExceptionForHR(volume.GetMute(out var muted));
            return new VolumeState((int)Math.Round(applied * 100), muted, name);
        });
    }
    public static int StepVolume(int current, int direction) => Math.Clamp(direction > 0 ? current / 5 * 5 + 5 : (current + 4) / 5 * 5 - 5, 0, 100);

    // Volume só do jogo: sessões de áudio do Windows (o mesmo mixer de volume de todo Windows 10/11) dos processos do
    // jogo, em todas as saídas ativas (fone, caixas, TV). Não toca em outros apps (Discord, navegador) nem no volume geral.
    public record AppVolume(int Level, bool Muted);
    public static AppVolume? SessionVolume(Func<int, bool> match) => WithSessions(match, null);
    public static AppVolume? SetSessionVolume(Func<int, bool> match, int level) => WithSessions(match, Math.Clamp(level, 0, 100));
    static AppVolume? WithSessions(Func<int, bool> match, int? set) {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try {
            if (enumerator.EnumAudioEndpoints(0, 1, out var devices) != 0) return null;
            try {
                if (devices.GetCount(out var deviceCount) != 0) return null;
                AppVolume? result = null;
                for (uint d = 0; d < deviceCount; d++) {
                    if (devices.Item(d, out var device) != 0) continue;
                    try {
                        var found = DeviceSessions(device, match, set);
                        if (found != null && (result == null || found.Level > result.Level)) result = found;
                    }
                    catch (COMException) { } // saída removida durante a leitura: ignora só ela
                    finally { Marshal.ReleaseComObject(device); }
                }
                return result;
            } finally { Marshal.ReleaseComObject(devices); }
        } finally { Marshal.ReleaseComObject(enumerator); }
    }
    static AppVolume? DeviceSessions(IMMDevice device, Func<int, bool> match, int? set) {
        var iid = typeof(IAudioSessionManager2).GUID;
        Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, 0, out var pointer));
        var manager = (IAudioSessionManager2)Marshal.GetObjectForIUnknown(pointer); Marshal.Release(pointer);
        try {
            Marshal.ThrowExceptionForHR(manager.GetSessionEnumerator(out var sessions));
            try {
                Marshal.ThrowExceptionForHR(sessions.GetCount(out var count));
                AppVolume? result = null;
                for (var i = 0; i < count; i++) {
                    if (sessions.GetSession(i, out var control) != 0) continue;
                    try {
                        if (control is not IAudioSessionControl2 control2 || control2.GetProcessId(out var pid) != 0 || pid == 0 || !match((int)pid)) continue;
                        var unknown = Marshal.GetIUnknownForObject(control); var simpleId = typeof(ISimpleAudioVolume).GUID;
                        var hr = Marshal.QueryInterface(unknown, ref simpleId, out var simplePointer); Marshal.Release(unknown);
                        if (hr != 0) continue;
                        var volume = (ISimpleAudioVolume)Marshal.GetObjectForIUnknown(simplePointer); Marshal.Release(simplePointer);
                        try {
                            var context = Guid.Empty;
                            if (set is int level) {
                                Marshal.ThrowExceptionForHR(volume.SetMasterVolume(level / 100f, ref context));
                                if (level > 0) volume.SetMute(false, ref context);
                            }
                            volume.GetMasterVolume(out var applied); volume.GetMute(out var muted);
                            var current = new AppVolume((int)Math.Round(applied * 100), muted);
                            if (result == null || current.Level > result.Level) result = current;
                        } finally { Marshal.ReleaseComObject(volume); }
                    } finally { Marshal.ReleaseComObject(control); }
                }
                return result;
            } finally { Marshal.ReleaseComObject(sessions); }
        } finally { Marshal.ReleaseComObject(manager); }
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2 {
        [PreserveSig] int GetAudioSessionControl(nint group, uint flags, out nint control);
        [PreserveSig] int GetSimpleAudioVolume(nint group, uint flags, out nint volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator { [PreserveSig] int GetCount(out int count); [PreserveSig] int GetSession(int index, out IAudioSessionControl session); }
    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid group);
        [PreserveSig] int SetGroupingParam(ref Guid group, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(nint notify);
        [PreserveSig] int UnregisterAudioSessionNotification(nint notify);
    }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2 {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid group);
        [PreserveSig] int SetGroupingParam(ref Guid group, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(nint notify);
        [PreserveSig] int UnregisterAudioSessionNotification(nint notify);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint pid);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume {
        [PreserveSig] int SetMasterVolume(float level, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
    static T WithEndpointVolume<T>(Func<IAudioEndpointVolume, string, T> action)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out var device) != 0) throw new InvalidOperationException("Nenhuma saída de áudio ativa no Windows.");
            try
            {
                Marshal.ThrowExceptionForHR(device.GetId(out var id));
                var name = List().FirstOrDefault(d => d.Id == id)?.Name ?? "Saída de áudio";
                var iid = typeof(IAudioEndpointVolume).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, 0, out var pointer));
                var volume = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(pointer);
                Marshal.Release(pointer);
                try { return action(volume, name); }
                finally { Marshal.ReleaseComObject(volume); }
            }
            finally { Marshal.ReleaseComObject(device); }
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(nint notify);
        [PreserveSig] int UnregisterControlChangeNotify(nint notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);
    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public nint Pointer; }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection { [PreserveSig] int GetCount(out uint count); [PreserveSig] int Item(uint index, out IMMDevice device); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, nint activation, out nint result);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] class PolicyConfig { }
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        void GetMixFormat(); void GetDeviceFormat(); void ResetDeviceFormat(); void SetDeviceFormat();
        void GetProcessingPeriod(); void SetProcessingPeriod(); void GetShareMode(); void SetShareMode();
        void GetPropertyValue(); void SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    }
}
