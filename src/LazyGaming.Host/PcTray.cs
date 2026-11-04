using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LazyGaming;

// Own STA/message loop: no visible window, no ownership of game/controller focus.
public sealed class PcTray : IDisposable
{
    readonly Thread thread;
    Control? dispatcher;
    volatile bool disposed;
    public PcTray(Func<bool> enabled, Action<bool> setEnabled, Func<Task> launcher, Action hub, Func<Task> quit) {
        thread = new Thread(() => {
            try {
            using var control = new Control(); _ = control.Handle; dispatcher = control;
            using var context = new ApplicationContext();
            using var activeIcon = MakeIcon(Color.FromArgb(180,173,214));
            using var pausedIcon = MakeIcon(Color.Gray);
            using var menu = new ContextMenuStrip();
            using var icon = new NotifyIcon { Icon = activeIcon, Text = "LazyGaming · Ativo", ContextMenuStrip = menu };
            var notices = new NoticePolicy();
            async Task Execute(Func<Task> action) {
                try { await action(); }
                catch (Exception e) {
                    RuntimeDiagnostics.Report("tray", "action-failed", e);
                    var message = e is InvalidOperationException ? e.Message : "Não foi possível concluir. Tente novamente.";
                    if (!disposed && notices.Accept(message,Environment.TickCount64)) {
                        try { icon.ShowBalloonTip(NoticePolicy.ReadingDuration(message),"LazyGaming",message,ToolTipIcon.Info); }
                        catch (System.ComponentModel.Win32Exception) { } catch (InvalidOperationException) { }
                    }
                }
            }
            menu.Items.Add("Abrir launcher",null,async (_,_) => await Execute(launcher));
            menu.Items.Add("Central de configurações",null,(_,_) => hub());
            var toggle = new ToolStripMenuItem();
            toggle.Click += async (_,_) => await Execute(() => { setEnabled(!enabled()); Refresh(); return Task.CompletedTask; });
            menu.Items.Add(toggle); menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Sair",null,async (_,_) => await Execute(quit));
            icon.DoubleClick += (_,_) => hub();
            void Refresh() {
                bool running = enabled(); toggle.Text = running ? "Pausar funcionamento" : "Ativar funcionamento";
                toggle.Checked = running; icon.Icon = running ? activeIcon : pausedIcon;
                icon.Text = running ? "LazyGaming · Ativo" : "LazyGaming · Pausado";
            }
            using var timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += (_,_) => {
                try { Refresh(); }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or System.Runtime.InteropServices.ExternalException) { timer.Stop(); }
            }; timer.Start(); Refresh();
            icon.Visible = !disposed;
            try { if (!disposed) System.Windows.Forms.Application.Run(context); }
            finally { icon.Visible = false; dispatcher = null; }
            } catch (Exception e) when (e is System.Runtime.InteropServices.ExternalException or InvalidOperationException or ArgumentException or IOException) { dispatcher = null; }
        }) { IsBackground = true, Name = "LazyGaming tray" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
    }
    static Icon MakeIcon(Color color) {
        using var bitmap = new Bitmap(32,32);
        using (var g = Graphics.FromImage(bitmap)) {
            g.Clear(Color.Transparent); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(color); g.FillEllipse(fill,1,1,30,30);
            using var pen = new Pen(Color.White,4); g.DrawLines(pen,[new Point(11,8),new Point(11,23),new Point(23,23)]);
        }
        var handle = bitmap.GetHicon();
        try { using var original = Icon.FromHandle(handle); return (Icon)original.Clone(); }
        finally { DestroyIcon(handle); }
    }
    public void Dispose() {
        disposed = true;
        var control = dispatcher;
        try { if (control is { IsDisposed: false }) control.BeginInvoke(() => System.Windows.Forms.Application.ExitThread()); }
        catch (InvalidOperationException) { }
    }
    [DllImport("user32.dll")] static extern bool DestroyIcon(nint handle);
}
