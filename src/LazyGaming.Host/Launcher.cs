using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
namespace LazyGaming;

public sealed class LauncherState
{
    readonly object gate = new();
    string id = ""; long revision;
    TaskCompletionSource changed = NewSignal();
    static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public object Select(string selected) { lock (gate) { if (id != selected) { id = selected; revision++; var old = changed; changed = NewSignal(); old.TrySetResult(); } return Snapshot(); } }
    object Snapshot() => new { id, revision };
    public async Task<object> Wait(long after, CancellationToken cancellation)
    {
        Task wait;
        lock (gate) { if (revision != after) return Snapshot(); wait = changed.Task; }
        try { await wait.WaitAsync(TimeSpan.FromSeconds(20), cancellation); } catch (TimeoutException) { }
        lock (gate) return Snapshot();
    }
}

// Janela exclusiva do launcher: não altera topologia, áudio ou janelas de outros apps.
public sealed class DesktopLauncher(int port, string directory, Func<nint>? gameWindow = null)
{
    readonly string cookie = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    readonly object gate = new();
    string token = "", inputClient = ""; DateTime tokenExpiry;
    Process? process; nint window, previous;
    NativeDesktop? native;
    readonly SemaphoreSlim toggleGate = new(1, 1);
    volatile bool opening, closing;
    CancellationTokenSource? openingWatch;
    object openingStatus = new { phase = "idle" };
    string lastOpeningDiagnostic = "";
    string ownershipError = "";
    object windowDiscovery = new { };
    public object Discovery => new { counts = windowDiscovery, ownershipError, composition = native?.Status, opening = openingStatus };
    public object InspectWindows() { var owned = OwnedWindows(); return new { discovery = windowDiscovery, ownershipError, windows = owned.Select(WindowFocus.Inspect).ToArray() }; }
    public Func<bool>? ControllerReleased { get; set; }
    public Action? ResetInput { get; set; }
    public Action<string>? FocusIssue { get; set; }
    public bool AcceptsControllerInput => Visible && Focused && !opening && !closing;
    public bool Visible => window != 0 && IsWindow(window) && IsWindowVisible(window);
    public bool Focused => Visible && GetForegroundWindow() == window && WindowFocus.HasInputFocus(window);
    public object Placement => WindowFocus.Inspect(window);
    public bool OwnsInput(string client) => client.Length >= 16 && Security.Equal(inputClient, client);
    public void SetInputClient(string client) { if (client.Length is >= 16 and <= 100) inputClient = client; }
    public bool Authenticate(string supplied) => Security.Equal(cookie, supplied);
    public string? Bootstrap(string supplied)
    {
        lock (gate) { if (DateTime.UtcNow > tokenExpiry || token.Length == 0 || !Security.Equal(token, supplied)) return null; token = ""; return cookie; }
    }
    public async Task Toggle()
    {
        if (!await toggleGate.WaitAsync(0)) return;
        try { await ToggleCore(); } finally { toggleGate.Release(); }
    }
    bool OwnsProcess(int pid)
    {
        object? service = null, rows = null;
        string stage = "bind";
        try {
            ownershipError = "";
            service = Marshal.BindToMoniker(@"winmgmts:\\.\root\cimv2");
            stage = "query";
            rows = ((dynamic)service).ExecQuery("SELECT CommandLine FROM Win32_Process WHERE ProcessId=" + pid);
            stage = "rows";
            foreach (dynamic row in (System.Collections.IEnumerable)rows) {
                stage = "command";
                object? properties = null, property = null;
                try {
                    // Propriedade WMI explícita: a resolução dinâmica row.CommandLine
                    // falhou com E_FAIL no auxiliar, apesar de a consulta ter retornado.
                    properties = row.Properties_;
                    property = ((dynamic)properties).Item("CommandLine", 0);
                    string command = ((dynamic)property).Value ?? "";
                    return command.Replace("\"", "").Contains("--user-data-dir=" + Path.Combine(directory, "desktop-browser"), StringComparison.OrdinalIgnoreCase);
                }
                finally { if (property != null) Marshal.ReleaseComObject(property); if (properties != null) Marshal.ReleaseComObject(properties); Marshal.ReleaseComObject(row); }
            }
        } catch (Exception e) { ownershipError = stage + ":" + e.GetType().Name + ":" + e.HResult; return false; }
        finally { if (rows != null) Marshal.ReleaseComObject(rows); if (service != null) Marshal.ReleaseComObject(service); }
        return false;
    }
    List<nint> OwnedWindows() {

        var edgePids = Process.GetProcessesByName("msedge").Select(p => { using (p) return p.Id; }).ToHashSet();
        var candidates = new List<(nint Handle, int Pid)>();

        EnumWindows((h, _) => {
            GetWindowThreadProcessId(h, out var pid); if (!edgePids.Contains((int)pid)) return true;
            var title = new StringBuilder(256); GetWindowText(h, title, title.Capacity);
            if (title.ToString().StartsWith("LazyGaming")) candidates.Add((h, (int)pid)); return true; }, 0);

        var ownedPids = candidates.Select(c => c.Pid).Distinct().Where(pid => pid == process?.Id || OwnsProcess(pid)).ToHashSet();
        windowDiscovery = new { edgeProcesses = edgePids.Count, candidates = candidates.Count, ownedProcesses = ownedPids.Count };

        return candidates.Where(c => ownedPids.Contains(c.Pid)).Select(c => c.Handle).ToList();
    }
    async Task ToggleCore()
    {

        if (Visible) { await HideCore(); return; }
        previous = GetForegroundWindow();
        if (window != 0 && IsWindow(window)) { await Present(); return; }
        // Limpa somente janelas antigas do perfil próprio; nenhuma janela do navegador pessoal.
        foreach (var stale in OwnedWindows()) PostMessage(stale, 0x10, 0, 0);
        await Task.Delay(250); window = 0;
        lock (gate) { token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)); tokenExpiry = DateTime.UtcNow.AddMinutes(1); }
        if (Environment.GetEnvironmentVariable("LAZYGAMING_DESKTOP_ENGINE") != "edge") {
            native?.Dispose();
            native = new NativeDesktop(directory, new Uri($"http://127.0.0.1:{port}"), gameWindow ?? (() => 0));
            try { window = await native.Open($"http://127.0.0.1:{port}/#desktop={token}"); await Present(); return; }
            catch { native.Dispose(); native = null; window = 0; throw; }
        }
        var edge = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe");
        if (!File.Exists(edge)) throw new InvalidOperationException("Microsoft Edge não encontrado para abrir o launcher.");
        var start = new ProcessStartInfo(edge) { UseShellExecute = false };
        start.ArgumentList.Add("--user-data-dir=" + Path.Combine(directory, "desktop-browser"));
        start.ArgumentList.Add("--no-first-run"); start.ArgumentList.Add("--disable-background-mode");
        // Fullscreen do próprio Edge remove também a barra desenhada no cliente,
        // que não desaparece apenas com a remoção dos estilos Win32 de título.
        start.ArgumentList.Add("--kiosk"); start.ArgumentList.Add($"http://127.0.0.1:{port}/#desktop={token}");
        start.ArgumentList.Add("--edge-kiosk-type=fullscreen");
        // Edge pode reutilizar o processo do perfil; PID de Process.Start sozinho não identifica sua janela.
        process = Process.Start(start);
        for (int i = 0; i < 80; i++)
        {
            await Task.Delay(100);
            window = OwnedWindows().FirstOrDefault(IsWindowVisible);
            if (window != 0 && IsWindow(window)) { await Present(); return; }

        }
        throw new InvalidOperationException("A janela do launcher ainda não ficou disponível.");
    }
    async Task Present()
    {
        openingWatch?.Cancel();
        var watch = openingWatch = new CancellationTokenSource();
        GetWindowThreadProcessId(previous, out uint previousPid);
        opening = true; ResetInput?.Invoke();
        try {
            if (native == null) { ShowWindowAsync(window, 9); await Task.Delay(150); }
            using (WindowFocus.PhysicalCoordinates()) {
            // Apenas a própria janela acompanha a tela principal atual (TV durante a sessão).
            var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            try { if (Displays.TvReady(false)) bounds = Displays.TvBounds(); } catch (InvalidOperationException) { }
            var style = GetWindowLongPtr(window, -16).ToInt64();
            SetWindowLongPtr(window, -16, (nint)(style & ~0x01C40000L));
            SetWindowPos(window, -1, bounds.X, bounds.Y, bounds.Width, bounds.Height, native == null ? 0x4060u : 0x4020u);
            }
            if (native != null) await native.Show();
            WindowFocus.ActivateInputWindow(window, true);
            if (native != null) await native.FocusInput();
            // O marco de entrada pertence à janela já apresentada. Criar/mostrar
            // o WebView pode atualizar a entrada antes desta entrega inicial.
            var guard = new LauncherOpeningFocus(Environment.TickCount64, WindowFocus.InputStamp());
            while (true) {
                var action = await CheckOpeningFocus(guard, previousPid, watch.Token);
                if (action == LauncherFocusAction.Cancel) { if (!Focused) await HideUnfocused(); return; }
                if (action == LauncherFocusAction.Fail) break;
                if (guard.Ready || action == LauncherFocusAction.Finish) {
                    _ = WatchOpeningFocus(guard, previousPid, watch.Token);
                    return;
                }
                await Task.Delay(50);
            }
            // Não deixar um painel aparentemente navegável com o jogo em foreground.
            if (native != null) await native.Hide(); else ShowWindowAsync(window, 0);
            throw new InvalidOperationException("Não foi possível focar o painel. Tente abri-lo novamente.");
        } finally { opening = false; }
    }
    async Task HideUnfocused() {
        ResetInput?.Invoke();
        if (native != null) await native.Hide(); else ShowWindowAsync(window, 0);
    }
    async Task<LauncherFocusAction> CheckOpeningFocus(LauncherOpeningFocus guard, uint previousPid, CancellationToken cancel) {
        cancel.ThrowIfCancellationRequested();
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out uint foregroundPid);
        bool related = foreground == window || foreground == 0 || previousPid != 0 && foregroundPid == previousPid;
        var action = guard.Update(Environment.TickCount64, Focused, WindowFocus.InputStamp(), related);
        if (action == LauncherFocusAction.Repair) {
            ResetInput?.Invoke();
            WindowFocus.ActivateInputWindow(window, true);
            if (native != null) await native.FocusInput();
        }
        var phase = action.ToString();
        var status = new { phase, focused = Focused, repairs = guard.Repairs, relatedForeground = related, inputChanged = guard.InputChanged };
        openingStatus = status;
        var diagnostic = System.Text.Json.JsonSerializer.Serialize(status);
        try { if (diagnostic != lastOpeningDiagnostic) { File.WriteAllText(Path.Combine(directory, "launcher-opening.json"), diagnostic); lastOpeningDiagnostic = diagnostic; } }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return action;
    }
    async Task WatchOpeningFocus(LauncherOpeningFocus guard, uint previousPid, CancellationToken cancel) {
        try {
            while (true) {
                await Task.Delay(100, cancel);
                if (!await toggleGate.WaitAsync(0, cancel)) continue;
                try {
                    if (!Visible) return;
                    var action = await CheckOpeningFocus(guard, previousPid, cancel);
                    if (action == LauncherFocusAction.Cancel) { if (!Focused) await HideUnfocused(); return; }
                    if (action == LauncherFocusAction.Finish) return;
                    if (action == LauncherFocusAction.Fail) {
                        await HideUnfocused();
                        FocusIssue?.Invoke("O painel não manteve o foco. Tente abri-lo novamente."); return;
                    }
                } finally { toggleGate.Release(); }
            }
        } catch (OperationCanceledException) { }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) {
            try {
                await toggleGate.WaitAsync(cancel);
                try {
                    cancel.ThrowIfCancellationRequested();
                    if (Visible) await HideUnfocused();
                    FocusIssue?.Invoke("Não foi possível confirmar o foco do painel.");
                } finally { toggleGate.Release(); }
            } catch (OperationCanceledException) { }
            catch (InvalidOperationException) { }
        }
    }
    public void AlignToTv() {
        using var dpi = WindowFocus.PhysicalCoordinates();
        if (!Visible) return;
        var bounds = Displays.TvBounds();
        if (native != null) {
            // Form/WebView devem atualizar seu layout na própria thread. Um
            // SetWindowPos assíncrono externo pode ser sobrescrito por DPI/layout.
            for (int attempt = 0; attempt < 4; attempt++) {
                native.Align(bounds).GetAwaiter().GetResult();
                if (WindowFocus.MatchesBounds(window, bounds)) return;
                Thread.Sleep(100);
            }
            File.WriteAllText(Path.Combine(directory, "launcher-placement.json"), System.Text.Json.JsonSerializer.Serialize(new { expected = bounds, actual = WindowFocus.Inspect(window), composition = native.Status }));
            throw new InvalidOperationException("O launcher não confirmou sua posição na TV; a troca foi recusada.");
        }
        var style = GetWindowLongPtr(window, -16).ToInt64();
        SetWindowLongPtr(window, -16, (nint)(style & ~0x01C40000L));
        SetWindowPos(window, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x4034);
        for (int i = 0; i < 20; i++) {
            if (WindowFocus.MatchesBounds(window, bounds)) return;
            Thread.Sleep(25);
        }
        throw new InvalidOperationException("O launcher não confirmou sua posição na TV; a troca foi recusada.");
    }
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    public void Close() { openingWatch?.Cancel(); native?.Dispose(); foreach (var owned in OwnedWindows()) PostMessage(owned, 0x10, 0, 0); }
    [DllImport("user32.dll")] static extern bool PostMessage(nint window, uint message, nint w, nint l);
    public async Task Hide(bool waitForController = true) {
        await toggleGate.WaitAsync();
        try { await HideCore(waitForController); } finally { toggleGate.Release(); }
    }
    async Task HideCore(bool waitForController = true) {
        openingWatch?.Cancel();
        if (window == 0 || !IsWindow(window) || !Visible) return;
        closing = true; ResetInput?.Invoke();
        try {
            if (waitForController) await WaitForControllerRelease();
            if (native != null) await native.Hide(); else ShowWindowAsync(window, 0);
            for (int i = 0; i < 20 && Visible; i++) await Task.Delay(25);
            if (Visible) throw new InvalidOperationException("Não foi possível ocultar o painel. Tente novamente.");
            var game = gameWindow?.Invoke() ?? 0;
            if (game != 0) WindowFocus.ActivateGame(game);
            else if (previous != 0 && IsWindow(previous)) WindowFocus.Activate(previous);
        } finally { closing = false; }
    }
    public async Task WaitForControllerRelease() {
        var deadline = Environment.TickCount64 + 3000;
        while (ControllerReleased?.Invoke() == false) {
            if (Environment.TickCount64 >= deadline) throw new InvalidOperationException("Solte os botões e os analógicos para voltar ao jogo.");
            await Task.Delay(20);
        }
    }
    delegate bool WindowCallback(nint window, nint state);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, nint state);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(nint window, StringBuilder text, int count);
    [DllImport("user32.dll")] static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
}

