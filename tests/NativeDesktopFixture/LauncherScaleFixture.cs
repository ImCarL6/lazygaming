using System.Text.Json;
using System.Drawing;
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
            Console.WriteLine(JsonSerializer.Serialize(new { legacyDpi, scale = scale.Clone(), placement = launcher.Placement, data }));
            if (!legacyDpi) {
                if (scale.GetProperty("awareness").GetInt32() != 2) throw new Exception("launcher deve ser per-monitor-aware");
                var placement = JsonSerializer.SerializeToElement(launcher.Placement);
                var width = placement.GetProperty("width").GetInt32();
                var height = placement.GetProperty("height").GetInt32();
                if (width < 640 || height < 360) throw new Exception("launcher deve abrir com o tamanho final, nunca com o tamanho padrão do Form");
                var expectedZoom = NativeDesktop.LauncherZoom(width, height, scale.GetProperty("dpi").GetInt32());
                if (Math.Abs(scale.GetProperty("zoom").GetDouble() - expectedZoom) > .005) throw new Exception("zoom deve acompanhar a área física final");
                if (scale.GetProperty("dpi").GetInt32() != scale.GetProperty("managedDpi").GetInt32())
                    throw new Exception("DPI gerenciado deve acompanhar o DPI real da janela");
                var browser = scale.GetProperty("browser");
                var ratio = browser.GetProperty("dpr").GetDouble();
                double physicalWidth = JsonSerializer.SerializeToElement(launcher.Placement).GetProperty("width").GetDouble();
                if (Math.Abs(browser.GetProperty("width").GetDouble() * ratio - physicalWidth) > ratio + 1)
                    throw new Exception("viewport deve corresponder aos pixels físicos, sem ampliação adicional do Windows");
            }
            await launcher.Hide(false);
            await launcher.Toggle();
            await Task.Delay(700);
            var reopened = JsonSerializer.SerializeToElement(launcher.Placement);
            if (reopened.GetProperty("width").GetInt32() != scale.GetProperty("formWidth").GetInt32() ||
                reopened.GetProperty("height").GetInt32() != scale.GetProperty("formHeight").GetInt32())
                throw new Exception("reabrir deve preservar o tamanho final");
            await launcher.Hide(false);

            using var native = new NativeDesktop(Path.Combine(data, "screens"), new Uri("http://127.0.0.1:47836"), () => 0);
            var handle = await native.Open("http://127.0.0.1:47836/");
            Rectangle[] screens;
            using (WindowFocus.PhysicalCoordinates()) screens = Screen.AllScreens.Select(s => s.Bounds).ToArray();
            foreach (var bounds in screens) {
                await native.Show(bounds);
                CheckBounds(handle, bounds, "Show");
                await Task.Delay(800);
                CheckScale(native, bounds, "Show");
                await native.Hide();
                await native.Show(bounds);
                CheckBounds(handle, bounds, "reabrir");
                await Task.Delay(600);
                CheckScale(native, bounds, "reabrir");
            }
            // Também atravessar monitores com o painel já visível (DPI distinto).
            foreach (var bounds in screens.Reverse()) {
                await native.Align(bounds);
                CheckBounds(handle, bounds, "Align");
                await Task.Delay(700);
                CheckScale(native, bounds, "Align");
            }
            await native.Hide();
            Console.WriteLine("PASS: abertura, reabertura e mudança de DPI nas telas físicas conectadas.");
        }
        finally { launcher.Close(); hidden?.BeginInvoke(() => { hidden.Close(); Application.ExitThread(); }); await app.StopAsync(); }
    }
    static void CheckBounds(nint handle, Rectangle bounds, string phase) {
        if (!WindowFocus.MatchesBounds(handle, bounds)) throw new Exception(phase + ": janela não ocupa o destino " + bounds + " " + JsonSerializer.Serialize(WindowFocus.Inspect(handle)));
    }
    static void CheckScale(NativeDesktop native, Rectangle bounds, string phase) {
        var status = JsonSerializer.SerializeToElement(native.Status);
        var scale = status.GetProperty("scale");
        var dpi = scale.GetProperty("dpi").GetInt32();
        var browser = scale.GetProperty("browser");
        var ratio = browser.GetProperty("dpr").GetDouble();
        Console.WriteLine(JsonSerializer.Serialize(new { phase, bounds, scale }));
        if (scale.GetProperty("formWidth").GetInt32() != bounds.Width || scale.GetProperty("formHeight").GetInt32() != bounds.Height ||
            scale.GetProperty("webWidth").GetInt32() != bounds.Width || scale.GetProperty("webHeight").GetInt32() != bounds.Height)
            throw new Exception(phase + ": Form e WebView devem preencher a tela física");
        if (dpi != scale.GetProperty("managedDpi").GetInt32() || Math.Abs(scale.GetProperty("zoom").GetDouble() - NativeDesktop.LauncherZoom(bounds.Width, bounds.Height, dpi)) > .005)
            throw new Exception(phase + ": DPI/zoom devem acompanhar o destino");
        if (Math.Abs(browser.GetProperty("width").GetDouble() * ratio - bounds.Width) > ratio + 1 ||
            Math.Abs(browser.GetProperty("height").GetDouble() * ratio - bounds.Height) > ratio + 1)
            throw new Exception(phase + ": viewport não corresponde à tela física");
    }
}
