using System.Text.Json;
using System.Windows.Forms;
using LazyGaming;

static class LauncherScaleFixture
{
    public static async Task Run(bool legacyDpi)
    {
        var data = Path.Combine(Path.GetTempPath(), "lg-scale-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        // O auxiliar cria primeiro a janela oculta de feedback em outra thread.
        // Reproduzir a ordem, pois ela inicializa o modo DPI do WinForms.
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Form? hidden = null;
        var feedback = new Thread(() => {
            using var form = new Form { ShowInTaskbar = false }; hidden = form;
            _ = form.Handle; ready.TrySetResult(); Application.Run();
        }) { IsBackground = true };
        feedback.SetApartmentState(ApartmentState.STA); feedback.Start();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:47836");
        await using var app = builder.Build();
        app.MapGet("/", () => Results.Content("<html><body><h1>Teste isolado de escala do launcher</h1></body></html>", "text/html"));
        await app.StartAsync();
        var launcher = new DesktopLauncher(47836, data, () => 0);
        launcher.ControllerReleased = () => true;
        try {
            await launcher.Toggle();
            await Task.Delay(1000);
            using var status = JsonDocument.Parse(JsonSerializer.Serialize(launcher.Discovery));
            var scale = status.RootElement.GetProperty("composition").GetProperty("scale");
            if (!legacyDpi) {
                if (scale.GetProperty("awareness").GetInt32() != 2) throw new Exception("launcher deve ser per-monitor-aware");
                if (scale.GetProperty("zoom").GetDouble() != 1) throw new Exception("zoom deve ser 100%");
                if (scale.GetProperty("dpi").GetInt32() != scale.GetProperty("managedDpi").GetInt32())
                    throw new Exception("DPI gerenciado deve acompanhar o DPI real da janela");
                var browser = scale.GetProperty("browser");
                var ratio = browser.GetProperty("dpr").GetDouble();
                double physicalWidth = JsonSerializer.SerializeToElement(launcher.Placement).GetProperty("width").GetDouble();
                if (Math.Abs(browser.GetProperty("width").GetDouble() * ratio - physicalWidth) > ratio + 1)
                    throw new Exception("viewport deve corresponder aos pixels físicos, sem ampliação adicional do Windows");
            }
            Console.WriteLine(JsonSerializer.Serialize(new { legacyDpi, scale = scale.Clone(), placement = launcher.Placement, data }));
            await launcher.Hide(false);
        }
        finally { launcher.Close(); hidden?.BeginInvoke(() => { hidden.Close(); Application.ExitThread(); }); await app.StopAsync(); }
    }
}
