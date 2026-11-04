using System.Diagnostics;

namespace LazyGaming;

public record Game(string Id, string Name, string Launch, string ProcessPath, bool Steam = false, string? ArtworkId = null, string? Folder = null, string? ProcessArgument = null, string? LaunchDirectory = null, GameMetadata? LocalMetadata = null, string[]? LaunchArguments = null, LaunchChoice[]? Choices = null, string? ChoiceLabel = null);
public record Running(int Pid, string Path, string? Argument = null);
public interface IGamePlatform
{
    List<Running> Processes();
    void Launch(Game game, string mode = "cable");
    bool RequestClose(Running process);
    void ForceClose(Running process);
    void TargetDisplay(Game game) { }
    void CancelTarget(Game game) { }
    void PlaceWindows(IReadOnlyList<Running> processes) { }
    Task<bool> MoveToTv(Running process, CancellationToken cancellation) => Task.FromResult(false);
    string? DisplayError => null;
}

public sealed class WindowsGames(GameLibrary library, string directory) : IGamePlatform, IDisposable
{
    readonly ClosingGames closing = new();
    string? lastIdentificationError;
    volatile GameWindowTarget? windowTarget;
    readonly TaskbarOverflow taskbarOverflow = new(directory);
    public Func<bool>? MenuVisible { get; set; }
    string? lastFocusState;
    public void TargetDisplay(Game game) {
        _ = Displays.GameDisplay();
        windowTarget = new(game.ProcessPath, DateTime.UtcNow.AddSeconds(90), WindowFocus.InputStamp);
    }
    public string? DisplayError => windowTarget?.Error;
    public void CancelTarget(Game game) => windowTarget?.Cancel(game.ProcessPath);
    public async Task<bool> MoveToTv(Running process, CancellationToken cancellation)
    {
        // Identidade e vida do processo verificadas; nenhum HWND vem da página.
        using var lifetime = ProcessImage.Track(process.Pid, process.Path);
        using var verified = Verified(process);
        windowTarget?.Cancel(process.Path);
        int stable = 0;
        for (int attempt = 0; attempt < 16; attempt++) {
            cancellation.ThrowIfCancellationRequested();
            if (lifetime.Exited) throw new InvalidOperationException("O jogo foi fechado durante a tentativa.");
            verified.Refresh();
            var window = verified.MainWindowHandle;
            if (WindowFocus.BelongsTo(window, process.Pid)) {
                WindowFocus.RestoreForPlacement(window);
                bool placed = WindowFocus.PlaceOnTv(window);
                stable = placed ? stable + 1 : 0;
                if (stable >= 8) return true;
            } else stable = 0;
            await Task.Delay(150, cancellation);
        }
        return false;
    }
    public void PlaceWindows(IReadOnlyList<Running> processes) { if (MenuVisible?.Invoke() == true) return; windowTarget?.Update(processes, DateTime.UtcNow, process => {
        using var verified = Verified(process);
        var window = verified.MainWindowHandle;
        if (!WindowFocus.BelongsTo(window, process.Pid)) return false;
        WindowFocus.RestoreForPlacement(window);
        bool positioned = WindowFocus.PlaceOnTv(window), focused = positioned && WindowFocus.ActivateGame(window);
        if (positioned && windowTarget is { } target) {
            taskbarOverflow.Schedule(window, process, () => ReferenceEquals(windowTarget, target)
                && target.CanAutomate(DateTime.UtcNow) && MenuVisible?.Invoke() != true);
        }
        var diagnostic = System.Text.Json.JsonSerializer.Serialize(new { pid = process.Pid, positioned, focused, phase = windowTarget?.Phase, window = WindowFocus.Inspect(window) });
        if (diagnostic != lastFocusState) {
            lastFocusState = diagnostic;
            try { File.WriteAllText(Path.Combine(directory, "game-focus-status.json"), diagnostic); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            try {
                var trace = Path.Combine(directory, "game-focus-history.jsonl");
                if (File.Exists(trace) && new FileInfo(trace).Length > 65_536) File.WriteAllText(trace, "");
                File.AppendAllText(trace, System.Text.Json.JsonSerializer.Serialize(new { at = DateTime.UtcNow, state = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(diagnostic) }) + Environment.NewLine);
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return positioned && focused;
    }); }
    public List<Running> Processes()
    {
        var result = closing.Active(process => StillListed(Path.GetFileNameWithoutExtension(process.Path), process.Argument != null ? process.Pid : null));
        var closingIds = result.Select(p => p.Pid).ToHashSet();
        // Nome só bloqueia a troca durante um fechamento já aceito. Não serve
        // como identidade para fechar/forçar outro PID. Runtimes compartilhados
        // continuam restritos ao objeto original, nunca ao grupo dotnet/java.
        var closingNames = result.Where(p => p.Argument == null).Select(p => Path.GetFileNameWithoutExtension(p.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = library.Games.Where(g => g.ProcessPath != "").Select(g => Path.GetFileNameWithoutExtension(g.ProcessPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in names.SelectMany(name => Process.GetProcessesByName(name).Select(process => (Name: name, Process: process))))
        {
            using (var p = candidate.Process)
            {
                if (closingIds.Contains(p.Id) || closingNames.Contains(candidate.Name)) continue;
                var observed = ProcessImage.ResolveObservation(() => ProcessImage.Observe(p.Id), () => StillListed(candidate.Name, p.Id), () => Thread.Sleep(60));
                if (observed.Exited) continue;
                if (observed.Image is string path)
                {
                    var candidates = library.Games.Where(g => string.Equals(g.ProcessPath, path, StringComparison.OrdinalIgnoreCase));
                    var content = candidates.SelectMany(LaunchChoices.Contents).FirstOrDefault(argument => argument == null || RuntimeMatches(p.Id, argument));
                    if (content == null && !candidates.Any(g => LaunchChoices.Contents(g).Contains(null))) continue;
                    result.Add(new(p.Id, path, content));
                } else {
                    IdentificationError(p.Id, candidate.Name, observed.Error);
                    throw new InvalidOperationException("O Windows não permitiu confirmar a identidade de um jogo ainda em execução. Aguarde alguns segundos e tente novamente.");
                }
            }
        }
        return result;
    }
    static bool StillListed(string name, int? pid = null) {
        bool found = false;
        foreach (var process in Process.GetProcessesByName(name)) using (process) { if (pid == null || process.Id == pid) found = true; }
        return found;
    }
    void IdentificationError(int pid, string name, int code) {
        var json = System.Text.Json.JsonSerializer.Serialize(new { pid, name, nativeError = code, stage = "image-query" });
        if (json == lastIdentificationError) return;
        lastIdentificationError = json;
        try { File.WriteAllText(Path.Combine(directory, "process-identification-status.json"), json); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    public void Dispose() => closing.Dispose();
    public static bool RuntimeMatches(int pid, string argument)
    {
        // WMI consulta o comando do runtime específico do jogo; não lê memória do jogo.
        object? service = null, rows = null;
        try
        {
            service = System.Runtime.InteropServices.Marshal.BindToMoniker(@"winmgmts:\\.\root\cimv2");
            rows = ((dynamic)service).ExecQuery("SELECT CommandLine FROM Win32_Process WHERE ProcessId=" + pid);
            foreach (dynamic row in (System.Collections.IEnumerable)rows)
            {
                object? properties = null, property = null;
                try
                {
                    // A propriedade dinâmica CommandLine falha em SWbemObject mesmo
                    // quando o WMI devolve o processo. Usar a coleção explícita,
                    // como na identificação da janela do launcher.
                    properties = row.Properties_;
                    property = ((dynamic)properties).Item("CommandLine", 0);
                    string command = ((dynamic)property).Value ?? "";
                    return RuntimeCommandMatches(command, argument);
                }
                finally {
                    if (property != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(property);
                    if (properties != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(properties);
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(row);
                }
            }
        }
        catch (Exception error) { throw new InvalidOperationException("Não foi possível identificar o runtime do jogo com segurança.", error); }
        finally { if (rows != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(rows); if (service != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(service); }
        return false;
    }
    public static bool RuntimeCommandMatches(string command, string argument) =>
        System.Text.RegularExpressions.Regex.IsMatch(command, "(?:^|[\\s\"\\\\/])" + System.Text.RegularExpressions.Regex.Escape(argument) + "(?:$|[\\s\"])", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        && !System.Text.RegularExpressions.Regex.IsMatch(command, "(?:^|\\s)-server(?:$|\\s)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    public void Launch(Game game, string mode = "cable")
    {
        try { LaunchCore(game, mode); }
        catch (Exception e) {
            File.WriteAllText(Path.Combine(directory, "last-launch-error.json"), System.Text.Json.JsonSerializer.Serialize(new { game = game.Id, mode, error = e.Message, at = DateTime.UtcNow }));
            throw;
        }
    }
    void LaunchCore(Game game, string mode)
    {
        GameDisplaySettings.Prepare(game, directory);
        if (game.Id == "steam-632360") {
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(game.ProcessPath)!, "UnityPlayer.dll")))
                throw new InvalidOperationException("A instalação Unity do Risk of Rain 2 não foi identificada.");
            var target = LaunchDisplay.Current();var bounds=target.Bounds;
            var launch = new ProcessStartInfo(Path.Combine(library.SteamPath, "steam.exe")) { UseShellExecute = false };
            foreach (var argument in GameDisplaySettings.RiskLaunchArguments(target.Monitor, bounds.Width, bounds.Height)) launch.ArgumentList.Add(argument);
            Process.Start(launch);
            return;
        }
        var info = game.Steam ? SteamLaunchInfo(game, library.SteamPath) : LaunchInfo(game);
        if (GameDisplaySettings.CanUseUnityDisplayArguments(game)) {
            var target = LaunchDisplay.Current();var bounds=target.Bounds;
            foreach (var argument in GameDisplaySettings.UnityDisplayArguments(target.Monitor, bounds.Width, bounds.Height)) info.ArgumentList.Add(argument);
        }
        using var launched = Process.Start(info);
    }
    public static ProcessStartInfo SteamLaunchInfo(Game game, string steamPath) {
        var id = game.ArtworkId;
        if (!game.Steam || !uint.TryParse(id, out _) || game.Launch != "steam://rungameid/" + id)
            throw new InvalidOperationException("Identidade Steam inválida para abertura.");
        var info = new ProcessStartInfo(Path.Combine(steamPath, "steam.exe")) { UseShellExecute = false, WorkingDirectory = steamPath };
        info.ArgumentList.Add("-applaunch"); info.ArgumentList.Add(id);
        return info;
    }
    public static ProcessStartInfo LaunchInfo(Game game) {
        var info = new ProcessStartInfo(game.Launch) {
        // EXE local não precisa de associações, DDE ou extensões do Shell.
        // ShellExecuteEx ficou preso no wrapper de Mighty e reteve o gate do jogo.
        // Steam continua usando seu protocolo/launcher já aprovado.
        UseShellExecute = game.Steam,
        WorkingDirectory = game.LaunchDirectory ?? Path.GetDirectoryName(game.ProcessPath)!
        };
        foreach (var argument in game.LaunchArguments ?? []) info.ArgumentList.Add(argument);
        return info;
    }
    static Process Verified(Running process)
    {
        var p = Process.GetProcessById(process.Pid);
        if (!string.Equals(ProcessImage.PathOf(p.Id), process.Path, StringComparison.OrdinalIgnoreCase))
        { p.Dispose(); throw new InvalidOperationException("O processo mudou; atualize o estado."); }
        if (process.Argument != null && !RuntimeMatches(p.Id, process.Argument)) { p.Dispose(); throw new InvalidOperationException("O runtime não pertence mais ao jogo esperado."); }
        return p;
    }
    public bool RequestClose(Running process) {
        IProcessLifetime? lifetime = null;
        try {
            lifetime = ProcessImage.Track(process.Pid, process.Path);
            using var p = Verified(process);
            if (lifetime.Exited) { windowTarget?.Cancel(process.Path); return true; }
            bool accepted;
            try { accepted = p.CloseMainWindow(); }
            catch (InvalidOperationException) when (lifetime.Exited) { accepted = true; }
            catch (System.ComponentModel.Win32Exception) when (lifetime.Exited) { accepted = true; }
            accepted |= lifetime.Exited;
            if (accepted) { closing.Add(process, lifetime); lifetime = null; windowTarget?.Cancel(process.Path); }
            return accepted;
        } catch (InvalidOperationException) when (lifetime?.Exited == true) { windowTarget?.Cancel(process.Path); return true; }
        catch (System.ComponentModel.Win32Exception) when (lifetime?.Exited == true || ProcessImage.Observe(process.Pid).Exited) { windowTarget?.Cancel(process.Path); return true; }
        finally { lifetime?.Dispose(); }
    }
    public void ForceClose(Running process) {
        var lifetime = ProcessImage.Track(process.Pid, process.Path);
        try { using var p = Verified(process); if (!lifetime.Exited) p.Kill(false); closing.Add(process, lifetime); windowTarget?.Cancel(process.Path); }
        catch { lifetime.Dispose(); throw; }
    }
}

public sealed class GameService(GameLibrary library, IGamePlatform platform, IGameEnvironment environment)
{
    readonly LaunchPreferenceStore preferences = new(library.Directory);
    IReadOnlyList<Game> catalog => library.Games;
    readonly SemaphoreSlim gate = new(1, 1);
    string? starting;
    string? startingChoice;
    DateTime deadline;
    DateTime? idleSince;
    DateTime switchUntil;
    string? openingError;
    void ExpireOpening(IReadOnlyList<Running> running) {
        if (starting == null || deadline > DateTime.UtcNow) return;
        var game = catalog.Single(g => g.Id == starting);
        if (!running.Any(p => LaunchChoices.Matches(game, p))) {
            openingError = "O jogo não abriu no tempo esperado. Confira e feche qualquer aviso do Steam antes de tentar novamente.";
            platform.CancelTarget(game);
        }
        starting = null;
    }
    public async Task SetMonitors(bool enabled)
    {
        await gate.WaitAsync();
        try { environment.SetMonitors(enabled); }
        finally { gate.Release(); }
    }
    public async Task FocusRunning()
    {
        await gate.WaitAsync();
        try {
            var processes = platform.Processes();
            var running = catalog.Where(g => processes.Any(p => LaunchChoices.Matches(g, p))).ToArray();
            if (running.Length == 1) { environment.ReconfirmDesktop(); platform.TargetDisplay(LaunchChoices.ForProcess(running[0], processes.First(p => LaunchChoices.Matches(running[0], p)))); }
        } finally { gate.Release(); }
    }
    public async Task Rescan(Action? change = null)
    {
        await gate.WaitAsync();
        try
        {
            if (starting != null || platform.Processes().Count > 0) throw new InvalidOperationException("Encerre o jogo antes de atualizar a biblioteca.");
            change?.Invoke();
            library.Refresh();
        }
        finally { gate.Release(); }
    }
    public object Status() => StatusAsync().GetAwaiter().GetResult();
    public async Task<object> StatusAsync(CancellationToken cancellation = default)
    {
        if (!await gate.WaitAsync(TimeSpan.FromSeconds(2), cancellation))
            throw new InvalidOperationException("Uma operação do jogo ainda está em andamento. Aguarde antes de tentar novamente.");
        try {
        var running = platform.Processes();
        ExpireOpening(running);
        if (starting != null && running.Any(p => LaunchChoices.Matches(catalog.Single(g => g.Id == starting), p))) starting = null;
        return new { environment = environment.Status, games = catalog.Select(g => new { g.Id, name = library.Metadata.DisplayName(g), origin = g.Steam ? "Steam" : "PC", artworkId = library.Metadata.ArtworkId(g), releaseYear = library.Metadata.For(g)?.Year,
            available = File.Exists(g.ProcessPath) && (g.Choices == null || g.Choices.Any(v => File.Exists(v.ContentPath))),
            choiceLabel = g.ChoiceLabel, selectedChoice = preferences.Get(g),
            choices = g.Choices?.Select(v => new { v.Id, v.Name, available = File.Exists(v.ContentPath) }).ToArray(),
            runningChoice = g.Choices?.FirstOrDefault(v => running.Any(p => LaunchChoices.Matches(g, p) && string.Equals(p.Argument, v.ContentPath, StringComparison.OrdinalIgnoreCase)))?.Id,
            running = running.Any(p => LaunchChoices.Matches(g, p)),
            starting = starting == g.Id && deadline > DateTime.UtcNow }).ToArray(),
            startError = openingError ?? platform.DisplayError ?? (starting != null && deadline <= DateTime.UtcNow &&
                !running.Any(p => LaunchChoices.Matches(catalog.Single(g => g.Id == starting), p))
                ? "O jogo não abriu no tempo esperado. Confira o launcher no PC antes de tentar novamente." : null) };
        } finally { gate.Release(); }
    }
    public async Task MoveToTv(string id, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try {
            var game = catalog.SingleOrDefault(g => g.Id == id) ?? throw new InvalidOperationException("Jogo desconhecido.");
            var processes = platform.Processes().Where(p => LaunchChoices.Matches(game, p)).ToArray();
            if (processes.Length != 1) throw new InvalidOperationException(processes.Length == 0
                ? "O jogo ainda não tem um processo identificado em execução. Aguarde a abertura e tente novamente."
                : "Há mais de uma instância desse jogo. Não foi possível escolher a janela com segurança.");
            var state = System.Text.Json.JsonSerializer.SerializeToElement(environment.Status);
            var mode = environment.Active && state.TryGetProperty("mode", out var currentMode) ? currentMode.GetString() ?? "cable" : "cable";
            environment.Prepare(mode);
            // Pode reparar atrás do menu: não ativa o jogo nem disputa foco com o controle.
            if (!await platform.MoveToTv(processes[0], cancellation))
                throw new InvalidOperationException("O jogo não confirmou a janela na TV. Se disponível, escolha Janela sem bordas nas opções do jogo e tente novamente.");
            idleSince = null;
        } finally { gate.Release(); }
    }
    public async Task Start(string id, string mode = "cable", string? choice = null)
    {
        await gate.WaitAsync();
        try
        {
            var game = catalog.SingleOrDefault(g => g.Id == id) ?? throw new InvalidOperationException("Jogo desconhecido.");
            var processes = platform.Processes();
            ExpireOpening(processes);
            openingError = null;
            var active = processes.Where(p => LaunchChoices.Matches(game, p)).ToArray();
            var selectedChoice = choice ?? preferences.Get(game);
            if (active.Length > 0) {
                if (choice != null && game.Choices?.Any(v => v.Id == choice) != true)
                    throw new InvalidOperationException("Esta versão do jogo não está cadastrada.");
                if (choice != null && !active.All(p => string.Equals(p.Argument, game.Choices!.Single(v => v.Id == choice).ContentPath, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Encerre o jogo antes de trocar de versão.");
                environment.Prepare(mode); platform.TargetDisplay(LaunchChoices.ForProcess(game, active[0])); starting = null; idleSince = null; switchUntil = default; return;
            }
            game = LaunchChoices.Select(game, selectedChoice);
            if (starting != null)
            {
                if (deadline <= DateTime.UtcNow) throw new InvalidOperationException("A abertura anterior não foi confirmada. Cancele a abertura no launcher e reinicie o auxiliar antes de tentar novamente.");
                if (starting == id) return; throw new InvalidOperationException("Aguarde o jogo que está abrindo.");
            }
            if (catalog.Any(g => processes.Any(p => LaunchChoices.Matches(g, p))))
                throw new InvalidOperationException("Encerre o jogo atual antes de abrir outro.");
            if (!File.Exists(game.ProcessPath)) throw new InvalidOperationException("Executável indisponível. Conclua a preparação do jogo.");
            starting = id; startingChoice = selectedChoice; deadline = DateTime.UtcNow.AddSeconds(90);
            try { environment.Prepare(mode); platform.TargetDisplay(game); idleSince = null; platform.Launch(game, mode); switchUntil = default;
                try { preferences.Save(game, selectedChoice); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { /* A launched game must not be rolled back for an optional preference. */ } }
            catch { starting = null; switchUntil = default; environment.Restore(); throw; }
        }
        finally { gate.Release(); }
    }
    public async Task Tick(bool maintainPresentation = true)
    {
        await gate.WaitAsync();
        try
        {
            var processes = platform.Processes();
            ExpireOpening(processes);
            if (maintainPresentation) platform.PlaceWindows(processes);
            bool running = catalog.Any(g => processes.Any(p => LaunchChoices.Matches(g, p)));
            if (starting != null && processes.Any(p => LaunchChoices.Matches(catalog.Single(g => g.Id == starting), p))) starting = null;
            if (maintainPresentation) environment.Tick();
            if (DateTime.UtcNow < switchUntil) { idleSince = null; return; }
            if (running || starting != null) { idleSince = null; return; }
            foreach (var game in catalog) platform.CancelTarget(game);
            idleSince ??= DateTime.UtcNow;
            // Pequena tolerância evita renegociar o HDMI ao trocar imediatamente de jogo.
            if (DateTime.UtcNow - idleSince >= TimeSpan.FromSeconds(5)) environment.Restore();
        }
        finally { gate.Release(); }
    }
    public async Task Stop(string id, bool force, string? nextGame = null, string? mode = null)
    {
        await gate.WaitAsync();
        try
        {
            var game = catalog.SingleOrDefault(g => g.Id == id) ?? throw new InvalidOperationException("Jogo desconhecido.");
            if (nextGame != null) {
                var next = catalog.SingleOrDefault(g => g.Id == nextGame) ?? throw new InvalidOperationException("Próximo jogo desconhecido.");
                if (next.Id == id || !File.Exists(next.ProcessPath)) throw new InvalidOperationException("O próximo jogo não está disponível.");
                var state = System.Text.Json.JsonSerializer.SerializeToElement(environment.Status);
                if (state.TryGetProperty("mode", out var currentMode) && currentMode.GetString() != mode)
                    throw new InvalidOperationException("Para trocar de jogo nesta sessão, mantenha o mesmo modo Cabo/Stream.");
            }
            var found = platform.Processes().Where(p => LaunchChoices.Matches(game, p)).ToList();
            if (found.Count == 0 && starting == id)
                throw new InvalidOperationException("A abertura ainda não foi confirmada. Cancele pelo launcher no PC antes de reiniciar o auxiliar.");
            foreach (var p in found)
            {
                if (force) platform.ForceClose(p);
                else if (!platform.RequestClose(p)) throw new InvalidOperationException("O jogo não aceitou fechar pela janela. Saia pelo menu do jogo ou confirme o encerramento forçado.");
            }
            if (starting == id) starting = null;
            if (nextGame != null) switchUntil = DateTime.UtcNow.AddSeconds(60);
        }
        finally { gate.Release(); }
    }
}

// Simulação explícita e isolada para testes: nunca executa ou encerra um processo.
public sealed class SimulatedGames : IGamePlatform
{
    public readonly List<Running> Moved = [];
    public bool MoveAccepted = true;
    public Task<bool> MoveToTv(Running process, CancellationToken cancellation) { Moved.Add(process); return Task.FromResult(MoveAccepted); }
    public List<string> Targeted = [];
    public void TargetDisplay(Game game) => Targeted.Add(game.Id);
    public readonly List<Running> Active = [];
    public int LaunchCount;
    public bool CloseAccepted;
    public List<Running> Processes() => Active.ToList();
    public string? LastMode;
    public void Launch(Game game, string mode = "cable") { LastMode = mode; LaunchCount++; Active.Add(new(1, game.ProcessPath, game.ProcessArgument)); }
    public bool RequestClose(Running process) { if (CloseAccepted) Active.Remove(process); return CloseAccepted; }
    public void ForceClose(Running process) => Active.Remove(process);
}
