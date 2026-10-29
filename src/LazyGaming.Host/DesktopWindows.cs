using System.Drawing;
using System.Runtime.InteropServices;

namespace LazyGaming;

// Só guarda handles/PIDs e geometria em memória; nunca títulos ou conteúdo.
// Preservação de apps é best effort e não relaxa a validação das telas.
public sealed class DesktopWindows : IDisposable
{
    sealed record Screen(string Device, Rectangle Bounds, Rectangle Work);
    sealed record Saved(nint Window, uint Pid, Placement Placement, Rectangle VisibleBounds, Screen Screen);
    readonly List<Saved> saved = [];
    public readonly List<object> Diagnostics = [];
    public object Inspect() => saved.Select(s => new { window = (long)s.Window, pid = s.Pid, state = s.Placement.Show, normal = s.Placement.Normal.Rectangle });
    public sealed record Verification(int Captured, int Confirmed, int Minimized, int Closed, int MissingScreen, int Unconfirmed);
    public Verification Verify() {
        Diagnostics.Clear();
        using var dpi = WindowFocus.PhysicalCoordinates();
        var screens = Screens();
        int confirmed = 0, minimized = 0, closed = 0, missing = 0, unconfirmed = 0;
        foreach (var entry in saved) {
            GetWindowThreadProcessId(entry.Window, out uint pid);
            if (!IsWindow(entry.Window) || pid != entry.Pid) { closed++; continue; }
            var screen = screens.Values.SingleOrDefault(s => s.Device == entry.Screen.Device);
            if (screen == null) { missing++; continue; }
            var current = new Placement { Length = (uint)Marshal.SizeOf<Placement>() };
            bool hasPlacement = GetWindowPlacement(entry.Window, ref current);
            if (entry.Placement.Show == 2 && hasPlacement && current.Show == 2) { minimized++; continue; }
            var info = Info(MonitorFromWindow(entry.Window, 2));
            var expected = Translate(entry.Placement.Normal.Rectangle, entry.Screen.Bounds, entry.Screen.Work, screen.Bounds, screen.Work);
            bool geometry = current.Normal.Rectangle == expected;
            if (entry.Placement.Show == 1) geometry = GetWindowRect(entry.Window, out var rect) && rect.Rectangle == TranslateVisible(entry.VisibleBounds, entry.Screen.Bounds, screen.Bounds);
            if (hasPlacement && info != null && screens.TryGetValue(info.Value.Device, out var actual) &&
                actual.Device == entry.Screen.Device && geometry && current.Show == entry.Placement.Show) confirmed++;
            else {
                unconfirmed++;
                Diagnostics.Add(new { before = entry.Placement.Normal.Rectangle, expected, actual = current.Normal.Rectangle,
                    beforeState = entry.Placement.Show, actualState = current.Show,
                    sameScreen = info != null && screens.TryGetValue(info.Value.Device, out var destination) && destination.Device == entry.Screen.Device });
            }
        }
        return new(saved.Count, confirmed, minimized, closed, missing, unconfirmed);
    }
    public static DesktopWindows Capture(nint excludedWindow = 0) {
        var result = new DesktopWindows();
        using var dpi = WindowFocus.PhysicalCoordinates();
        var screens = Screens();
        EnumWindows((window, _) => {
            if (window == excludedWindow || !IsWindowVisible(window) || GetWindow(window, 4) != 0) return true;
            long style = Style(window, -16), extended = Style(window, -20);
            if ((extended & 0x80) != 0 || (style & 0x00C40000) == 0 && (extended & 0x40000) == 0) return true;
            var placement = new Placement { Length = (uint)Marshal.SizeOf<Placement>() };
            if (!GetWindowPlacement(window, ref placement) || !GetWindowRect(window, out var rect)) return true;
            var info = Info(MonitorFromWindow(window, 2));
            if (info == null || !screens.TryGetValue(info.Value.Device, out var screen)) return true;
            GetWindowThreadProcessId(window, out uint pid);
            result.saved.Add(new(window, pid, placement, rect.Rectangle, screen));
            return true;
        }, 0);
        return result;
    }
    static Dictionary<string, Screen> Screens() {
        var result = new Dictionary<string, Screen>(StringComparer.OrdinalIgnoreCase);
        var layout = Displays.DesktopLayout();
        EnumDisplayMonitors(0, 0, (monitor, _, _, _) => {
            var info = Info(monitor);
            var physical = layout.SingleOrDefault(s => string.Equals(s.GdiName, info?.Device, StringComparison.OrdinalIgnoreCase));
            if (physical != null && info != null) result[info.Value.Device] = new(physical.DevicePath, physical.Bounds, info.Value.Work.Rectangle);
            return true;
        }, 0);
        return result;
    }
    // WINDOWPLACEMENT usa workspace, não coordenadas de SetWindowPos.
    // Compensa também a margem de taskbar, que pode mudar com a tela principal.
    public static Rectangle Translate(Rectangle normal, Rectangle oldBounds, Rectangle oldWork, Rectangle newBounds, Rectangle newWork) =>
        new(checked(normal.X + newBounds.X - oldBounds.X + oldWork.X - oldBounds.X - (newWork.X - newBounds.X)),
            checked(normal.Y + newBounds.Y - oldBounds.Y + oldWork.Y - oldBounds.Y - (newWork.Y - newBounds.Y)), normal.Width, normal.Height);
    public static Rectangle TranslateVisible(Rectangle visible, Rectangle oldBounds, Rectangle newBounds) =>
        new(checked(visible.X + newBounds.X - oldBounds.X), checked(visible.Y + newBounds.Y - oldBounds.Y), visible.Width, visible.Height);
    public void Restore() {
        using var dpi = WindowFocus.PhysicalCoordinates();
        using var focus = WindowFocus.KeepAcrossDisplayChange();
        var screenMap = Screens();
        var screens = screenMap.Values.ToArray();
        // O primeiro movimento pode gerar WM_DPICHANGED. Reconfirmar no destino
        // evita que a aplicação escale novamente o retângulo salvo no monitor anterior.
        for (int attempt = 0; attempt < 3; attempt++) {
        foreach (var entry in saved) {
            if (!IsWindow(entry.Window)) continue;
            GetWindowThreadProcessId(entry.Window, out uint pid);
            if (pid != entry.Pid) continue;
            var screen = screens.SingleOrDefault(s => s.Device == entry.Screen.Device);
            if (screen == null) continue; // Tela removida: deixar o Windows recuperar a janela.
            var desired = Translate(entry.Placement.Normal.Rectangle, entry.Screen.Bounds, entry.Screen.Work, screen.Bounds, screen.Work);
            var current = new Placement { Length = (uint)Marshal.SizeOf<Placement>() };
            if (!GetWindowPlacement(entry.Window, ref current)) continue;
            // Não há posição visível a preservar em minimizadas. Regravar seu
            // rcNormalPosition pode reescalá-lo pela DPI do principal. Mantê-las
            // minimizadas; o destino ao reabri-las fica sob controle do Windows.
            if (entry.Placement.Show == 2) continue;
            // Janela normal: mover o retângulo físico, sem reabrir a janela e
            // sem usar rcNormalPosition (que pode estar em cache, como no mstsc).
            if (entry.Placement.Show == 1) {
                var visible = TranslateVisible(entry.VisibleBounds, entry.Screen.Bounds, screen.Bounds);
                if (!GetWindowRect(entry.Window, out var rect) || rect.Rectangle != visible)
                    SetWindowPos(entry.Window, 0, visible.X, visible.Y, visible.Width, visible.Height, 0x4014);
                continue;
            }
            var info = Info(MonitorFromWindow(entry.Window, 2));
            bool sameScreen = info != null && screenMap.TryGetValue(info.Value.Device, out var physical) && physical.Device == screen.Device;
            if (sameScreen && current.Normal.Rectangle == desired && current.Show == entry.Placement.Show) continue;
            var placement = entry.Placement;
            placement.Normal = Rect.From(desired);
            placement.Flags = (placement.Flags & ~1u) | 4u; // Não fixar ícone minimizado; assíncrono entre threads.
            // Uma janela maximizada pode manter o monitor antigo mesmo com
            // rcNormalPosition correto. Mover em estado normal antes de maximizar,
            // como no Workspaces do PowerToys; minimizadas nunca são abertas.
            if (placement.Show == 3 && !sameScreen) {
                placement.Show = 4; SetWindowPlacement(entry.Window, ref placement); placement.Show = 3;
            }
            if (placement.Show == 1) placement.Show = 4;
            SetWindowPlacement(entry.Window, ref placement);
            SetWindowPlacement(entry.Window, ref placement);
        }
        Thread.Sleep(150);
        if (Verify().Unconfirmed == 0) break;
        }
    }
    public void Dispose() { } // Restauração explícita após sucesso ou rollback, nunca após validação recusada.
    static long Style(nint window, int index) => nint.Size == 8 ? GetWindowLongPtr(window, index).ToInt64() : GetWindowLong(window, index);
    static MonitorInfo? Info(nint monitor) {
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>(), Device = "" };
        return monitor != 0 && GetMonitorInfo(monitor, ref info) ? info : null;
    }
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct Rect {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle Rectangle => Rectangle.FromLTRB(Left, Top, Right, Bottom);
        public static Rect From(Rectangle r) => new() { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
    }
    [StructLayout(LayoutKind.Sequential)] struct Placement { public uint Length, Flags, Show; public Point Min, Max; public Rect Normal; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct MonitorInfo {
        public uint Size; public Rect Monitor, Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    delegate bool WindowCallback(nint window, nint data);
    delegate bool MonitorCallback(nint monitor, nint dc, nint rect, nint data);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, nint data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll")] static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] static extern bool GetWindowPlacement(nint window, ref Placement placement);
    [DllImport("user32.dll")] static extern bool SetWindowPlacement(nint window, ref Placement placement);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(nint window, int index);
}
