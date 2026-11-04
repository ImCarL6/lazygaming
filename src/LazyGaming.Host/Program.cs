using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using LazyGaming;

// App Windows sem janela de console (atalhos/instalador). Comandos de diagnóstico rodados num terminal
// continuam imprimindo nele; saída redirecionada (scripts/testes) já funciona sem isto.
HostConsole.AttachToParentTerminal();

if (args.Contains("--package-version")) {
    Console.WriteLine(typeof(PcPreferences).Assembly.GetName().Version!.ToString(3)); return;
}
if (args.Contains("--check-dependencies")) {
    Console.WriteLine(JsonSerializer.Serialize(RuntimeDependencies.Inspect(),new JsonSerializerOptions(JsonSerializerDefaults.Web))); return;
}

// Deve preceder a primeira janela de feedback/WebView, inclusive em outras
// threads. Evita virtualização de DPI ao levar o painel do monitor para a TV.
System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);

if (args.Contains("--open-launcher") || args.Contains("--open-hub"))
{
    bool openHub = args.Contains("--open-hub");
    try {
        var localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LazyGaming");
        var saved = Path.Combine(localData, "pc-command.json");
        if (!File.Exists(saved)) saved = Path.Combine(localData,"pairing.json"); // Existing helper migration.
        using var stored = JsonDocument.Parse(File.ReadAllText(saved));
        var key = Convert.FromHexString(stored.RootElement.GetProperty("Key").GetString()!);
        var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var nonce = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        using var client = new HttpClient(new HttpClientHandler { UseProxy=false }) { Timeout = TimeSpan.FromSeconds(8) };
        var route = openHub ? "/api/hub/open" : "/api/launcher/toggle";
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:47831" + route);
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        request.Headers.Add("X-LG-Time", time); request.Headers.Add("X-LG-Nonce", nonce);
        request.Headers.Add("X-LG-Signature", Security.Sign($"POST\n{route}\n{time}\n{nonce}\n{{}}", key));
        using var response = await client.SendAsync(request);
        if (response.IsSuccessStatusCode) {
            if (openHub) {
                using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(result.RootElement.GetProperty("url").GetString()!) { UseShellExecute = true });
            }
            return;
        }
        // Auxiliar vivo recusou a operação: não iniciar uma segunda instância
        // para tentar escapar da recusa de foco/autenticação.
        Console.Error.WriteLine("O auxiliar não conseguiu abrir o painel. Tente novamente.");
        Environment.ExitCode = 1; return;
    } catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException or JsonException or FormatException or UnauthorizedAccessException) { }
    // Uma tentativa que expirou pode continuar no auxiliar; nunca duplicá-lo.
    if (!HostStartup.CanStart(47831)) using (var probe = new HttpClient(new HttpClientHandler { UseProxy=false }) { Timeout = TimeSpan.FromSeconds(2) }) {
        try {
            using var alive = await probe.GetAsync("http://127.0.0.1:47831/api/discover");
            Console.Error.WriteLine("O auxiliar já está aberto, mas não confirmou a abertura do painel. Tente novamente.");
            Environment.ExitCode = 1; return;
        } catch (HttpRequestException) { }
        catch (TaskCanceledException) {
            Console.Error.WriteLine("O auxiliar não respondeu. Aguarde antes de tentar novamente.");
            Environment.ExitCode = 1; return;
        }
    }
    args = args.Concat([openHub ? "--hub" : "--launcher"]).ToArray();
}
if (args.Contains("--preview-feedback-on") || args.Contains("--preview-feedback-off"))
{
    using var sample = new ControllerFeedback(true); await Task.Delay(400);
    sample.Notify(args.Contains("--preview-feedback-on"), 30000); await Task.Delay(31000); return;
}
if (Array.IndexOf(args, "--replace-local-game") is var replaceIndex && replaceIndex >= 0) {
    if (replaceIndex + 2 >= args.Length) throw new InvalidOperationException("Informe o jogo local existente e a nova ficha.");
    var importDirectory = Environment.GetEnvironmentVariable("LAZYGAMING_IMPORT_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LazyGaming");
    Console.WriteLine(new LocalGameStore(importDirectory).Import(args[replaceIndex + 2], args[replaceIndex + 1])); return;
}
if (Array.IndexOf(args, "--import-local-game") is var importIndex && importIndex >= 0) {
    if (importIndex + 1 >= args.Length) throw new InvalidOperationException("Informe o arquivo da ficha local.");
    var importDirectory = Environment.GetEnvironmentVariable("LAZYGAMING_IMPORT_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LazyGaming");
    Console.WriteLine(new LocalGameStore(importDirectory).Import(args[importIndex + 1])); return;
}
if (args.Contains("--inspect-library")) { Console.WriteLine(JsonSerializer.Serialize(new GameLibrary(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LazyGaming")).Inspect())); return; }
if (args.Contains("--verify-catalog")) {
    var catalogDirectory = Path.Combine(AppContext.BaseDirectory, "catalog-validation");
    var catalogLibrary = new GameLibrary(catalogDirectory);
    await catalogLibrary.Metadata.EnsureAll(catalogLibrary.Games, true);
    await new ArtworkStore(catalogLibrary).EnsureAll();
    Console.WriteLine(JsonSerializer.Serialize(catalogLibrary.Inspect())); return;
}
if (args.Contains("--inspect-desktop-windows")) { using var windows = DesktopWindows.Capture(); Console.WriteLine(JsonSerializer.Serialize(windows.Inspect())); return; }
if (args.Contains("--inspect-launcher-windows")) { Console.WriteLine(JsonSerializer.Serialize(await Task.Run(() => new DesktopLauncher(47831, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LazyGaming")).InspectWindows()))); return; }
if (args.Contains("--verify-game-focus")) {
    var focusLibrary = new GameLibrary(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LazyGaming"));
    var window = WindowFocus.GameWindow(focusLibrary);
    if (window == 0) throw new InvalidOperationException("Nenhuma janela de jogo identificada.");
    Console.WriteLine("BEFORE: " + JsonSerializer.Serialize(WindowFocus.Inspect(window)));
    var focused = await Task.Run(() => WindowFocus.ActivateGame(window));
    await Task.Delay(500);
    Console.WriteLine("AFTER: " + JsonSerializer.Serialize(new { focused, state = WindowFocus.Inspect(window) })); return;
}
if (args.Contains("--inspect-game-window")) {
    var inspectLibrary = new GameLibrary(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LazyGaming"));
    Console.WriteLine(JsonSerializer.Serialize(WindowFocus.Inspect(WindowFocus.GameWindow(inspectLibrary)))); return;
}
if (args.Contains("--inspect-game-background")) {
    var inspectLibrary = new GameLibrary(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LazyGaming"));
    var baseline = args.Contains("--background-baseline");
    var benchmark = args.Contains("--benchmark");
    using var capture = new GameBackground(() => WindowFocus.GameWindow(inspectLibrary), () => true, baseline ? 1280 : 960, baseline ? .9f : .55f);
    var hashes = new HashSet<string>(); int received = 0;
    var watch = System.Diagnostics.Stopwatch.StartNew();
    for (int i = 0; i < (benchmark ? 100 : 20) && received < (benchmark ? 40 : 3); i++) {
        var frameStart = watch.Elapsed.TotalMilliseconds;
        var frame = await capture.Frame();
        if (frame != null) { received++; hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(frame))); }
        await Task.Delay(benchmark ? baseline ? 100 : (int)Math.Max(0, 42 - (watch.Elapsed.TotalMilliseconds - frameStart)) : 150);
    }
    Console.WriteLine(JsonSerializer.Serialize(new { received, distinctFrames = hashes.Count, seconds = Math.Round(watch.Elapsed.TotalSeconds, 2), status = capture.Status })); return;
}
if (args.Contains("--cache-art"))
{
    var lib = new GameLibrary(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LazyGaming"));
    var art = new ArtworkStore(lib); await art.EnsureAll();
    Console.WriteLine(JsonSerializer.Serialize(lib.Games.Select(g => new { g.Name, cover = art.FileFor(g.ArtworkId!, "cover") != null, backdrop = art.FileFor(g.ArtworkId!, "backdrop") != null })));
    return;
}
if (args.Contains("--inspect-dualsense")) { await TouchpadMouse.Inspect(); return; }
if (args.Contains("--inspect-displays")) { Console.WriteLine(JsonSerializer.Serialize(Displays.List())); return; }
if(args.Contains("--inspect-launch-display")) {
    var store=new PcPreferenceStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LazyGaming"));
    Displays.Preferences=()=>store.Current;Console.WriteLine(JsonSerializer.Serialize(LaunchDisplay.Current()));return;
}
if (args.Contains("--verify-displays") || args.Contains("--verify-extended-displays"))
{
    bool extended = args.Contains("--verify-extended-displays");
    if (extended) {
        var verificationData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LazyGaming");
        var verificationLibrary = new GameLibrary(verificationData);
        if (new WindowsGames(verificationLibrary, verificationData).Processes().Count != 0 || File.Exists(Path.Combine(verificationData, "session.json")))
            throw new InvalidOperationException("Encerre o jogo e a sessão antes de verificar telas.");
        if (Displays.Tv().Active) throw new InvalidOperationException("Esta verificação exige TV inicialmente desativada.");
    }
    var original = Displays.Capture();
    var recovery = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LazyGaming", "display-verification.json");
    Displays.Save(original, recovery);
    using var verificationWindows = DesktopWindows.Capture();
    try {
        if (extended) {
            Displays.PrepareDesktop(original); await Task.Delay(1500);
            var current = Displays.Capture();
            var pcPaths = current.Paths.Where(p => original.Paths.Any(o => o.Target.Id == p.Target.Id && o.Target.Adapter.Low == p.Target.Adapter.Low && o.Target.Adapter.High == p.Target.Adapter.High)).ToArray();
            var expected = original.Modes.ToArray();
            var firstBefore = original.Modes[original.Paths[0].Source.ModeIndex].Source.Position;
            var firstAfter = current.Modes[pcPaths[0].Source.ModeIndex].Source.Position;
            foreach (var p in original.Paths) {
                expected[p.Source.ModeIndex].Source.Position.X += firstAfter.X - firstBefore.X;
                expected[p.Source.ModeIndex].Source.Position.Y += firstAfter.Y - firstBefore.Y;
            }
            if (!Displays.TvReady() || !Displays.SameLayout(new(original.Paths, expected), new(pcPaths, current.Modes)))
                throw new InvalidOperationException("A ativação não preservou modos e organização relativa do desktop.");
            Console.WriteLine("TV_PRIMARY_4K60_PC_RELATIVE_LAYOUT_PRESERVED");
            var apps = verificationWindows.Verify();
            Console.WriteLine("APP_WINDOWS_DURING_SESSION: " + JsonSerializer.Serialize(apps));
            if (apps.Unconfirmed != 0) Console.WriteLine("APP_WINDOWS_DETAILS: " + JsonSerializer.Serialize(verificationWindows.Diagnostics));
            if (apps.Unconfirmed != 0) throw new InvalidOperationException("Há janelas cuja preservação não foi confirmada.");
        } else { Displays.PrepareTv(); Console.WriteLine("TV_ONLY_NATIVE_CONFIRMED: " + JsonSerializer.Serialize(Displays.List().Where(d => d.Active))); }
    }
    finally {
        Displays.Restore(original); verificationWindows.Restore(); await Task.Delay(700);
        Console.WriteLine("APP_WINDOWS_FINAL: " + JsonSerializer.Serialize(verificationWindows.Verify()));
    }
    if (extended) {
        if (!Displays.SameLayout(Displays.Capture(), original) || Displays.Tv().Active) throw new InvalidOperationException("Restauração da TV inicialmente desativada não foi confirmada.");
        Console.WriteLine("ORIGINAL_PC_LAYOUT_RESTORED_TV_INACTIVE");
        var apps = verificationWindows.Verify();
        Console.WriteLine("APP_WINDOWS_AFTER_RESTORE: " + JsonSerializer.Serialize(apps));
        if (apps.Unconfirmed != 0) Console.WriteLine("APP_WINDOWS_RESTORE_DETAILS: " + JsonSerializer.Serialize(verificationWindows.Diagnostics));
        if (apps.Unconfirmed != 0) throw new InvalidOperationException("Há janelas cuja restauração não foi confirmada.");
        return;
    }
    Console.WriteLine("RESTORED: " + JsonSerializer.Serialize(Displays.List().Where(d => d.Active))); return;
}
if (args.Contains("--inspect-audio")) { Console.WriteLine(JsonSerializer.Serialize(Audio.List())); return; }
if (args.Contains("--inspect-volume")) { Console.WriteLine(JsonSerializer.Serialize(Audio.Volume())); return; }
if (args.Contains("--verify-audio")) { Console.WriteLine(Audio.VerifyRoundTrip()); return; }
// Diagnóstico somente leitura da instalação na TV: descobre, confere condições e versão instalada; nunca instala.
if (args.Contains("--tv-check")) {
    HostConsole.AttachToParentTerminal();
    var local = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address).Where(PrivateNetwork.IsLocal).Distinct().ToArray();
    var toolsArg = Array.IndexOf(args, "--tizen") is var ti and >= 0 && ti + 1 < args.Length ? args[ti + 1] : null;
    if (args.Contains("--search-tools")) { toolsArg ??= TvInstaller.Search(TvInstaller.SearchStarts(), 9, TimeSpan.FromSeconds(25)); Console.WriteLine("tools: " + (toolsArg ?? "(não encontradas)")); }
    if (Array.IndexOf(args, "--verify-installer") is var vi and >= 0 && vi + 1 < args.Length) { Console.WriteLine("assinatura Samsung válida: " + Authenticode.SignedBySamsung(args[vi + 1])); return; }
    using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
    var checker = new TvInstaller(Path.Combine(AppContext.BaseDirectory, "wwwroot"), Path.GetTempPath(), () => TvInstaller.Locate(TvInstaller.DefaultRoots(toolsArg)),
        new ProcessToolRunner(), () => local.Select(a => a.ToString()).ToArray(), (ip, c) => TvInstaller.Probe(http, ip, c), () => TvInstaller.Subnets(local));
    if (Array.IndexOf(args, "--package-to") is var pi and >= 0 && pi + 1 < args.Length) {
        // Só empacota/assina localmente, pelo mesmo caminho da instalação automática.
        var tools = TvInstaller.Locate(TvInstaller.DefaultRoots(toolsArg)) ?? throw new InvalidOperationException("Ferramentas Tizen não encontradas.");
        var target = Path.GetFullPath(args[pi + 1]);
        Console.WriteLine(JsonSerializer.Serialize(await checker.Package(tools, Path.Combine(target, "stage"), target, default)));
        return;
    }
    Console.WriteLine(JsonSerializer.Serialize(await checker.Check(false, false, null, default), new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    return;
}
// Reinício do auxiliar pedido pelo próprio app: aguardar a instância anterior liberar porta e perfil.
if (Array.IndexOf(args, "--wait-pid") is var waitAt and >= 0 && waitAt + 1 < args.Length && int.TryParse(args[waitAt + 1], out var waitPid)) {
    try { using var previous = System.Diagnostics.Process.GetProcessById(waitPid); if (previous.ProcessName.Equals("LazyGaming.Host", StringComparison.OrdinalIgnoreCase)) previous.WaitForExit(20000); }
    catch (ArgumentException) { }
}
var preview = args.Contains("--preview-library");
var test = args.Contains("--test") || preview;
int port = int.TryParse(Environment.GetEnvironmentVariable("LAZYGAMING_PORT"), out var requested) ? requested : 47831;
var data = test ? Path.Combine(Path.GetTempPath(), "lazygaming-test-" + port) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LazyGaming");
using var instanceLease = HostInstanceLease.Acquire(data);
using var diagnostics = new RuntimeDiagnostics(data);
diagnostics.Attach();
try {
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders(); // Não registrar códigos, chaves, corpos ou URLs autenticadas.
var pcPreferences = new PcPreferenceStore(data);
var lan = (args.Contains("--lan") || pcPreferences.Current.TvIntegrationEnabled) && !test;
var addresses = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
    .SelectMany(n => n.GetIPProperties().UnicastAddresses).Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
    .ToArray();
var lanAddresses = addresses.Where(a => PrivateNetwork.IsLocal(a.Address)).DistinctBy(a => a.Address).ToArray();
builder.WebHost.ConfigureKestrel(k => { k.Limits.MaxRequestBodySize = 8192; k.Listen(IPAddress.Loopback, port);
    if (lan) foreach (var a in lanAddresses) k.Listen(a.Address, port); });
var security = new Security(data);
var localCommands = new LocalCommands(data);
Displays.Preferences = () => pcPreferences.Current;
var hubSession = new HubSession();
var reopenAvailable = !test && DesktopReopen.Register();
var pairingRecovery = new PairingRecovery(data);
var library = new GameLibrary(data, test && !preview);
var artwork = new ArtworkStore(library);
IGamePlatform platform = test ? new SimulatedGames() : new WindowsGames(library, data);
var desktop = new DesktopLauncher(port, data, () => test ? 0 : WindowFocus.GameWindow(library));
if (platform is WindowsGames windowsGames) windowsGames.MenuVisible = () => desktop.Visible;
IGameEnvironment environment = new TransportEnvironment(test ? new SimulatedEnvironment() : new TvSession(data, new NativeSessionPlatform(() => WindowFocus.GameWindow(library), desktop.AlignToTv)), data);
IStreamHost streamHost = new SunshineHost(data);
var streamControls = new StreamControls(data);
long streamClientReport = 0;
async Task<StreamAvailability> StreamStatus() {
    var host = await streamHost.Inspect();
    if (!host.Available) return host;
    // Heartbeat autenticado da própria TV renova presença após reinício do
    // auxiliar. Cookie/loopback do PC não pode enviar /stream/report.
    return streamControls.Online || Environment.TickCount64 - Interlocked.Read(ref streamClientReport) < 120000 && Interlocked.Read(ref streamClientReport) != 0
        ? host : new(false, "Abra o LazyGaming na TV.");
}
using var background = new GameBackground(() => test ? 0 : WindowFocus.GameWindow(library), () => !test && desktop.Visible);
var launcher = new LauncherState();
var controllerKeys = new ControllerKeys();
var games = new GameService(library, platform, environment);
using var feedback = test ? null : new ControllerFeedback();
var dependencyNotices = new NoticePolicy();
var controllerDiagnosticGate = new object();
var steamGuard = new SteamGuideGuard(() => library.SteamPath);
var touchpad = new TouchpadMouse(() => !test && (environment.Active || desktop.AcceptsControllerInput), value => feedback?.Notify(value), () => { _ = Task.Run(async () => { try { await OpenConfiguredLauncher(); } catch (InvalidOperationException e) { feedback?.NotifyMessage(e.Message); } catch { feedback?.NotifyMessage("Não foi possível abrir o painel. Tente novamente."); } }); }, () => desktop.AcceptsControllerInput, controllerKeys.Push, preferences: () => pcPreferences.Current, diagnostic: ControllerDiagnostic);
desktop.ControllerReleased = () => test || touchpad.Released;
desktop.ResetInput = () => { controllerKeys.Clear(); touchpad.ResetNavigation(); };
desktop.FocusIssue = message => { RuntimeDiagnostics.Report("launcher", "focus-failed", detail: new { message }); feedback?.NotifyMessage(message); };
var app = builder.Build();
object? tvReport = null, controllerAck = null;
var hosts = addresses.Select(a => a.Address.ToString()).Append("localhost").Append("127.0.0.1").ToHashSet();
app.Use(async (ctx, next) => {
    var path = ctx.Request.Path.Value ?? "";
    var operation = ctx.Request.Method == "POST" && System.Text.RegularExpressions.Regex.IsMatch(path,
        @"^/api/(games/[^/]+/(start|stop|move-to-tv)|audio|displays|library/refresh|hub/(scan|setup|settings|tv-install|restart))$")
        ? diagnostics.Begin(ctx.Request.Method + " " + path) : null;
    bool completed = false, expectedCancellation = false;
    try { await next(); completed = ctx.Response.StatusCode < 400; }
    catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested || app.Lifetime.ApplicationStopping.IsCancellationRequested) { expectedCancellation = true; throw; }
    catch (Exception e) { RuntimeDiagnostics.Report("http", "unhandled-request", e, new { method = ctx.Request.Method, route = path }); throw; }
    finally {
        if (!completed && !expectedCancellation && operation == null && path.StartsWith("/api/", StringComparison.Ordinal) && path != "/api/diagnostics/report")
            RuntimeDiagnostics.Report("http", "request-failed", detail: new { method = ctx.Request.Method, route = path, status = ctx.Response.StatusCode });
        diagnostics.End(operation, completed, expectedCancellation ? 499 : ctx.Response.StatusCode);
    }
});
app.Use(async (ctx, next) =>
{
    if (!hosts.Contains(ctx.Request.Host.Host)) { ctx.Response.StatusCode = 403; return; }
    var remote = ctx.Connection.RemoteIpAddress;
    if (remote == null || !(IPAddress.IsLoopback(remote) || (lan && lanAddresses.Any(a =>
        a.Address.GetAddressBytes().Zip(a.IPv4Mask.GetAddressBytes(), (x,m) => (byte)(x&m)).SequenceEqual(
        remote.GetAddressBytes().Zip(a.IPv4Mask.GetAddressBytes(), (x,m) => (byte)(x&m)))))))
    { ctx.Response.StatusCode = 403; return; }
    ctx.Response.Headers.AccessControlAllowOrigin = "*";
    ctx.Response.Headers.AccessControlAllowHeaders = "Content-Type,X-LG-Time,X-LG-Nonce,X-LG-Signature";
    ctx.Response.Headers.AccessControlAllowMethods = "GET,POST,OPTIONS";
    ctx.Response.Headers.CacheControl = "no-store";
    if (ctx.Request.Method == "OPTIONS") { ctx.Response.StatusCode = 204; return; }
    if (ctx.Request.Path.StartsWithSegments("/api") && ctx.Request.Path != "/api/discover" &&
        ctx.Request.Path != "/api/pair" && ctx.Request.Path != "/api/local-pairing" &&
        ctx.Request.Path != "/api/recovery/discover" && ctx.Request.Path != "/api/recovery/restore")
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync();
        ctx.Items["body"] = body;
        bool desktopAuth = IPAddress.IsLoopback(remote) && desktop.Authenticate(ctx.Request.Cookies["lg-desktop"] ?? "") &&
            (ctx.Request.Method == "GET" || ctx.Request.Headers.Origin == $"http://127.0.0.1:{port}");
        bool hubAuth = IPAddress.IsLoopback(remote) && hubSession.Authenticate(ctx.Request.Cookies["lg-hub"] ?? "") &&
            (ctx.Request.Path.StartsWithSegments("/api/hub") || ctx.Request.Path.StartsWithSegments("/api/games") && ctx.Request.Path.Value!.EndsWith("/settings") || ctx.Request.Path == "/api/status" || ctx.Request.Path == "/api/audio" || ctx.Request.Path == "/api/displays" || ctx.Request.Path == "/api/diagnostics/report") &&
            (ctx.Request.Method == "GET" || ctx.Request.Headers.Origin == $"http://127.0.0.1:{port}");
        if (ctx.Request.Path.StartsWithSegments("/api/hub") && !IPAddress.IsLoopback(remote)) { ctx.Response.StatusCode = 403; return; }
        bool localCommandAuth = IPAddress.IsLoopback(remote) && localCommands.Verify(ctx.Request.Method, ctx.Request.Path + ctx.Request.QueryString, body,
            ctx.Request.Headers["X-LG-Time"].ToString(), ctx.Request.Headers["X-LG-Nonce"].ToString(), ctx.Request.Headers["X-LG-Signature"].ToString());
        if (!desktopAuth && !hubAuth && !localCommandAuth && !security.Verify(ctx.Request.Method, ctx.Request.Path + ctx.Request.QueryString, body,
            ctx.Request.Headers["X-LG-Time"].ToString(), ctx.Request.Headers["X-LG-Nonce"].ToString(), ctx.Request.Headers["X-LG-Signature"].ToString()))
        { ctx.Response.StatusCode = 401; await ctx.Response.WriteAsJsonAsync(new { error = "Pareamento necessário ou comando expirado." }); return; }
    }
    try { await next(); }
    catch (Exception e) when (e is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException || ctx.Request.Path.StartsWithSegments("/api/hub") && e is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
    {
        RuntimeDiagnostics.Report("operation", "request-exception", e, new { method = ctx.Request.Method, route = ctx.Request.Path.Value });
        if (ctx.Request.Path.StartsWithSegments("/api/games") || ctx.Request.Path == "/api/status") {
            try { File.WriteAllText(Path.Combine(data, "last-game-operation-error.json"), JsonSerializer.Serialize(new {
                at = DateTime.UtcNow, method = ctx.Request.Method, path = ctx.Request.Path.Value,
                errorType = e.GetType().Name, nativeError = (e as System.ComponentModel.Win32Exception)?.NativeErrorCode
            })); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        ctx.Response.StatusCode = 409; await ctx.Response.WriteAsJsonAsync(new { error = e is InvalidOperationException or InvalidDataException ? e.Message : "Não foi possível concluir a operação. Confira os arquivos e tente novamente." });
    }
});
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "wwwroot")) });
app.UseStaticFiles(new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "wwwroot")) });
app.MapPost("/api/diagnostics/report", (HttpContext ctx) => {
    using var parsed = JsonDocument.Parse((string)ctx.Items["body"]!);
    var report = parsed.RootElement;
    string Text(string name) => report.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    diagnostics.Record("frontend", Text("code"), true, detail: new { source = Text("source"), message = Text("message"), stack = Text("stack"), file = Text("file"), line = Text("line"), viewport = Text("viewport"), browser = Text("browser") });
    return Results.Ok(new { saved = diagnostics.Available });
});
app.MapPost("/api/hub/diagnostics/open", () => {
    if (!diagnostics.Available) throw new InvalidOperationException("Não foi possível gravar os relatórios neste PC. Confira o espaço livre e as permissões do seu usuário.");
    var report = diagnostics.Export();
    if (!test) {
        var open = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = false }; open.ArgumentList.Add(diagnostics.Root); System.Diagnostics.Process.Start(open);
    }
    return Results.Ok(new { ready = true, reportCreated = report != null, issues = diagnostics.IssueCount });
});
app.MapMethods("/desktop-session", ["GET", "POST"], async (HttpContext ctx) =>
{
    if (!IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress!) || ctx.Request.Host.Host != "127.0.0.1") return Results.StatusCode(403);
    if (ctx.Request.Method == "GET") {
        if (!desktop.Authenticate(ctx.Request.Cookies["lg-desktop"] ?? "")) return Results.StatusCode(401);
        desktop.SetInputClient(ctx.Request.Headers["X-LG-Client"].ToString()); return Results.Ok(new { accepted = true });
    }
    if (ctx.Request.Headers.Origin != $"http://127.0.0.1:{port}") return Results.StatusCode(403);
    using var json = await JsonDocument.ParseAsync(ctx.Request.Body);
    var cookie = desktop.Bootstrap(json.RootElement.GetProperty("token").GetString() ?? "");
    if (cookie == null) return Results.StatusCode(403);
    desktop.SetInputClient(ctx.Request.Headers["X-LG-Client"].ToString());
    ctx.Response.Cookies.Append("lg-desktop", cookie, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/" });
    return Results.Ok(new { accepted = true });
});
app.MapPost("/api/launcher/input/ack", (HttpContext ctx) => {
    if (!IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress!) || !desktop.OwnsInput(ctx.Request.Query["client"].ToString())) return Results.StatusCode(403);
    using var json = JsonDocument.Parse((string)ctx.Items["body"]!); controllerAck = json.RootElement.Clone();
    return Results.Ok(new { accepted = true });
});
app.MapPost("/api/launcher/navigation-test", (HttpContext ctx) => {
    if (!IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress!) || !desktop.Visible) return Results.StatusCode(403);
    using var json = JsonDocument.Parse((string)ctx.Items["body"]!); int key = json.RootElement.GetProperty("key").GetInt32();
    if (key is not (37 or 39)) return Results.BadRequest(); controllerKeys.Push(key); return Results.Ok(new { accepted = true });
});
app.MapGet("/api/launcher/input", async (HttpContext ctx) => {
    if (!IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress!) || !desktop.Authenticate(ctx.Request.Cookies["lg-desktop"] ?? "") || !desktop.OwnsInput(ctx.Request.Query["client"].ToString())) return Results.StatusCode(403) as IResult;
    return Results.Json(await controllerKeys.Take(ctx.RequestAborted));
});
app.MapGet("/api/launcher/sync", (long after, HttpContext ctx) => launcher.Wait(after, ctx.RequestAborted));
app.MapPost("/api/launcher/selection", (HttpContext ctx) => {
    using var json = JsonDocument.Parse((string)ctx.Items["body"]!); var id = json.RootElement.GetProperty("id").GetString();
    return id != null && library.Games.Any(g => g.Id == id) ? Results.Ok(launcher.Select(id)) : Results.BadRequest(new { error = "Jogo desconhecido." });
});
app.MapPost("/api/launcher/toggle", async (HttpContext ctx) => {
    if (!IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress!)) return Results.StatusCode(403);
    await OpenConfiguredLauncher();
    return Results.Ok(new { accepted = true });
});
app.MapPost("/api/launcher/hide", async (HttpContext ctx) => {
    if (!IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress!)) return Results.StatusCode(403);
    await desktop.Hide(); return Results.Ok(new { accepted = true });
});
app.MapGet("/api/discover", (string? challenge) => new { identity = security.Identity, name = Environment.MachineName,
    proof = security.Proof((challenge ?? "")[..Math.Min(challenge?.Length ?? 0, 100)]), test });
app.MapPost("/api/pair", (PairRequest req) => req.Invite is { Length: > 0 } invite ? security.PairInvite(invite) : security.Pair(req.Code ?? ""));
app.MapPost("/api/recovery/enroll", (HttpContext ctx) => {
    if (!test && (ctx.Connection.RemoteIpAddress == null || IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress))) return Results.StatusCode(403) as IResult;
    using var body = JsonDocument.Parse((string)ctx.Items["body"]!);
    string? current = body.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
    return Results.Ok(pairingRecovery.Enroll(current));
});
app.MapGet("/api/recovery/discover", (string id, string challenge) => pairingRecovery.Discover(id, challenge, security.Identity));
app.MapPost("/api/recovery/restore", async (HttpContext ctx) => {
    if (!test && (ctx.Connection.RemoteIpAddress == null || IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress))) return Results.StatusCode(403);
    if (ctx.Request.ContentLength is null or > 1024) return Results.BadRequest();
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var restored = pairingRecovery.Restore(security, body, ctx.Request.Headers["X-LG-Time"].ToString(), ctx.Request.Headers["X-LG-Nonce"].ToString(), ctx.Request.Headers["X-LG-Signature"].ToString());
    return restored == null ? Results.Json(new { error = "Não foi possível autorizar a recuperação nesta TV." }, statusCode: 401) : Results.Ok(restored);
});
app.MapPost("/api/local-pairing", (HttpContext ctx) =>
{
    var origin = ctx.Request.Headers.Origin.ToString();
    if (!IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress!) ||
        (origin != "" && origin != $"http://127.0.0.1:{port}" && origin != $"http://localhost:{port}")) return Results.StatusCode(403);
    return Results.Ok(new { code = security.Renew(), expiresIn = 300, addresses = lanAddresses.Select(a => $"http://{a.Address}:{port}") });
});
app.MapGet("/api/status", async (HttpContext ctx) => new { state = await games.StatusAsync(ctx.RequestAborted), artwork = library.Games.ToDictionary(g => g.Id, g => new { cover = artwork.Url(g, "cover"), backdrop = artwork.Url(g, "backdrop"), icon = artwork.Url(g, "icon") }), controller = touchpad.Status, launcher = new { visible = desktop.Visible, focused = desktop.Focused, placement = desktop.Placement, discovery = desktop.Discovery, input = controllerKeys.Status, ack = controllerAck }, displays = environment.DisplayControls, simulation = test, preview,
    simulatedLaunches = (platform as SimulatedGames)?.LaunchCount, enabled = pcPreferences.Current.Enabled,
    version = typeof(PcPreferences).Assembly.GetName().Version!.ToString(3) });
