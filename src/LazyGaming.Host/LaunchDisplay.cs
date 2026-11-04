using System.Drawing;
using System.Runtime.InteropServices;

namespace LazyGaming;

public static class LaunchDisplay
{
    public sealed record Target(int Monitor, Rectangle Bounds);
    public static Target Resolve(string display, IReadOnlyList<Displays.DesktopScreen> layout, IReadOnlyList<string> monitors) {
        var matches=layout.Where(s=>s.GdiName.Equals(display,StringComparison.OrdinalIgnoreCase)).ToArray();
        var indices=monitors.Select((name,index)=>(name,index)).Where(s=>s.name.Equals(display,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(matches.Length!=1 || indices.Length!=1 || matches[0].Bounds.Width<=0 || matches[0].Bounds.Height<=0)
            throw new InvalidOperationException("Não foi possível confirmar a tela de jogo. Confira a tela escolhida na Central.");
        return new(indices[0].index+1,matches[0].Bounds);
    }
    public static Target Current() {
        // WinForms Screen.AllScreens can retain a previous topology on this thread.
        // Resolve both the active pixel size and monitor order from fresh OS data.
        var names=new List<string>();
        MonitorCallback callback=(monitor,_,_,_)=>{
            var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
            if(GetMonitorInfo(monitor,ref info))names.Add(info.Device);return true;
        };
        if(!EnumDisplayMonitors(0,0,callback,0))throw new InvalidOperationException("Não foi possível consultar as telas.");
        return Resolve(Displays.GameDisplay(),Displays.DesktopLayout(),names);
    }
    delegate bool MonitorCallback(nint monitor,nint dc,nint rect,nint data);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct MonitorInfo {
        public int Size;public RectangleBounds Monitor,Work;public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Device;
    }
    [StructLayout(LayoutKind.Sequential)] struct RectangleBounds{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)]static extern bool EnumDisplayMonitors(nint dc,nint clip,MonitorCallback callback,nint data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] [return:MarshalAs(UnmanagedType.Bool)]static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
}
