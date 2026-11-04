namespace LazyGaming;

public sealed class TouchpadMouse(Func<bool> sessionActive, Action<bool>? feedback = null, Action? launcher = null, Func<bool>? navigationActive = null, Action<int, bool>? navigationKey = null, IControllerSource? source = null, IMouseOutput? mouse = null, Func<PcPreferences>? preferences = null, Action<string, Exception?>? diagnostic = null)
{
    readonly ControllerProcessor processor = new();
    public ShortcutCapture Capture { get; } = new();
    readonly IControllerSource controllerSource = source ?? new WindowsControllerSource(() => preferences?.Invoke().ControllerSource ?? "auto");
    readonly IMouseOutput mouseOutput = mouse ?? new WindowsMouseOutput();
    public bool Enabled => processor.Enabled;
    volatile bool connected, touchAvailable, microphoneAvailable, validReport;
    public ControllerDetection Detection => PcSetup.Controller(connected && validReport ? adapterId : null, touchAvailable, microphoneAvailable, adapterBrand);
    public bool Released => !connected || processor.Released(Environment.TickCount64);
    string? issue, adapterId, adapterBrand;
    long lastReport; int reportBytes, reportId;
    long nextFeatureAttempt;
    int resetNavigation;
    public void ResetNavigation() => Interlocked.Exchange(ref resetNavigation, 1);
    public object Status => new { enabled = Enabled, connected, touchAvailable, adapterId, swapConfirmBack = preferences != null && SafeSwap(), released = Released, navigationArmed = processor.NavigationArmed, panelEligible = navigationActive?.Invoke() == true, reportBytes, reportId,
        reportAgeMs = lastReport == 0 ? (long?)null : Environment.TickCount64 - Interlocked.Read(ref lastReport), issue };
    // Nunca deixar o botão esquerdo preso: soltar antes de descartar o estado do controle.
    void ResetProcessor(long now) { if (processor.Dragging) try { mouseOutput.Send(new("left-up")); } catch { } processor.Reset(now); }
    bool SafeSwap() { try { return PcSetup.Effective(preferences!(), Detection).SwapConfirmBack; } catch { return false; } }
    public async Task Run(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            if (preferences?.Invoke().Enabled == false) {
                connected = false; touchAvailable = false; ResetProcessor(Environment.TickCount64);
                try { await Task.Delay(200, cancellation); } catch (OperationCanceledException) { break; }
                continue;
            }
            int retryDelay = 3000;
            try
            {
                using var stream = controllerSource.Open();
                string? sourceChoice = preferences?.Invoke().ControllerSource;
                if (stream == null) { connected = false; touchAvailable = false; ResetProcessor(Environment.TickCount64); }
                else
                {
                    ResetProcessor(Environment.TickCount64); connected = true; validReport = false; issue = null; adapterId = stream.Adapter.Id; adapterBrand = adapterId == "xinput" ? DualSenseHid.XInputBrand() : null;
                    var buffer = new byte[stream.ReportLength];
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    idle.CancelAfter(2000);
                    while (!cancellation.IsCancellationRequested)
                    {
                        int length;
                        // Prazo próprio: se o driver HID não honrar o cancelamento, a leitura não pode travar o loop;
                        // sair do using fecha o handle e aborta a operação pendente.
                        var read = stream.Input.ReadAsync(buffer, idle.Token).AsTask();
                        if (await Task.WhenAny(read, Task.Delay(2600, cancellation)) != read) { cancellation.ThrowIfCancellationRequested(); throw new TimeoutException(); }
                        try { length = await read; }
                        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new TimeoutException(); }
                        if (length == 0) break;
                        if (preferences?.Invoke().Enabled == false) { retryDelay = 100; break; }
                        if (sourceChoice != preferences?.Invoke().ControllerSource) { retryDelay = 100; break; }
                        idle.CancelAfter(2000);
                        long now = Environment.TickCount64;
                        Interlocked.Exchange(ref lastReport, now); reportBytes = length; reportId = buffer[0];
                        var state = stream.Adapter.Decode(buffer.AsSpan(0, length));
                        validReport = state != null;
                        touchAvailable = state?.Touch != null;
                        microphoneAvailable = state?.Microphone != null;
                        if(state!=null && adapterId=="dualsense" && !microphoneAvailable && buffer[0]==1 && length==78 &&
                            preferences?.Invoke().DualSenseBluetoothFeatures!=false && now>=nextFeatureAttempt && stream is IControllerFeatures features) {
                            nextFeatureAttempt=now+30000;
                            var handshake = features.EnableFullReports(cancellation);
                            if (await Task.WhenAny(handshake, Task.Delay(1000, cancellation)) != handshake) { diagnostic?.Invoke("bluetooth-handshake-timeout", null); throw new TimeoutException(); }
                        }
                        if (Capture.Observe(state,now)) { ResetProcessor(now); continue; }
                        if (Interlocked.Exchange(ref resetNavigation, 0) != 0) processor.ResetNavigation(now);
                        var result = processor.Process(state, now, sessionActive(), navigationActive?.Invoke() == true,
                            preferences is null ? null : PcSetup.Effective(preferences(),Detection));
                        foreach (var key in result.Navigation) navigationKey?.Invoke(key.Key, key.Repeat);
                        foreach (var action in result.Mouse) mouseOutput.Send(action);
                        if (result.ToggleLauncher) { try { launcher?.Invoke(); } catch { } }
                        if (result.MouseModeChanged is bool enabled) { try { feedback?.Invoke(enabled); } catch { } }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
            catch (TimeoutException) {
                connected = false; touchAvailable = false; ResetProcessor(Environment.TickCount64);
                issue = "A leitura do controle parou; reconectando a leitura compartilhada."; retryDelay = 100; try { diagnostic?.Invoke("read-timeout", null); } catch { }
            }
            catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            { connected = false; touchAvailable = false; ResetProcessor(Environment.TickCount64); issue = "Não foi possível ler o controle compartilhado."; }
            // Qualquer outra falha também não pode encerrar a leitura para sempre (sintoma: atalhos param até reiniciar).
            catch (Exception e) when (!cancellation.IsCancellationRequested) {
                connected = false; touchAvailable = false; ResetProcessor(Environment.TickCount64);
                issue = "A leitura do controle falhou e foi reiniciada."; retryDelay = 500;
                try { diagnostic?.Invoke("read-loop-fault", e); } catch { }
            }
            connected = false; touchAvailable = false; ResetProcessor(Environment.TickCount64);
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
            var state=stream.Adapter.Decode(buffer.AsSpan(0,length));
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { length,report=buffer[0],prefix=Convert.ToHexString(buffer.AsSpan(0,Math.Min(length,12))),state,readOnly=true }));
        }
        catch (OperationCanceledException) { Console.WriteLine("DualSense aberto, aguardando relatórios. Nenhum comando de mouse enviado."); }
    }
}