app.MapGet("/api/presentation", async () => new { cable = test ? new CableAvailability(true, false, null) : Presentation.InspectCable(), stream = await StreamStatus() });
app.MapGet("/api/presentation/cable", () => test ? new CableAvailability(true, false, null) : Presentation.InspectCable());
app.MapGet("/api/stream/status", StreamStatus);
app.MapGet("/api/stream/controls", () => streamControls.Snapshot());
app.MapPost("/api/stream/controls", (HttpContext ctx) => {
    using var json = JsonDocument.Parse((string)ctx.Items["body"]!);
    var root = json.RootElement;
    var preferences = root.GetProperty("settings").Deserialize<StreamPreferences>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    return Results.Ok(streamControls.Change(preferences, root.TryGetProperty("action", out var action) && action.ValueKind == JsonValueKind.String ? action.GetString() : null));
});
app.MapPost("/api/stream/report", async (HttpContext ctx) => {
    if (!test && (ctx.Connection.RemoteIpAddress == null || IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress))) return Results.StatusCode(403);
    using var json = JsonDocument.Parse((string)ctx.Items["body"]!); var root = json.RootElement;
    bool playing = root.GetProperty("active").GetBoolean(), becameActive = playing && !streamControls.Playing;
    var result = streamControls.Report(root.GetProperty("completed").GetInt64(), playing, root.GetProperty("profile").GetString() ?? "", root.TryGetProperty("notice", out var notice) ? notice.GetString() ?? "" : "");
    // O Desktop do Sunshine é aberto depois do jogo; conferir foco após o vídeo conectar.
    if (becameActive) await games.FocusRunning();
    return Results.Ok(result);
});
app.MapGet("/api/stream/configuration", () => {
    if (test) return Results.Conflict(new { error = "Motor de vídeo indisponível na simulação." }) as IResult;
    try { return Results.Ok(((SunshineHost)streamHost).EmbeddedConfiguration()); }
    catch (Exception e) when (e is IOException or System.Security.Cryptography.CryptographicException or JsonException or UnauthorizedAccessException) {
        Console.Error.WriteLine("Stream configuration: " + e.GetType().Name + ": " + e.Message);
        return Results.Conflict(new { error = "Os arquivos locais do Sunshine não estão acessíveis ao auxiliar. A preparação precisa ser corrigida no PC." });
    }
});
app.MapPost("/api/stream/pair", async (HttpContext ctx) => {
    if (test || ctx.Connection.RemoteIpAddress == null || IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress)) return Results.StatusCode(403);
    using var json = JsonDocument.Parse((string)ctx.Items["body"]!);
    var pin = json.RootElement.GetProperty("pin").GetString() ?? "";
    await ((SunshineHost)streamHost).ApproveEmbeddedPair(pin, ctx.Connection.RemoteIpAddress);
    return Results.Ok(new { accepted = true });
});