public static class WindowFocus
{
    public static int DpiAwareness(nint window) => GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(window));
    public static uint WindowDpi(nint window) => GetDpiForWindow(window);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] static extern nint GetWindowDpiAwarenessContext(nint window);
    [DllImport("user32.dll")] static extern int GetAwarenessFromDpiAwarenessContext(nint context);
    public static bool IsForeground(nint window) => window != 0 && GetForegroundWindow() == window;
    public static bool CanPresent(nint window) => window != 0 && IsWindowVisible(window) && !IsIconic(window);
    public static bool BelongsTo(nint window, int pid) {
        if (window == 0) return false;
        GetWindowThreadProcessId(window, out uint owner);
        return owner == pid;
    }
    public static void RestoreForPlacement(nint window) {
        if (IsIconic(window)) ShowWindowAsync(window, 4); // SW_SHOWNOACTIVATE: conserva o menu.
    }
    public static bool PlaceOnTv(nint window) {
        // Não redimensionar uma janela ainda minimizada; aguardar a restauração
        // assíncrona antes de conferir retângulo/foco.
        if (window == 0 || !IsWindowVisible(window) || IsIconic(window)) return false;
        using var dpi = PhysicalCoordinates();
        if (!Displays.TvReady(false)) throw new InvalidOperationException("A TV de destino não está disponível.");
        var bounds = Displays.TvBounds();
        if (MatchesBounds(window, bounds)) return true;
        // Apresentação comum a todos os jogos: janela sem bordas no destino físico.
        // Não injeta no renderizador nem troca o monitor principal do Windows.
        long style = ReadWindowStyle(window).ToInt64() & 0xFFFFFFFFL;
        long borderless = (style & ~0x00CF0000L & ~0x01000000L) | 0x80000000L;
        if (style != borderless) WriteWindowStyle(window, new nint(borderless));
        // Somente a janela do jogo verificado; não ativa foco nem muda a topologia.
        SetWindowPos(window, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x4034);
        return MatchesBounds(window, bounds);
    }
    static nint ReadWindowStyle(nint window) => nint.Size == 8 ? GetWindowLongPtr(window, -16) : new nint(GetWindowLong(window, -16));
    static void WriteWindowStyle(nint window, nint value) {
        if (nint.Size == 8) SetWindowLongPtr(window, -16, value);
        else SetWindowLong(window, -16, value.ToInt32());
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong(nint window, int index, int value);
    public static bool MatchesBounds(nint window, System.Drawing.Rectangle bounds) {
        using var dpi = PhysicalCoordinates();
        return GetWindowRect(window, out var rect) && rect.Left == bounds.Left && rect.Top == bounds.Top && rect.Right == bounds.Right && rect.Bottom == bounds.Bottom;
    }
    public static IDisposable PhysicalCoordinates() => new DpiContext();
    sealed class DpiContext : IDisposable {
        readonly nint previous = SetThreadDpiAwarenessContext(-4);
        public void Dispose() { if (previous != 0) SetThreadDpiAwarenessContext(previous); }
    }
    [DllImport("user32.dll")] static extern nint SetThreadDpiAwarenessContext(nint value);
    public static object Inspect(nint window) {
        using var dpi = PhysicalCoordinates();
        GetWindowRect(window, out var rect);
        var foreground = GetForegroundWindow(); GetWindowThreadProcessId(foreground, out var foregroundPid);
        return new { window = (long)window, foreground = foreground == window, inputFocused = HasInputFocus(window), visible = IsWindowVisible(window), minimized = IsIconic(window), foregroundPid, left = rect.Left, top = rect.Top, width = rect.Right - rect.Left, height = rect.Bottom - rect.Top };
    }
    public static GamePlacement KeepGamePlacement(Func<nint>? gameWindow) => new GamePlacement(gameWindow);
    public sealed class GamePlacement : IDisposable {
        readonly Func<nint>? query; readonly nint window; readonly Rect bounds;
        public GamePlacement(Func<nint>? query) {
            using var dpi = PhysicalCoordinates();
            this.query = query; window = query?.Invoke() ?? 0;
            if (window != 0 && !GetWindowRect(window, out bounds)) throw new InvalidOperationException("Não foi possível guardar a posição do jogo antes da troca.");
        }
        public void Dispose() {
            try { Restore(); } catch { /* A transação já informa falha; Dispose não mascara recuperação. */ }
        }
        public void Restore() {
            using var dpi = PhysicalCoordinates();
            if (window == 0 || query?.Invoke() != window) return;
            if (!Displays.TvReady(false)) return;
            if (GetWindowRect(window, out var current) && current.Equals(bounds)) return;
            SetWindowPos(window, 0, bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, 0x4014);
            for (int i = 0; i < 20; i++) {
                if (GetWindowRect(window, out current) && current.Equals(bounds)) return;
                Thread.Sleep(25);
            }
            throw new InvalidOperationException("O jogo não confirmou a posição anterior na TV; a troca foi recusada.");
        }
    }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    public static IDisposable KeepAcrossDisplayChange() => new DisplayFocus();
    sealed class DisplayFocus : IDisposable {
        readonly nint previous = GetForegroundWindow();
        readonly uint input = LastInput();
        public void Dispose() {
            // Não roubar foco se houve entrada do usuário durante a mudança.
            if (previous != 0 && input != uint.MaxValue && GetForegroundWindow() != previous && LastInput() == input) Activate(previous);
        }
    }
    [StructLayout(LayoutKind.Sequential)] struct LastInputInfo { public uint Size, Time; }
    static uint LastInput() { var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() }; return GetLastInputInfo(ref info) ? info.Time : uint.MaxValue; }
    public static uint InputStamp() => LastInput();
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInputInfo info);
    public static bool Activate(nint window)
    {
        if (window == 0) return false;
        PeekMessage(out _, 0, 0, 0, 0); // Criar fila antes de qualquer AttachThreadInput, inclusive no fallback.
        // Não unir filas de entrada: isso pode bloquear ShowWindow ao alternar Edge.
        if (IsIconic(window)) ShowWindowAsync(window, 9);
        BringWindowToTop(window); SetForegroundWindow(window);
        if (GetForegroundWindow() != window) {
            var current = GetCurrentThreadId(); var other = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            bool joined = other != 0 && other != current && AttachThreadInput(current, other, true);
            try { SetForegroundWindow(window); } finally { if (joined) AttachThreadInput(current, other, false); }
        }
        return GetForegroundWindow() == window;
    }
    public static bool ActivateGame(nint window) => ActivateInputWindow(window);
    public static bool ActivateInputWindow(nint window, bool panel = false) {
        if (window == 0) return false;
        if (IsIconic(window)) {
            ShowWindowAsync(window, 9);
            // Espera curta fora de AttachThreadInput; não bloquear a thread da janela.
            for (int attempt = 0; attempt < 10 && IsIconic(window); attempt++) Thread.Sleep(25);
            if (IsIconic(window)) return false;
        }
        return InputFocusHandoff.Try(
            () => InputFocusHandoff.Ready(IsWindowVisible(window), IsIconic(window), GetForegroundWindow() == window, HasInputFocus(window)),
            () => Activate(window),
            () => new FocusInputQueues(window),
            () => { SetForegroundWindow(window); SetActiveWindow(window); SetFocus(window); });
    }
    sealed class FocusInputQueues : IDisposable {
        readonly uint caller, target, foreground;
        readonly bool joinedForeground, joinedTarget;
        public FocusInputQueues(nint window) {
            PeekMessage(out _, 0, 0, 0, 0);
            caller = GetCurrentThreadId(); target = GetWindowThreadProcessId(window, out _);
            foreground = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            // Conectar foreground e destino também para jogos: o primeiro
            // SetForegroundWindow pode ser recusado ao iniciar pela TV.
            // Nenhum clique/tecla ou ShowWindow síncrono durante a conexão.
            joinedForeground = foreground != 0 && foreground != caller && foreground != target && AttachThreadInput(caller, foreground, true);
            joinedTarget = target != 0 && target != caller && AttachThreadInput(caller, target, true);
        }
        public void Dispose() {
            if (joinedTarget) AttachThreadInput(caller, target, false);
            if (joinedForeground) AttachThreadInput(caller, foreground, false);
        }
    }
    public static bool HasInputFocus(nint window) {
        if (window == 0) return false;
        var thread = GetWindowThreadProcessId(window, out _);
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        return thread != 0 && GetGUIThreadInfo(thread, ref info) && info.Active == window &&
            (info.Focus == window || info.Focus != 0 && IsChild(window, info.Focus));
    }
    [StructLayout(LayoutKind.Sequential)] struct GuiThreadInfo {
        public uint Size, Flags; public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret; public Rect CaretRect;
    }
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")] static extern nint SetActiveWindow(nint window);
    [DllImport("user32.dll")] static extern nint SetFocus(nint window);
    [DllImport("user32.dll")] static extern bool IsChild(nint parent, nint child);
    [StructLayout(LayoutKind.Sequential)] struct NativeMessage { public nint Window; public uint Message; public nuint W; public nint L; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")] static extern bool PeekMessage(out NativeMessage message, nint window, uint first, uint last, uint remove);
    public static nint GameWindow(GameLibrary library)
    {
        foreach (var game in library.Games)
            if (GameWindow(game) is var window && window != 0) return window;
        return 0;
    }
    public static nint GameWindow(Game game)
    {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(game.ProcessPath)))
                using (process) { if (string.Equals(ProcessImage.PathOf(process.Id), game.ProcessPath, StringComparison.OrdinalIgnoreCase) &&
                    LaunchChoices.Contents(game).Any(content => content == null || WindowsGames.RuntimeMatches(process.Id, content)) && process.MainWindowHandle != 0) return process.MainWindowHandle; }
        return 0;
    }
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(nint window);
    [DllImport("user32.dll")] static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(nint window, int command);
}
public sealed class ControllerKeys
{
    readonly System.Threading.Channels.Channel<(int Key, bool Repeat)> keys = System.Threading.Channels.Channel.CreateBounded<(int Key, bool Repeat)>(new System.Threading.Channels.BoundedChannelOptions(32) { FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest });
    long generated, delivered, lastPoll;
    public object Status => new { generated = Interlocked.Read(ref generated), delivered = Interlocked.Read(ref delivered), lastPoll = Interlocked.Read(ref lastPoll) };
    public void Push(int key, bool repeat = false) { Interlocked.Increment(ref generated); keys.Writer.TryWrite((key, repeat)); }
    public void Clear() { while (keys.Reader.TryRead(out _)) { } }
    public async Task<object> Take(CancellationToken cancel) {
        Interlocked.Exchange(ref lastPoll, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel); timeout.CancelAfter(20000);
        try { var input = await keys.Reader.ReadAsync(timeout.Token); Interlocked.Increment(ref delivered); return new { key = input.Key, repeat = input.Repeat }; } catch (OperationCanceledException) { return new { key = 0, repeat = false }; }
    }
}
