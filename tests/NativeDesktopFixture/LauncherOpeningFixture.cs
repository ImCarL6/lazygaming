using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using LazyGaming;

static class LauncherOpeningFixture
{
    public static async Task Run()
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var data = Path.Combine(Path.GetTempPath(), "lg-opening-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        Form? source = null;
        var ready = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ui = new Thread(() => {
            using var window = new Form { Text = "LazyGaming focus test", StartPosition = FormStartPosition.Manual,
                Bounds = new Rectangle(100, 100, 480, 260) };
            source = window;
            window.Shown += (_, _) => ready.TrySetResult(window.Handle);
            Application.Run(window);
        }) { IsBackground = true };
        ui.SetApartmentState(ApartmentState.STA); ui.Start();
        var handle = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:47835");
        await using var app = builder.Build();
        app.MapGet("/", () => Results.Content("<html><body><button autofocus>Teste isolado de foco do launcher</button></body></html>", "text/html"));
        await app.StartAsync();
        var launcher = new DesktopLauncher(47835, data, () => 0);
        string? issue = null;
        launcher.FocusIssue = text => issue = text;
        launcher.ControllerReleased = () => true;
        int repaired = 0;
        try {
            for (int cycle = 0; cycle < 3; cycle++) {
                Check(WindowFocus.ActivateInputWindow(handle), "fixture fonte deve receber foco");
                await launcher.Toggle();
                Console.WriteLine(JsonSerializer.Serialize(new { cycle, stage = "opened", launcher.Visible, launcher.Focused, accepts = launcher.AcceptsControllerInput, launcher.Discovery, launcher.Placement }));
                Check(launcher.Visible && launcher.Focused && launcher.AcceptsControllerInput, "abertura confirma foco completo antes de navegar");
                await Task.Delay(700);
                // Mesmo efeito de um jogo que ativa sua janela depois do menu abrir.
                Check(WindowFocus.ActivateInputWindow(handle), "perda tardia controlada deve ocorrer");
                for (int attempt = 0; attempt < 30 && !launcher.AcceptsControllerInput; attempt++) await Task.Delay(100);
                Check(launcher.Visible && launcher.AcceptsControllerInput && issue == null, "foco tardio recuperado sem fechar e reabrir launcher");
                using var state = JsonDocument.Parse(JsonSerializer.Serialize(launcher.Discovery));
                Check(state.RootElement.GetProperty("opening").GetProperty("repairs").GetInt32() > 0, "reparo deve ter sido executado");
                repaired++;
                await launcher.Hide(false);
                Check(!launcher.Visible, "ocultar cancela observação anterior");
                Check(WindowFocus.ActivateInputWindow(handle), "fixture volta a foreground após ocultar");
                await Task.Delay(200);
                Check(WindowFocus.IsForeground(handle), "observação cancelada não reabre/retoma foco");
            }
            Console.WriteLine(JsonSerializer.Serialize(new { passed = true, nativeCycles = repaired, delayedFocusRepaired = true,
                hiddenWatchCancelled = true, realGameStarted = false, data }));
        }
        finally {
            launcher.Close(); source?.BeginInvoke(source.Close);
            await app.StopAsync();
        }
    }
}