app.MapPost("/api/stream/client", (HttpContext ctx) => {
    using var body = JsonDocument.Parse((string)ctx.Items["body"]!);
    if (body.RootElement.TryGetProperty("installed", out var installed) && installed.ValueKind == JsonValueKind.True)
        Interlocked.Exchange(ref streamClientReport, Environment.TickCount64);
    else Interlocked.Exchange(ref streamClientReport, 0);
    return Results.Ok(new { accepted = true });
});
app.MapPost("/api/stream/confirm", async (HttpContext ctx) => {
    if (test || ctx.Connection.RemoteIpAddress == null || IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress)) return Results.StatusCode(403);
    using var body = JsonDocument.Parse((string)ctx.Items["body"]!);
    var certHash = body.RootElement.GetProperty("certHash").GetString() ?? "";
    await ((SunshineHost)streamHost).ConfirmEmbeddedClient(certHash);
    return Results.Ok(new { accepted = true });
});
app.MapGet("/api/stream/handoff", async () => {
    if (!(await StreamStatus()).Available) return Results.Conflict(new { error = "Stream indisponível. Conclua o pareamento no Moonlight." });
    return Results.Ok(await streamHost.Target());
});
app.MapGet("/api/launcher/background", async (HttpContext ctx) => {
    if (!IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress!) || !desktop.Authenticate(ctx.Request.Cookies["lg-desktop"] ?? "") || !desktop.OwnsInput(ctx.Request.Query["client"].ToString())) return Results.StatusCode(403);
    var frame = await background.Frame();
    return frame == null ? Results.NoContent() : Results.File(frame, "image/jpeg");
});
app.MapGet("/api/launcher/background-status", (HttpContext ctx) => IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress!) ? Results.Ok(background.Status) : Results.StatusCode(403));
app.MapPost("/api/tv", (HttpContext ctx) =>
{
    using var body = JsonDocument.Parse((string)ctx.Items["body"]!);
    tvReport = new { time = DateTimeOffset.UtcNow, report = body.RootElement.Clone() };
    return Results.Ok(new { accepted = true });
});
app.MapGet("/api/tv", () => Results.Ok(tvReport));
async Task EnrichLibrary(bool retryMissing = false) { try { await library.Metadata.EnsureAll(library.Games, retryMissing); await artwork.EnsureAll(); } catch (Exception e) { RuntimeDiagnostics.Report("library", "enrichment-failed", e); } }
var pcHub = new PcHub(library, games, () => EnrichLibrary(true), () => pcPreferences.Current.Enabled);
pcHub.Map(app, artwork, hubSession, port);
async Task QuitHelper() {
    await games.Rescan(() => {
        if (environment.Active) throw new InvalidOperationException("Aguarde o fim da sessão antes de fechar o auxiliar.");
        app.Lifetime.StopApplication();
    });
}
void SetActivity(bool enabled) { pcPreferences.SetEnabled(enabled); controllerKeys.Clear(); touchpad.ResetNavigation(); }
void RequireActivity() {
    if (!pcPreferences.Current.Enabled) throw new InvalidOperationException("LazyGaming pausado. Ative pelo ícone na área de notificação ou pela Central do PC.");
    if(touchpad.Capture.Active(Environment.TickCount64))throw new InvalidOperationException("Conclua ou cancele a gravação do atalho.");
}
void OpenHub() {
    if (test) return;
    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"http://127.0.0.1:{port}/hub.html#" + hubSession.Issue()) { UseShellExecute = true }); }
    catch { feedback?.NotifyMessage("Não foi possível abrir a Central do PC."); }
}
async Task OpenConfiguredLauncher() {
    // Primeiro uso acontece no próprio launcher (WebView2); sem WebView2 a Central no navegador assume abaixo.
    var dependencies = RuntimeDependencies.Inspect();
    if (!dependencies.WebView2Available) {
        if (dependencyNotices.Accept("webview-unavailable",Environment.TickCount64)) {
            feedback?.NotifyMessage(dependencies.Note!); OpenHub();
        }
        return;
    }
    await desktop.Toggle();
}
using var tray = test ? null : new PcTray(() => pcPreferences.Current.Enabled, SetActivity,
    async () => { RequireActivity(); await OpenConfiguredLauncher(); }, OpenHub, QuitHelper);
