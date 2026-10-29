using System.Drawing;
using System.Windows.Forms;
using LazyGaming;

var ready = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
Form? source = null;
var ui = new Thread(() => {
    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
    using var pattern = new Pattern(); source = pattern;
    pattern.Shown += (_, _) => ready.TrySetResult(pattern.Handle);
    Application.Run(pattern);
}) { IsBackground = true };
ui.SetApartmentState(ApartmentState.STA); ui.Start();
var handle = await ready.Task;
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:47834");
var app = builder.Build();
app.MapGet("/", () => Results.Content("""
<!doctype html><html style="background:transparent"><head><meta charset="utf-8"></head><body style="margin:0;font:28px Segoe UI;color:white;height:100vh;background:linear-gradient(90deg,#070c16d9,#080e1880 45%,#090e1820 80%),linear-gradient(0deg,#0a101be6,transparent 55%,#060a1280)">
<p style="padding:36px">LazyGaming — composição nativa</p><p style="padding:36px">Menu HTML sobre superfície GPU do Windows</p>
<script>chrome.webview.addEventListener('message',e=>{document.body.dataset.active=e.data.active});LGNativeDesktop.background(true);</script>
</body></html>
""", "text/html"));
await app.StartAsync();
using var desktop = new NativeDesktop(Path.Combine(Path.GetTempPath(), "lazygaming-native-fixture"), new("http://127.0.0.1:47834"), () => handle);
await desktop.Open("http://127.0.0.1:47834/");
await desktop.Show(Screen.PrimaryScreen!.Bounds);
await Task.Delay(1500);
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(desktop.Status));
await Task.Delay(300000);
await desktop.Hide(); desktop.Dispose();
source?.BeginInvoke(source.Close);
await app.StopAsync();

sealed class Pattern : Form {
    readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    int tick;
    public Pattern() {
        Text = "LazyGaming native source fixture"; FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual; Bounds = Screen.PrimaryScreen!.Bounds; BackColor = Color.FromArgb(28, 95, 110); DoubleBuffered = true;
        timer.Tick += (_, _) => { tick++; Invalidate(); }; timer.Start();
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        using var grid = new Pen(Color.FromArgb(100, 255, 255, 255));
        for (int x = 0; x < Width; x += 8) e.Graphics.DrawLine(grid, x, 0, x, Height);
        for (int y = 0; y < Height; y += 8) e.Graphics.DrawLine(grid, 0, y, Width, y);
        using var font = new Font("Segoe UI", 44);
        e.Graphics.DrawString("FONTE NATIVA • SEM JPEG\n" + Width + " × " + Height + "\n" + tick, font, Brushes.White, Width / 3, Height / 3);
        e.Graphics.FillRectangle(Brushes.Orange, tick * 7 % Math.Max(1, Width - 180), Height * 3 / 4, 180, 90);
    }
    protected override void Dispose(bool disposing) { timer.Dispose(); base.Dispose(disposing); }
}
