using System.Drawing;
using System.Drawing.Drawing2D;
using System.Media;
using System.Windows.Forms;

namespace LazyGaming;

public sealed class ControllerFeedback : IDisposable
{
    FeedbackWindow? window;
    sealed record Notice(bool? Enabled, string? Text, int Duration);
    Notice? pending;
    readonly NoticePolicy messages = new(), modes = new();
    volatile bool disposed;
    readonly Thread ui;
    public ControllerFeedback(bool inspection = false)
    {
        ui = new Thread(() =>
        {
            try {
                using var form = new FeedbackWindow(inspection); _ = form.Handle;
                Volatile.Write(ref window, form);
                form.BeginInvoke(Drain);
                if (!disposed) System.Windows.Forms.Application.Run();
            } catch (Exception e) when (e is System.Runtime.InteropServices.ExternalException or InvalidOperationException or ArgumentException or IOException) { }
            finally { window = null; Interlocked.Exchange(ref pending,null); }
        }) { IsBackground = true, Name = "LazyGaming feedback" };
        ui.SetApartmentState(ApartmentState.STA); ui.Start();
    }
    public void Notify(bool enabled, int duration = 2800)
    {
        if (disposed || !modes.Accept(enabled.ToString(),Environment.TickCount64,800,0)) return;
        Interlocked.Exchange(ref pending,new(enabled,null,Math.Clamp(duration,500,3000)));
        var form = Volatile.Read(ref window);
        try { if (form is { IsHandleCreated: true }) form.BeginInvoke(Drain); } catch { }
    }
    public void NotifyMessage(string text) {
        if (disposed || string.IsNullOrWhiteSpace(text) || !messages.Accept(text,Environment.TickCount64)) return;
        Interlocked.Exchange(ref pending,new(null,text,NoticePolicy.ReadingDuration(text)));
        var form = Volatile.Read(ref window);
        try { if (form is { IsHandleCreated: true }) form.BeginInvoke(Drain); } catch { }
    }
    void Drain() {
        var form = window; var item = Interlocked.Exchange(ref pending,null);
        if (disposed || form == null || item == null) return;
        try { form.Notify(item.Enabled,item.Text,item.Duration); }
        catch (Exception e) when (e is System.Runtime.InteropServices.ExternalException or InvalidOperationException or ArgumentException) { }
    }
    public void Dispose()
    {
        disposed = true; Interlocked.Exchange(ref pending,null);
        var form = Volatile.Read(ref window);
        try { if (form is { IsHandleCreated: true }) form.BeginInvoke(() => { form.Close(); System.Windows.Forms.Application.ExitThread(); }); } catch { }
    }
    sealed class FeedbackWindow : Form
    {
        readonly System.Windows.Forms.Timer timer = new() { Interval = 20 };
        readonly SoundPlayer on = new(Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "audio", "mouse-on.wav"));
        readonly SoundPlayer off = new(Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "audio", "mouse-off.wav"));
        bool mode;
        string? notice;
        long since;
        float scale;
        int duration = 1900;
        readonly bool inspection;
        readonly bool onLoaded, offLoaded;
        public FeedbackWindow(bool inspection)
        {
            this.inspection = inspection;
            Text = "LazyGaming — Mouse"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = inspection;
            TopMost = true; DoubleBuffered = true; BackColor = Color.FromArgb(23, 29, 40); StartPosition = FormStartPosition.Manual;
            timer.Tick += (_, _) =>
            {
                var age = Environment.TickCount64 - since;
                if (age >= duration) { timer.Stop(); Hide(); return; }
                Opacity = Math.Min(0.97, Math.Min(age / 110.0, (duration - age) / 240.0));
            };
            try { on.Load(); onLoaded = true; } catch { }
            try { off.Load(); offLoaded = true; } catch { }
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000020; if (!inspection) p.ExStyle |= 0x00000080; return p; } }
        public void Notify(bool? enabled, string? text, int duration)
        {
            this.duration = duration;
            notice = text; mode = enabled == true; var screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            scale = Math.Clamp(screen.Width / 1920f, 1, 2); Size = new((int)((notice == null ? 430 : 680) * scale), (int)((notice == null ? 84 : 102) * scale));
            Location = new(screen.Left + (screen.Width - Width) / 2, screen.Bottom - Height - (int)(60 * scale));
            using var shape = new GraphicsPath(); float radius = 20 * scale;
            shape.AddRoundedRectangle(new RectangleF(0, 0, Width, Height), new SizeF(radius, radius)); Region = new Region(shape);
            since = Environment.TickCount64; Opacity = 0.01; Show(); Invalidate(); timer.Start();
            if (enabled.HasValue && (enabled.Value ? onLoaded : offLoaded)) try { (enabled.Value ? on : off).Play(); } catch { }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var accent = mode ? Color.FromArgb(194, 181, 221) : Color.FromArgb(172, 185, 205);
            using var pen = new Pen(accent, 2.3f * scale);
            using var mouse = new GraphicsPath(); mouse.AddRoundedRectangle(new RectangleF(28 * scale, 22 * scale, 29 * scale, 41 * scale), new SizeF(14 * scale, 14 * scale));
            if (notice == null) { e.Graphics.DrawPath(pen, mouse); e.Graphics.DrawLine(pen, 42.5f * scale, 22 * scale, 42.5f * scale, 38 * scale); }
            using var font = new Font("Segoe UI", 19 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.FromArgb(243, 247, 252));
            e.Graphics.DrawString(notice ?? (mode ? "Mouse ativado" : "Mouse desativado"), font, brush, new RectangleF(78 * scale, 22 * scale, Width - 118 * scale, Height - 28 * scale));
            using var dot = new SolidBrush(accent); e.Graphics.FillEllipse(dot, Width - 34 * scale, 37 * scale, 8 * scale, 8 * scale);
        }
        protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); on.Dispose(); off.Dispose(); } base.Dispose(disposing); }
    }
}