app.MapGet("/api/hub/dependencies", () => Results.Ok(RuntimeDependencies.Inspect()));
app.MapPost("/api/hub/quit", async () => {
    await QuitHelper();
    return Results.Ok(new { accepted = true });
});
app.MapGet("/api/hub/activity", () => Results.Ok(new { enabled = pcPreferences.Current.Enabled }));
app.MapPost("/api/hub/activity", (HttpContext ctx) => {
    using var json = JsonDocument.Parse((string)ctx.Items["body"]!);
    if (!json.RootElement.TryGetProperty("enabled",out var enabled) || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        return Results.BadRequest(new { error = "Escolha ativar ou pausar." });
    SetActivity(enabled.GetBoolean()); return Results.Ok(new { enabled = pcPreferences.Current.Enabled });
});
app.MapGet("/api/hub/pc-settings", () => Results.Ok(new {
    preferences = pcPreferences.Current,
    controllerOptions = PcSetup.Effective(pcPreferences.Current,touchpad.Detection), reopenAvailable,
    screens = test ? new List<Displays.Screen>() : Displays.List(),
    audio = test ? new List<AudioDevice>() : Audio.List(),
    controller = touchpad.Detection,
    tvSetup = new { lanEnabled = lan, commandLineLan = args.Contains("--lan"), paired = security.Paired,
        addresses = lan ? lanAddresses.Select(a => $"http://{a.Address}:{port}").ToArray() : [] }
}));
app.MapGet("/api/hub/controller", () => Results.Ok(new {
    controller=touchpad.Detection,
    controllerOptions=PcSetup.Effective(pcPreferences.Current,touchpad.Detection),
    defaultControllerOptions=PcSetup.Defaults(touchpad.Detection,pcPreferences.Current.ControllerSource)
}));
app.MapPost("/api/hub/pc-settings", async (HttpContext ctx) => {
    var value = JsonSerializer.Deserialize<PcPreferences>((string)ctx.Items["body"]!, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidOperationException("Configuração vazia.");
    value.Validate();
    await pcHub.Index(() => {
        if (environment.Active) throw new InvalidOperationException("Encerre a sessão antes de alterar a configuração do PC.");
        if (!test && !string.IsNullOrEmpty(value.DisplayPath) && !Displays.List().Any(s => s.Available && s.DevicePath == value.DisplayPath))
            throw new InvalidOperationException("A tela escolhida está desconectada.");
        if (!test && value.AudioDeviceId is { Length: > 0 } and not "keep" && !Audio.List().Any(d => d.Id == value.AudioDeviceId))
            throw new InvalidOperationException("A saída de áudio está desconectada.");
        pcPreferences.Save(value with { Enabled = pcPreferences.Current.Enabled, SetupCompleted = pcPreferences.Current.SetupCompleted,
            SamsungHintDismissed = pcPreferences.Current.SamsungHintDismissed, TvAutoInstall = pcPreferences.Current.TvAutoInstall,
            TizenToolsPath = pcPreferences.Current.TizenToolsPath, TvAddress = pcPreferences.Current.TvAddress, TvSetupResume = pcPreferences.Current.TvSetupResume });
    });
    return Results.Ok(pcPreferences.Current);
});
// --preview-samsung (só em teste/prévia) simula uma TV Samsung conectada e inativa para conferir a etapa opcional.
List<Displays.Screen> SetupScreens() => test ? [new("Tela de teste","test-display",5,true,true,1920,1080,60),
    .. (args.Contains("--preview-samsung") ? new[] { new Displays.Screen("SAMSUNG","test-samsung",5,true,false,3840,2160,60) } : [])] : Displays.List();
List<AudioDevice> SetupAudio() => test ? [] : Audio.List();
object SetupSnapshot() {
    var screens = SetupScreens(); var preferences = pcPreferences.Current;
    return new { preferences, screens, audio = SetupAudio(), controller = touchpad.Detection,
        controllerOptions = PcSetup.Effective(preferences,touchpad.Detection), reopenAvailable,
        defaultControllerOptions = PcSetup.Defaults(touchpad.Detection,preferences.ControllerSource),
        displayPath = preferences.DisplayPath ?? Displays.SelectTarget(screens)?.DevicePath,
        onlyGameDisplay = !environment.DisplayControls.MonitorsEnabled,
        samsungHint = PcSetup.SuggestSamsung(screens,preferences,security.Paired),
        tvSetupResume = preferences.TvSetupResume,
        samsungOffer = PcSetup.SamsungOffer(screens,preferences,security.Paired,lan,test ? [] : lanAddresses.Select(a => a.Address.ToString()).ToArray()) };
}
app.MapGet("/api/hub/setup", () => Results.Ok(SetupSnapshot()));
object IntegrationSnapshot() => new { startWithWindows = !test && PcIntegration.StartsWithWindows(), desktopShortcut = !test && PcIntegration.HasDesktopShortcut(),
    blockSteamGuide = pcPreferences.Current.BlockSteamGuide, steamGuideNote = steamGuard.ConfigNote, bigPictureClosed = steamGuard.Closed };
app.MapGet("/api/hub/integration", () => Results.Ok(IntegrationSnapshot()));
app.MapPost("/api/hub/integration", (HttpContext ctx) => {
    using var body = JsonDocument.Parse((string)ctx.Items["body"]!);
    bool? Flag(string name) => body.RootElement.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
    if (!test) {
        if (Flag("startWithWindows") is bool start) PcIntegration.SetStartWithWindows(start);
        if (Flag("desktopShortcut") is bool shortcut) PcIntegration.SetDesktopShortcut(shortcut);
        PcIntegration.EnsureStartMenu();
    }
    if (Flag("blockSteamGuide") is bool block) pcPreferences.Save(pcPreferences.Current with { BlockSteamGuide = block });
    return Results.Ok(IntegrationSnapshot());
});
app.MapPost("/api/hub/controller-capture", async () => {
    await games.Rescan(() => {
        if(environment.Active || !pcPreferences.Current.Enabled)throw new InvalidOperationException("Encerre a sessão e ative o LazyGaming para gravar.");
        touchpad.Capture.Begin(Environment.TickCount64);
    });
    return Results.Ok(touchpad.Capture.State(Environment.TickCount64));
});
app.MapGet("/api/hub/controller-capture", () => Results.Ok(touchpad.Capture.State(Environment.TickCount64)));
app.MapPost("/api/hub/controller-capture/cancel", () => { touchpad.Capture.Cancel();return Results.Ok(new { accepted=true }); });
app.MapPost("/api/hub/setup", async (HttpContext ctx) => {
    var setup = JsonSerializer.Deserialize<InitialPcSetup>((string)ctx.Items["body"]!,new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidOperationException("Confira a configuração inicial.");
    await pcHub.Index(() => {
        if (environment.Active) throw new InvalidOperationException("Encerre a sessão antes de configurar.");
        var value = PcSetup.Complete(pcPreferences.Current,setup,SetupScreens(),SetupAudio());
        environment.SetMonitors(!setup.OnlyGameDisplay);
        pcPreferences.Save(value);
    });
    return Results.Ok(SetupSnapshot());
});
app.MapPost("/api/hub/samsung-hint/dismiss", () => {
    pcPreferences.Save(pcPreferences.Current with { SamsungHintDismissed = true });
    return Results.Ok(new { accepted = true });
});
// Instalação automática do app da TV: só depois da adesão do usuário (Configurar agora).
var tvHttp = new HttpClient(new HttpClientHandler { UseProxy = false });
var tvInstaller = new TvInstaller(Path.Combine(AppContext.BaseDirectory, "wwwroot"), Path.GetTempPath(),
    () => TvInstaller.Locate(TvInstaller.DefaultRoots(pcPreferences.Current.TizenToolsPath)), new ProcessToolRunner(),
    () => lanAddresses.Select(a => a.Address.ToString()).ToArray(), (ip, cancel) => TvInstaller.Probe(tvHttp, ip, cancel),
    () => TvInstaller.Subnets(lanAddresses.Select(a => a.Address)),
    () => JsonSerializer.Serialize(new { identity = security.Identity, addresses = lanAddresses.Select(a => $"http://{a.Address}:{port}"), invite = security.IssueInvite(TimeSpan.FromHours(24)) }));
TvInstaller.Status PreviewTv() => new("installed", [new("tv", "ok", "TV de teste (prévia)"), new("developer", "ok", "Modo desenvolvedor com o endereço deste PC"),
    new("tools", "ok", "Ferramentas Tizen e perfil de teste"), new("access", "ok", "Acesso de desenvolvimento confirmado"), new("app", "ok", "LazyGaming " + tvInstaller.BundledVersion + " instalado na TV")],
    new("127.0.0.1", "Prévia", "TV de teste", true, "127.0.0.1", "preview"), tvInstaller.BundledVersion, null, null, false, DateTimeOffset.UtcNow);
var tizenDownload = new TizenStudioDownload(tvHttp, Path.Combine(Path.GetTempPath(), "LazyGaming-Tizen"));
// Certificado Samsung pelo app: login na página oficial, chaves geradas aqui, perfil registrado pela Tizen CLI.
var certificates = new SamsungCertificates(tvHttp, SamsungCertificates.DefaultFolder());
object certificateState = new { phase = "idle" }; int certificateRunning = 0;
void StartSamsungCertificate() {
    if (Interlocked.Exchange(ref certificateRunning, 1) == 1) return;
    _ = Task.Run(async () => {
        var cancel = app.Lifetime.ApplicationStopping;
        try {
            var tools = TvInstaller.Locate(TvInstaller.DefaultRoots(pcPreferences.Current.TizenToolsPath)) ?? throw new InvalidOperationException("Instale as ferramentas Tizen primeiro.");
            var tv = tvInstaller.Current.Tv ?? throw new InvalidOperationException("A TV ainda não foi encontrada. Use Tentar agora.");
            var runner = new ProcessToolRunner(); var serial = tv.Ip + ":" + TvInstaller.SdbPort;
            await runner.Run(tools.Sdb, ["connect", serial], TimeSpan.FromSeconds(20), null, cancel);
            var duid = (await runner.Run(tools.Sdb, ["-s", serial, "shell", "0", "getduid"], TimeSpan.FromSeconds(15), null, cancel)).Output.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(duid, "^[A-Za-z0-9._-]{6,64}$")) throw new InvalidOperationException("Não foi possível ler o DUID da TV. Confira o Developer Mode e tente de novo.");
            certificateState = new { phase = "login" };
            var login = await SamsungCertificates.WaitLogin(url => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }), cancel);
            certificateState = new { phase = "creating" };
            var (authorPassword, distributorPassword) = await certificates.Create(login, duid, cancel);
            certificateState = new { phase = "registering" };
            var env = tools.JavaHome != null ? new Dictionary<string, string> { ["JAVA_HOME"] = tools.JavaHome } : null;
            if (tools.ProfilesFile != null && File.ReadAllText(tools.ProfilesFile).Contains("name=\"" + SamsungCertificates.Profile + "\""))
                await runner.Run(tools.Cli, ["security-profiles", "remove", "-n", SamsungCertificates.Profile], TimeSpan.FromMinutes(1), env, cancel);
            List<string> add = ["security-profiles", "add", "-n", SamsungCertificates.Profile, "-a", certificates.AuthorFile, "-p", authorPassword, "-d", certificates.DistributorFile, "-dp", distributorPassword];
            if (tools.Profile == null) add.Add("-A");
            var added = await runner.Run(tools.Cli, add, TimeSpan.FromMinutes(1), env, cancel);
            if (TvInstaller.Locate([tools.Root])?.Profile != SamsungCertificates.Profile)
                throw new InvalidOperationException("Os certificados foram criados em " + certificates.Folder + ", mas a Tizen CLI não registrou o perfil. " + System.Text.RegularExpressions.Regex.Replace(added.Output, "(-p|-dp)\\s+\\S+", "$1 ***").Trim().Split('\n').LastOrDefault());
            certificateState = new { phase = "done", folder = certificates.Folder };
            _ = CheckTv(true);
        }
        catch (OperationCanceledException) { certificateState = new { phase = "idle" }; }
        catch (Exception e) { certificateState = new { phase = "failed", error = e.Message }; }
        finally { Interlocked.Exchange(ref certificateRunning, 0); }
    });
}
// Procura uma instalação existente do Tizen Studio no PC (uma vez a cada 10 min, com prazo) e lembra a pasta encontrada.
long toolsSearchedAt = -600_000; var toolsSearch = new SemaphoreSlim(1, 1);
async Task FindTizenTools() {
    if (TvInstaller.Locate(TvInstaller.DefaultRoots(pcPreferences.Current.TizenToolsPath)) is { } known) {
        if (pcPreferences.Current.TizenToolsPath == null) pcPreferences.Save(pcPreferences.Current with { TizenToolsPath = known.Root });
        return;
    }
    if (Environment.TickCount64 - toolsSearchedAt < (tizenDownload.Current.Phase == "opened" ? 60_000 : 600_000) || !await toolsSearch.WaitAsync(0)) return;
    try {
        toolsSearchedAt = Environment.TickCount64;
        var found = await Task.Run(() => TvInstaller.Search(TvInstaller.SearchStarts(), 9, TimeSpan.FromSeconds(25)));
        if (found != null) pcPreferences.Save(pcPreferences.Current with { TizenToolsPath = found });
    } finally { toolsSearch.Release(); }
}
async Task<TvInstaller.Status> CheckTv(bool force) {
    if (test) return PreviewTv();
    await FindTizenTools();
    var status = await tvInstaller.Check(pcPreferences.Current.TvAutoInstall && lan, force, pcPreferences.Current.TvAddress, app.Lifetime.ApplicationStopping);
    RuntimeDiagnostics.State("tv-install", status.Phase is "installed" or "idle" or "ready" ? null : status.Phase + ": " + status.Message);
    if (status.Tv?.Ip is { } ip && ip != pcPreferences.Current.TvAddress) pcPreferences.Save(pcPreferences.Current with { TvAddress = ip });
    return status;
}
object TvInstallSnapshot() => new { auto = pcPreferences.Current.TvAutoInstall, lanEnabled = lan || test, restartRequired = pcPreferences.Current.TvIntegrationEnabled && !lan && !test,
    paired = security.Paired, bundledVersion = tvInstaller.BundledVersion, toolsPath = pcPreferences.Current.TizenToolsPath,
    status = test && pcPreferences.Current.TvAutoInstall ? PreviewTv() : tvInstaller.Current,
    download = tizenDownload.Current, searchingTools = toolsSearch.CurrentCount == 0, certificate = certificateState,
    installPath = TizenStudioDownload.InstallPath(), installerReady = File.Exists(tizenDownload.File),
    addresses = lanAddresses.Select(a => a.Address.ToString()) };
