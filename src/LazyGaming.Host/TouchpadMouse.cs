namespace LazyGaming;

public sealed class TouchpadMouse(Func<bool> sessionActive, Action<bool>? feedback = null, Action? launcher = null, Func<bool>? navigationActive = null, Action<int, bool>? navigationKey = null, IControllerSource? source = null, IMouseOutput? mouse = null)
{
    readonly ControllerProcessor processor = new();
    readonly IControllerSource controllerSource = source ?? new WindowsControllerSource();
    readonly IMouseOutput mouseOutput = mouse ?? new WindowsMouseOutput();
    public bool Enabled => processor.Enabled;
    volatile bool connected, touchAvailable;
    public bool Released => !connected || processor.Released(Environment.TickCount64);
    string? issue;
    long lastReport; int reportBytes, reportId;
    public object Status => new { enabled = Enabled, connected, touchAvailable, released = Released, reportBytes, reportId,
        reportAgeMs = lastReport == 0 ? (long?)null : Environment.TickCount64 - Interlocked.Read(ref lastReport), issue };
    public async Task Run(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            int retryDelay = 3000;
            try
            {
                using var stream = controllerSource.Open();
                if (stream == null) { connected = false; touchAvailable = false; processor.Reset(Environment.TickCount64); }
                else
                {
                    processor.Reset(Environment.TickCount64); connected = true; issue = null;
                    var buffer = new byte[stream.ReportLength];
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    idle.CancelAfter(2000);
                    while (!cancellation.IsCancellationRequested)
                    {
                        int length;
                        try { length = await stream.Input.ReadAsync(buffer, idle.Token); }
                        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new TimeoutException(); }
                        if (length == 0) break;
                        idle.CancelAfter(2000);
                        long now = Environment.TickCount64;
                        Interlocked.Exchange(ref lastReport, now); reportBytes = length; reportId = buffer[0];
                        var state = stream.Adapter.Decode(buffer.AsSpan(0, length));
                        touchAvailable = state?.Touch != null;
                        var result = processor.Process(state, now, sessionActive(), navigationActive?.Invoke() == true);
                        foreach (var key in result.Navigation) navigationKey?.Invoke(key.Key, key.Repeat);
                        foreach (var action in result.Mouse) mouseOutput.Send(action);
                        if (result.ToggleLauncher) { try { launcher?.Invoke(); } catch { } }
                        if (result.MouseModeChanged is bool enabled) { try { feedback?.Invoke(enabled); } catch { } }
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (TimeoutException) {
                connected = false; touchAvailable = false; processor.Reset(Environment.TickCount64);
                issue = "A leitura do DualSense parou; reconectando a leitura compartilhada."; retryDelay = 100;
            }
            catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            { connected = false; touchAvailable = false; processor.Reset(Environment.TickCount64); issue = "Não foi possível ler o controle compartilhado."; }
            connected = false; touchAvailable = false; processor.Reset(Environment.TickCount64);
            try { await Task.Delay(retryDelay, cancellation); } catch (OperationCanceledException) { break; }
        }
    }
    public static async Task Inspect()
    {
        using var stream = DualSenseHid.Open();
        if (stream == null) { Console.WriteLine("DualSense não disponível para leitura compartilhada."); return; }
        using var timeout = new CancellationTokenSource(3000);
        var buffer = new byte[stream.ReportLength];
        try
        {
            int length = await stream.Stream.ReadAsync(buffer, timeout.Token);
            Console.WriteLine($"DualSense conectado; bytes={length}; report=0x{buffer[0]:X2}; touchpad={(DualSenseReport.Parse(buffer.AsSpan(0, length)) != null ? "disponível" : "relatório básico sem toque")}. Nenhum comando de mouse enviado.");
        }
        catch (OperationCanceledException) { Console.WriteLine("DualSense aberto, aguardando relatórios. Nenhum comando de mouse enviado."); }
    }
}
