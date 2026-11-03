using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Text.Json;

namespace LazyGaming;

// Fecha somente o menu de estouro do Explorer, pelo mesmo padrão de acessibilidade
// do botão. Não oculta a barra de tarefas nem envia Escape/cliques ao jogo.
public sealed class TaskbarOverflow(string directory)
{
    long nextCheck;
    int pending;

    public static bool Collapse(Func<bool> isOpen, Action toggle, Func<bool> allowed)
    {
        if (!allowed() || !isOpen()) return false;
        // Não alternar um botão já fechado se o estado mudou durante a consulta.
        if (!allowed() || !isOpen()) return false;
        toggle();
        return !isOpen();
    }

    public void Schedule(nint window, Running process, Func<bool> active)
    {
        long now = Environment.TickCount64;
        if (now < Volatile.Read(ref nextCheck) || Interlocked.CompareExchange(ref pending, 1, 0) != 0) return;
        Volatile.Write(ref nextCheck, now + 2000);
        int pid = process.Pid;
        IProcessLifetime lifetime;
        try { lifetime = ProcessImage.Track(pid, process.Path); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) {
            Volatile.Write(ref pending, 0); return;
        }
        uint input = WindowFocus.InputStamp();
        _ = Task.Run(() => {
            bool Allowed() => !lifetime.Exited && input != uint.MaxValue && WindowFocus.InputStamp() == input && active()
                && WindowFocus.BelongsTo(window, pid) && WindowFocus.CanPresent(window);
            try {
                if (TryDismiss(window, Allowed) && Allowed()) {
                    bool focused = WindowFocus.ActivateGame(window);
                    Write(new { at = DateTime.UtcNow, pid, closed = true, focused });
                }
            } catch (Exception error) {
                Write(new { at = DateTime.UtcNow, pid, closed = false, error = error.GetType().Name, error.HResult });
            } finally { lifetime.Dispose(); Volatile.Write(ref pending, 0); }
        });
    }

    static bool TryDismiss(nint gameWindow, Func<bool> allowed)
    {
        if (!allowed()) return false;
        var tray = FindWindow("Shell_TrayWnd", null);
        if (tray == 0) return false;
        GetWindowThreadProcessId(tray, out uint shellPid);
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        if (!string.Equals(ProcessImage.PathOf((int)shellPid), shell, StringComparison.OrdinalIgnoreCase)) return false;
        using var dpi = WindowFocus.PhysicalCoordinates();
        if (!GetWindowRect(tray, out var trayRect) || !GetWindowRect(gameWindow, out var gameRect)
            || trayRect.Right <= gameRect.Left || trayRect.Left >= gameRect.Right
            || trayRect.Bottom <= gameRect.Top || trayRect.Top >= gameRect.Bottom) return false;
        var root = AutomationElement.FromHandle(tray);
        if (root.Current.ProcessId != shellPid) return false;
        var button = root.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.AutomationIdProperty, "OverflowButton"),
            new PropertyCondition(AutomationElement.ClassNameProperty, "ToggleButton")));
        if (button == null || button.Current.ProcessId != shellPid
            || !button.TryGetCurrentPattern(TogglePattern.Pattern, out object pattern)) return false;
        var toggle = (TogglePattern)pattern;
        return Collapse(() => toggle.Current.ToggleState == ToggleState.On, toggle.Toggle, allowed);
    }

    void Write(object state)
    {
        try { File.WriteAllText(Path.Combine(directory, "taskbar-overflow-status.json"), JsonSerializer.Serialize(state)); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint FindWindow(string name, string? title);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint window, out Rect rect);
}