app.MapGet("/api/hub/tv-install", () => Results.Ok(TvInstallSnapshot()));
app.MapPost("/api/hub/tv-install", async (HttpContext ctx) => {
    using var body = JsonDocument.Parse((string)ctx.Items["body"]!);
    var action = body.RootElement.TryGetProperty("action", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
    switch (action) {
        case "enable": pcPreferences.Save(pcPreferences.Current with { TvAutoInstall = true, TvIntegrationEnabled = true, SamsungHintDismissed = true }); break;
        case "disable": pcPreferences.Save(pcPreferences.Current with { TvAutoInstall = false }); break;
        case "tools": {
            var path = body.RootElement.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            if (path != null && (!Path.IsPathFullyQualified(path) || TvInstaller.Locate([path]) == null)) return Results.BadRequest(new { error = "Essa pasta não tem o Tizen Studio (sdb e tizen)." });
            pcPreferences.Save(pcPreferences.Current with { TizenToolsPath = path }); break;
        }
        case "check": break;
        case "download-tools": {
            if (test) return Results.Conflict(new { error = "A prévia não baixa ferramentas." });
            // O usuário aceitou a licença na tela ao clicar em instalar; o Windows pede a confirmação de administrador.
            var target = TizenStudioDownload.InstallPath();
            tizenDownload.StartInstall(Authenticode.SignedBySamsung, async file => {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, "--accept-license --no-java-check \"" + target + "\"") { UseShellExecute = true, Verb = "runas" })!;
                await process.WaitForExitAsync(app.Lifetime.ApplicationStopping); return process.ExitCode;
            }, () => {
                if (TvInstaller.Locate([target]) == null) return false;
                pcPreferences.Save(pcPreferences.Current with { TizenToolsPath = target }); _ = CheckTv(true); return true;
            }, app.Lifetime.ApplicationStopping);
            return Results.Ok(TvInstallSnapshot());
        }
        case "open-installer":
            if (test || !File.Exists(tizenDownload.File) || !Authenticode.SignedBySamsung(tizenDownload.File)) return Results.BadRequest(new { error = "Baixe o instalador primeiro." });
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tizenDownload.File) { UseShellExecute = true });
            return Results.Ok(TvInstallSnapshot());
        case "samsung-certificate":
            if (test) return Results.Conflict(new { error = "A prévia não cria certificados." });
            StartSamsungCertificate();
            return Results.Ok(TvInstallSnapshot());
        case "open-tool": {
            // Só ferramentas oficiais localizadas pelo próprio Host (caminho fixo), nunca um caminho vindo da página.
            var which = body.RootElement.TryGetProperty("tool", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : null;
            var tools = TvInstaller.Locate(TvInstaller.DefaultRoots(pcPreferences.Current.TizenToolsPath));
            var exe = which == "certificate-manager" ? tools?.CertificateManager : which == "package-manager" ? tools?.PackageManager : null;
            if (test) return Results.Conflict(new { error = "A prévia não abre ferramentas." });
            if (exe == null) return Results.BadRequest(new { error = "Ferramenta não encontrada nesta instalação do Tizen Studio." });
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
            return Results.Ok(TvInstallSnapshot());
        }
        default: return Results.BadRequest(new { error = "Ação desconhecida." });
    }
    if (action is "enable" or "check" or "tools") { var tvCheck = CheckTv(true); await Task.WhenAny(tvCheck, Task.Delay(400)); }
    return Results.Ok(TvInstallSnapshot());
});
// Reinicia somente o auxiliar (abre a rede local para a TV). Nunca durante sessão; Windows e TV não são tocados.
app.MapPost("/api/hub/restart", async () => {
    if (test) return Results.Conflict(new { error = "A prévia não reinicia o auxiliar." });
    await games.Rescan(() => { if (environment.Active) throw new InvalidOperationException("Encerre a sessão antes de reiniciar o LazyGaming."); });
    pcPreferences.Save(pcPreferences.Current with { TvSetupResume = true });
    app.Lifetime.ApplicationStopped.Register(() => {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, ["--wait-pid", Environment.ProcessId.ToString(), "--launcher"]) { UseShellExecute = false }); } catch { }
    });
    _ = Task.Run(async () => { await Task.Delay(300); app.Lifetime.StopApplication(); });
    return Results.Ok(new { accepted = true });
});
app.MapPost("/api/hub/tv-setup/opened", () => { if (pcPreferences.Current.TvSetupResume) pcPreferences.Save(pcPreferences.Current with { TvSetupResume = false }); return Results.Ok(new { accepted = true }); });
// Retomar pelo painel avançado: só desfaz a dispensa; não ativa LAN nem consulta a TV.
app.MapPost("/api/hub/samsung-hint/resume", () => {
    pcPreferences.Save(pcPreferences.Current with { SamsungHintDismissed = false });
    return Results.Ok(SetupSnapshot());
});
app.MapPost("/api/library/refresh", async () => { await games.Rescan(); if (!test || preview) _ = EnrichLibrary(true); return Results.Ok(new { count = library.Games.Count }); });
app.MapGet("/api/library/search", async (string query) => Results.Ok(await library.Metadata.Search(query)));
app.MapGet("/api/games/{id}/settings", (string id) => {
    var game = library.Games.SingleOrDefault(g => g.Id == id) ?? throw new InvalidOperationException("Jogo desconhecido.");
    return Results.Ok(new { name = library.Metadata.DisplayName(game), originalName = game.Name, metadata = library.Metadata.For(game), customName = library.Metadata.Customization(game).Name });
});
app.MapPost("/api/games/{id}/settings", async (string id, HttpContext ctx) => {
    var game = library.Games.SingleOrDefault(g => g.Id == id) ?? throw new InvalidOperationException("Jogo desconhecido.");
    using var body = JsonDocument.Parse((string)ctx.Items["body"]!);
    if (body.RootElement.TryGetProperty("name", out var name)) {
        if (name.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) return Results.BadRequest(new { error = "Nome inválido." });
        library.Metadata.Rename(game, name.GetString());
    }
    if (body.RootElement.TryGetProperty("storeId", out var storeId)) {
        if (storeId.ValueKind != JsonValueKind.String) return Results.BadRequest(new { error = "Escolha um jogo do catálogo." });
        await library.Metadata.Assign(game, storeId.GetString()!); _ = artwork.EnsureAll();
    }
    return Results.Ok(new { saved = true });
});
app.MapGet("/art/{id}/{kind}.jpg", (string id, string kind, HttpContext ctx) =>
{
    var file = artwork.FileFor(id, kind); if (file == null) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "public,max-age=2592000,immutable";
    return Results.File(file, "image/jpeg");
});
app.MapPost("/api/games/{id}/focus", async (string id, HttpContext ctx) => {
    if (test) return Results.Ok(new { focused = false });
    var game = library.Games.SingleOrDefault(g => g.Id == id) ?? throw new InvalidOperationException("Jogo desconhecido.");
    platform.TargetDisplay(game);
    await desktop.Hide(ctx.Connection.RemoteIpAddress is { } origin && IPAddress.IsLoopback(origin));
    var focused = WindowFocus.ActivateGame(WindowFocus.GameWindow(game));
    return Results.Ok(new { focused });
});
app.MapPost("/api/games/{id}/move-to-tv", async (string id, HttpContext ctx) => {
    RequireActivity();
    if (preview) return Results.Conflict(new { error = "A prévia não move jogos reais." });
    await games.MoveToTv(id, ctx.RequestAborted);
    return Results.Ok(new { moved = true });
});
app.MapPost("/api/games/{id}/start", async (string id, HttpContext ctx) => { if (preview) return Results.Conflict(new { error = "A prévia não inicia jogos." });
    RequireActivity();
    using var body = JsonDocument.Parse((string)ctx.Items["body"]!);
    var mode = body.RootElement.TryGetProperty("mode", out var selectedMode) ? selectedMode.GetString() ?? "" : "cable";
    Presentation.Validate(mode, mode == "stream" && (await StreamStatus()).Available);
    if (!test && desktop.Visible && ctx.Connection.RemoteIpAddress is { } localInput && IPAddress.IsLoopback(localInput)) await desktop.WaitForControllerRelease();
    if (!test && ctx.Connection.RemoteIpAddress is { } tvInput && !IPAddress.IsLoopback(tvInput)) await desktop.Hide(false);
    await games.Start(id, mode, body.RootElement.TryGetProperty("choice", out var choice) ? choice.GetString() : null);
    return Results.Ok(new { accepted = true }); });
app.MapPost("/api/games/{id}/stop", async (string id, HttpContext ctx) =>
{
    if (preview) return Results.Conflict(new { error = "A prévia não controla processos." });
    using var body = JsonDocument.Parse((string)ctx.Items["body"]!);
    bool force = body.RootElement.TryGetProperty("force", out var f) && f.ValueKind == JsonValueKind.True;
    if (force && (!body.RootElement.TryGetProperty("confirm", out var c) || c.GetString() != "LOSE_UNSAVED_PROGRESS"))
        return Results.BadRequest(new { error = "Confirme a possível perda de progresso não salvo." });
    var nextGame = body.RootElement.TryGetProperty("nextGame", out var next) ? next.GetString() : null;
    if (nextGame != null) RequireActivity();
    var nextMode = body.RootElement.TryGetProperty("mode", out var transport) ? transport.GetString() : null;
    await games.Stop(id, force, nextGame, nextMode); return Results.Ok(new { accepted = true });
});
app.MapGet("/api/audio", () => test ? new List<AudioDevice>() : Audio.List());
app.MapGet("/api/displays", () => environment.DisplayControls);
app.MapPost("/api/displays", async (HttpContext ctx) => {
    RequireActivity();
    using var body = JsonDocument.Parse((string)ctx.Items["body"]!);
    if (!body.RootElement.TryGetProperty("monitorsEnabled", out var enabled) || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        return Results.BadRequest(new { error = "Escolha ligar ou desligar os monitores." });
    await games.SetMonitors(enabled.GetBoolean());
    return Results.Ok(environment.DisplayControls);
});
app.MapPost("/api/audio", (HttpContext ctx) =>
{
    if (test) throw new InvalidOperationException("Áudio indisponível na simulação.");
    using var body = JsonDocument.Parse((string)ctx.Items["body"]!);
    environment.ManualAudio(body.RootElement.GetProperty("id").GetString()!);
    return Results.Ok(new { accepted = true });
});
var simulatedVolume = new VolumeState(50, false, "Saída simulada");
var simulatedGameVolume = new Audio.AppVolume(80, false);
// Jogo verificado em execução (inclui emuladores cadastrados, como o Eglantine): PIDs do jogo e pasta dele, porque alguns
// jogos tocam áudio por um processo auxiliar da própria pasta. Runtimes compartilhados (dotnet/java) só pelo PID.
(string Name, Func<int, bool> Match)? RunningGameAudio() {
    var running = platform.Processes();
    if (running.Count == 0) return null;
    var game = library.Games.FirstOrDefault(g => running.Any(r => string.Equals(g.ProcessPath, r.Path, StringComparison.OrdinalIgnoreCase)));
    var pids = running.Select(r => r.Pid).ToHashSet();
    var folders = running.Where(r => r.Argument == null).Select(r => Path.GetDirectoryName(r.Path)).Where(f => f != null).Select(f => f!.TrimEnd('\\') + "\\").Distinct().ToArray();
    return (game != null ? library.Metadata.DisplayName(game) : Path.GetFileNameWithoutExtension(running[0].Path),
        pid => pids.Contains(pid) || ProcessImage.Observe(pid).Image is string image && folders.Any(f => image.StartsWith(f, StringComparison.OrdinalIgnoreCase)));
}
object VolumeSnapshot(VolumeState system) {
    object? game = null;
    if (test) game = new { name = "Jogo de teste", level = simulatedGameVolume.Level, muted = simulatedGameVolume.Muted };
    else if (RunningGameAudio() is { } audio && Audio.SessionVolume(audio.Match) is { } v) game = new { name = audio.Name, level = v.Level, muted = v.Muted };
    return new { level = system.Level, muted = system.Muted, device = system.Device, game };
}
app.MapGet("/api/volume", () => VolumeSnapshot(test ? simulatedVolume : Audio.Volume()));
app.MapPost("/api/volume", (HttpContext ctx) =>
{
    using var body = JsonDocument.Parse((string)ctx.Items["body"]!);
    var forGame = body.RootElement.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.String && target.GetString() == "game";
    var system = test ? simulatedVolume : Audio.Volume();
    if (forGame) {
        if (!body.RootElement.TryGetProperty("level", out var gameRequested) || !gameRequested.TryGetInt32(out var gameLevel)) return Results.BadRequest(new { error = "Informe o volume desejado." });
        gameLevel = Math.Clamp(gameLevel, 0, 100);
        if (test) { simulatedGameVolume = new(gameLevel, false); return Results.Ok(VolumeSnapshot(system)); }
        var audio = RunningGameAudio() ?? throw new InvalidOperationException("Nenhum jogo aberto.");
        if (Audio.SetSessionVolume(audio.Match, gameLevel) == null) throw new InvalidOperationException("O jogo ainda não está tocando som.");
        return Results.Ok(VolumeSnapshot(system));
    }
    int level;
    if (body.RootElement.TryGetProperty("level", out var requested) && requested.TryGetInt32(out var absolute)) level = absolute;
    else if (body.RootElement.TryGetProperty("direction", out var direction) && direction.TryGetInt32(out var step) && step != 0) level = Audio.StepVolume(system.Level, step);
    else return Results.BadRequest(new { error = "Informe o volume desejado." });
    level = Math.Clamp(level, 0, 100);
    if (test) return Results.Ok(VolumeSnapshot(simulatedVolume = simulatedVolume with { Level = level, Muted = level == 0 && simulatedVolume.Muted }));
    return Results.Ok(VolumeSnapshot(Audio.SetVolume(level)));
});
Console.WriteLine($"LazyGaming: http://127.0.0.1:{port} ({(test ? "SIMULAÇÃO" : lan ? "rede local" : "somente PC")})");
Console.WriteLine("Pareie pela página aberta no PC. Ctrl+C encerra o auxiliar; jogos permanecem abertos.");
// Vigia: a leitura do controle nunca fica parada até reiniciar o auxiliar.
if (!test) _ = Task.Run(async () => {
    var stopping = app.Lifetime.ApplicationStopping;
    while (!stopping.IsCancellationRequested) {
        try { await touchpad.Run(stopping); }
        catch (Exception e) when (!stopping.IsCancellationRequested) { ControllerDiagnostic("read-task-fault", e); }
        if (stopping.IsCancellationRequested) break;
        ControllerDiagnostic("read-task-restart", null);
        try { await Task.Delay(1000, stopping); } catch (OperationCanceledException) { break; }
    }
});
// Diagnóstico local do controle: só tipo/mensagem/primeiro quadro, sem relatórios, chaves ou caminhos de usuário.
void ControllerDiagnostic(string kind, Exception? error) {
    RuntimeDiagnostics.Report("controller", kind, error);
    try {
        var file = Path.Combine(data, "controller-diagnostics.jsonl");
        lock (controllerDiagnosticGate) {
            if (File.Exists(file) && new FileInfo(file).Length > 256 * 1024) File.Move(file, file + ".old", true);
            var frame = error?.StackTrace?.Split('\n').FirstOrDefault()?.Trim();
            File.AppendAllText(file, JsonSerializer.Serialize(new { at = DateTime.UtcNow, kind, version = typeof(Program).Assembly.GetName().Version?.ToString(3), error = error?.GetType().FullName, message = error?.Message, frame }) + "\n");
        }
    } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
}
if (!test || preview) _ = EnrichLibrary();
_ = pcHub.Automatic(app.Lifetime.ApplicationStopping);
_ = Task.Run(async () =>
{
    while (!app.Lifetime.ApplicationStopping.IsCancellationRequested)
    {
        try { await games.Tick(pcPreferences.Current.Enabled); await background.Tick(); } catch (Exception e) { RuntimeDiagnostics.Report("game-loop", "tick-failed", e); }
        try { if (!test) steamGuard.Tick(pcPreferences.Current.BlockSteamGuide, environment.Active || desktop.Visible); } catch (Exception e) { RuntimeDiagnostics.Report("steam", "guide-guard-failed", e); }
        await Task.Delay(750);
    }
});
// Instala/atualiza o app da TV sozinho quando todas as condições ficam verdadeiras (adesão, TV, Developer Mode, ferramentas, acesso).
if (!test) _ = Task.Run(async () => {
    try { await Task.Delay(5000, app.Lifetime.ApplicationStopping); } catch (OperationCanceledException) { return; }
    while (!app.Lifetime.ApplicationStopping.IsCancellationRequested) {
        try { if (pcPreferences.Current.TvAutoInstall && !environment.Active) await CheckTv(false); } catch (OperationCanceledException) { break; } catch (Exception e) { RuntimeDiagnostics.Report("tv-install", "check-failed", e); }
        try { await Task.Delay(tvInstaller.Current.Phase == "installed" ? 120_000 : 20_000, app.Lifetime.ApplicationStopping); } catch (OperationCanceledException) { break; }
    }
});
if (!test && args.Contains("--hub")) app.Lifetime.ApplicationStarted.Register(OpenHub);
else if (!test && !pcPreferences.Current.SetupCompleted) app.Lifetime.ApplicationStarted.Register(() => { _ = Task.Run(async () => { try { await OpenConfiguredLauncher(); } catch { OpenHub(); } }); });
else if (args.Contains("--launcher")) app.Lifetime.ApplicationStarted.Register(() => { _ = Task.Run(async () => { try { await OpenConfiguredLauncher(); } catch { feedback?.NotifyMessage("Não foi possível abrir o launcher. Abra a Central pelo ícone do LazyGaming."); } }); });
app.Lifetime.ApplicationStopping.Register(desktop.Close);
app.Lifetime.ApplicationStopped.Register(() => (platform as IDisposable)?.Dispose());
app.Lifetime.ApplicationStarted.Register(() => {
    _ = Task.Run(async () => {
        diagnostics.CaptureSystem();
        diagnostics.Context("library", new { games = library.Games.Count, steam = library.Games.Count(g => g.Steam), lanEnabled = lan, controllers = pcPreferences.Current.ControllerSource });
        await diagnostics.ImportWindowsCrashes(library.Games.Where(g => g.ProcessArgument == null || EmulatorLibrary.IsEmulator(g.ProcessPath)).Select(g => Path.GetFileName(g.ProcessPath)));
    });
});
await app.RunAsync(); diagnostics.Complete();
} catch (Exception e) { RuntimeDiagnostics.Report("process", "host-failed", e); throw; }
record PairRequest(string? Code, string? Invite = null);
static class HostConsole {
    public static void AttachToParentTerminal() {
        if (Console.IsOutputRedirected) return;
        try { AttachConsole(-1); } catch (EntryPointNotFoundException) { }
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AttachConsole(int process);
}
