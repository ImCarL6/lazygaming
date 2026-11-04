using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LazyGaming;

// Relatórios locais e limitados. Não copia configurações, pareamentos, dumps de
// memória, corpos HTTP ou comandos dos jogos. Falha no diagnóstico nunca derruba o app.
public sealed class RuntimeDiagnostics : IDisposable
{
    const long LogLimit = 1_048_576;
    static readonly string[] ReportFiles = ["session.json", "summary.json", "system.json", "events.jsonl", "events.previous.jsonl", "windows-crashes.json"];
    static readonly Regex RunName = new(@"^run-\d{8}-\d{6}-\d+-[a-f0-9]{8}$");
    readonly object gate = new();
    readonly Dictionary<string, (int Count, DateTime Last)> issues = [];
    readonly Dictionary<string, string> states = [];
    readonly Dictionary<string, string> pending = [];
    readonly string data, marker;
    readonly DateTime started = DateTime.UtcNow;
    readonly System.Threading.Timer exportTimer;
    bool available, disposed, completed, fatal, attached, exportQueued;
    DateTime lastExport;
    int issueCount;
    public static RuntimeDiagnostics? Current { get; private set; }
    public string Root { get; }
    public string RunDirectory { get; }
    public string RunId { get; } = "run-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8];
    public int IssueCount { get { lock (gate) return issueCount; } }
    public bool Available => available;
    public RuntimeDiagnostics(string data, int keepRuns = 10) {
        this.data = data; Root = Path.Combine(data, "diagnostics"); marker = Path.Combine(Root, "current-run.json"); RunDirectory = Path.Combine(Root, RunId);
        exportTimer = new(_ => { lock (gate) { exportQueued = false; ExportCore(); } });
        try {
            Directory.CreateDirectory(Root); if (Reparse(Root) || File.Exists(marker) && Reparse(marker)) throw new IOException("Pasta de relatórios ligada a outro destino.");
            try { Prune(Math.Clamp(keepRuns, 2, 10)); } catch { /* Relatório antigo aberto não impede a coleta nova. */ }
            Directory.CreateDirectory(RunDirectory); available = true;
            RecoverPrevious();
            Write("session.json", new { format = 1, run = RunId, version = Version, build = Build, started, pid = Environment.ProcessId });
            SaveState("running"); Record("lifecycle", "started", false);
        } catch { available = false; }
    }
    public static string Version => typeof(RuntimeDiagnostics).Assembly.GetName().Version?.ToString(3) ?? "unknown";
    public static string Build => typeof(RuntimeDiagnostics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? Version;
    public static void Report(string component, string code, Exception? error = null, object? detail = null) => Current?.Record(component, code, true, error, detail);
    public static void Trace(string component, string code, object? detail = null) => Current?.Record(component, code, false, null, detail);
    public static void State(string component, string? problem) {
        var current = Current; if (current == null) return;
        lock (current.gate) {
            var value = problem ?? "";
            if (current.states.GetValueOrDefault(component, "") == value) return;
            current.states[component] = value;
            current.Record(component, value.Length == 0 ? "recovered" : "problem", value.Length > 0, detail: new { message = value });
        }
    }
    public void Attach() {
        Current = this; attached = true;
        AppDomain.CurrentDomain.UnhandledException += Unhandled;
        TaskScheduler.UnobservedTaskException += Unobserved;
        System.Windows.Forms.Application.ThreadException += UiException;
    }
    void Unhandled(object sender, UnhandledExceptionEventArgs e) { fatal = true; Record("process", "unhandled-exception", true, e.ExceptionObject as Exception); }
    void Unobserved(object? sender, UnobservedTaskExceptionEventArgs e) => Record("task", "unobserved-exception", true, e.Exception);
    void UiException(object sender, ThreadExceptionEventArgs e) => Record("ui", "thread-exception", true, e.Exception);
    public static string Clean(string? value) {
        if (value == null) return "";
        var text = value.Length > 8000 ? value[..8000] : value;
        foreach (var path in new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), AppContext.BaseDirectory })
            if (!string.IsNullOrEmpty(path)) text = text.Replace(path.TrimEnd('\\', '/'), "[pasta local]", StringComparison.OrdinalIgnoreCase);
        text = Regex.Replace(text, @"(?i)(?:[A-Z]:[\\/]|\\\\)[^\r\n\""<>|]*", "[caminho local]");
        text = Regex.Replace(text, @"https?://[^\s)\""<>]+", "[endereço]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(?i)\b(?:authorization|bearer|access[_-]?token|token|password|senha|secret|cookie|invite|nonce|signature|key)\b[\""'\s]*[:= ]+[\""']?[^\s,;\""'}]+", "[credencial omitida]");
        text = Regex.Replace(text, @"\b[a-fA-F0-9]{32,}\b", "[identificador omitido]");
        text = Regex.Replace(text, @"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}", "[email omitido]");
        return text.Length > 1500 ? text[..1500] : text;
    }
    static object? Safe(object? value, int depth = 0) {
        if (value == null || depth > 4) return null;
        var json = value is JsonElement element ? element : JsonSerializer.SerializeToElement(value);
        return json.ValueKind switch {
            JsonValueKind.Object => json.EnumerateObject().Take(24).Where(p => !Regex.IsMatch(p.Name, "token|password|senha|secret|cookie|invite|nonce|signature|authorization|^key$|^body$|commandline", RegexOptions.IgnoreCase))
                .ToDictionary(p => Clean(p.Name), p => Safe(p.Value, depth + 1)),
            JsonValueKind.Array => json.EnumerateArray().Take(20).Select(x => Safe(x, depth + 1)).ToArray(),
            JsonValueKind.String => Clean(json.GetString()),
            JsonValueKind.Number => json.TryGetInt64(out var integer) ? (object)integer : json.GetDouble(),
            JsonValueKind.True => true, JsonValueKind.False => false, _ => null
        };
    }
    public void Record(string component, string code, bool problem, Exception? error = null, object? detail = null) {
        lock (gate) {
            if (!available || disposed) return;
            try {
                component = Clean(component); code = Clean(code);
                var key = component + ":" + code + ":" + error?.GetType().Name;
                var now = DateTime.UtcNow; bool repeat = false;
                if (problem) {
                    issueCount++;
                    var prior = issues.GetValueOrDefault(key);
                    repeat = prior.Count > 0 && now - prior.Last < TimeSpan.FromSeconds(30);
                    if (issues.Count >= 64 && !issues.ContainsKey(key)) issues.Remove(issues.MinBy(p => p.Value.Last).Key);
                    issues[key] = (prior.Count + 1, repeat ? prior.Last : now);
                }
                if (!repeat) {
                    var record = new { at = now, component, code, level = problem ? "warning" : "info",
                        error = error == null ? null : new { type = error.GetType().FullName, hresult = $"0x{error.HResult:X8}", message = Clean(error.Message), stack = Clean(error.StackTrace), innerType = error.InnerException?.GetType().FullName, innerMessage = Clean(error.InnerException?.Message) }, detail = Safe(detail) };
                    var log = Path.Combine(RunDirectory, "events.jsonl");
                    if (File.Exists(log) && new FileInfo(log).Length >= LogLimit) File.Move(log, Path.Combine(RunDirectory, "events.previous.jsonl"), true);
                    File.AppendAllText(log, JsonSerializer.Serialize(record) + "\n", new UTF8Encoding(false));
                    SaveState(completed ? "completed" : "running");
                }
                if (problem && !exportQueued && now - lastExport >= TimeSpan.FromSeconds(30)) { exportQueued = true; exportTimer.Change(1500, Timeout.Infinite); }
            } catch { /* Disco cheio/permissões não mudam o fluxo do produto. */ }
        }
    }
    public string? Begin(string name) {
        lock (gate) {
            if (!available || disposed || pending.Count >= 32) return null;
            var id = Guid.NewGuid().ToString("N")[..8]; pending[id] = Clean(name);
            Record("operation", "begin", false, detail: new { operation = name, id }); return id;
        }
    }
    public void End(string? id, bool success, int status) {
        lock (gate) {
            if (id == null || !pending.Remove(id, out var name)) return;
            Record("operation", success ? "completed" : "failed", !success, detail: new { operation = name, id, status });
        }
    }
    void Write(string name, object value) => DurableJson.Write(Path.Combine(RunDirectory, name), value);
    void SaveState(string state) {
        var summary = new { format = 1, run = RunId, version = Version, build = Build, started, updated = DateTime.UtcNow, state,
            issueCount, issues = issues.Select(p => new { category = p.Key, count = p.Value.Count, last = p.Value.Last }).ToArray(),
            pending = pending.Values.ToArray(), sessionPending = File.Exists(Path.Combine(data, "session.json")), displayRecoveryPending = File.Exists(Path.Combine(data, "display-verification.json")) };
        Write("summary.json", summary); DurableJson.Write(marker, summary);
    }
    void RecoverPrevious() {
        if (!File.Exists(marker)) return;
        try {
            if (new FileInfo(marker).Length > 65_536) return;
            using var json = JsonDocument.Parse(File.ReadAllText(marker)); var old = json.RootElement;
            if (old.GetProperty("state").GetString() != "running") return;
            var id = old.GetProperty("run").GetString() ?? "";
            if (!RunName.IsMatch(id)) return;
            var path = Path.Combine(Root, id); if (!Directory.Exists(path) || Reparse(path)) return;
            var interrupted = new { format = 1, run = id, state = "interrupted", detectedAt = DateTime.UtcNow, previous = Safe(old),
                reason = "A execução anterior não registrou encerramento normal. Pode ser crash, encerramento forçado ou desligamento do PC; a causa não foi confirmada." };
            DurableJson.Write(Path.Combine(path, "summary.json"), interrupted); ExportDirectory(path);
            Record("lifecycle", "previous-run-interrupted", true, detail: new { previousRun = id, pending = old.TryGetProperty("pending", out var p) ? Safe(p) : null });
        } catch { }
    }
    public void CaptureSystem() {
        try {
            var gpus = new List<object>();
            using (var adapters = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                foreach (var name in adapters?.GetSubKeyNames().Where(n => Regex.IsMatch(n, @"^\d{4}$")).Take(8) ?? []) {
                    using var adapter = adapters!.OpenSubKey(name);
                    gpus.Add(new { name = Clean(adapter?.GetValue("DriverDesc")?.ToString()), driver = Clean(adapter?.GetValue("DriverVersion")?.ToString()), provider = Clean(adapter?.GetValue("ProviderName")?.ToString()) });
                }
            object? displays = null;
            object? activeModes = null;
            try { displays = Displays.List().Select(d => new { name = Clean(d.Name), d.Technology, d.Available, d.Active, preferredWidth = d.Width, preferredHeight = d.Height, d.Hz }).ToArray();
                activeModes = Displays.DesktopLayout().Select(d => new { d.Bounds.X, d.Bounds.Y, d.Bounds.Width, d.Bounds.Height }).ToArray();
            } catch (Exception e) { Record("system", "display-inventory-failed", true, e); }
            lock (gate) if (available && !disposed) Write("system.json", new { version = Version, build = Build, os = RuntimeInformation.OSDescription, osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(), runtime = RuntimeInformation.FrameworkDescription, processors = Environment.ProcessorCount,
                memoryAvailableMiB = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1_048_576, culture = CultureInfo.CurrentCulture.Name, gpus, displays, activeModes });
        } catch (Exception e) { Record("system", "inventory-failed", true, e); }
    }
    public void Context(string code, object value) => Record("context", code, false, detail: value);
    public async Task ImportWindowsCrashes(IEnumerable<string>? gameExecutables = null) {
        try {
            var tool = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "wevtutil.exe");
            if (!File.Exists(tool)) return;
            var info = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "qe", "Application", "/q:*[System[(EventID=1000 or EventID=1001 or EventID=1026) and TimeCreated[timediff(@SystemTime)<=604800000]]]", "/c:120", "/rd:true", "/f:xml" }) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { try { process.Kill(); } catch { } return; }
            var xml = await output; _ = await errors;
            if (process.ExitCode != 0 || xml.Length > 2_000_000) return;
            var events = ParseWindowsCrashes(xml, gameExecutables);
            lock (gate) if (available && !disposed) Write("windows-crashes.json", events);
            foreach (var crash in events) Record("windows", "historical-crash-record", true, detail: crash);
        } catch (Exception e) { Record("windows", "crash-records-unavailable", false, detail: new { type = e.GetType().Name }); }
    }
    public static object[] ParseWindowsCrashes(string xml, IEnumerable<string>? gameExecutables = null) {
        var document = System.Xml.Linq.XDocument.Parse("<events>" + xml + "</events>");
        var names = new[] { "LazyGaming.Host.exe" }.Concat(gameExecutables ?? []).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase).Take(65).ToArray();
        return document.Root!.Elements().Where(e => e.Name.LocalName == "Event" && names.Any(n => e.Value.Contains(n, StringComparison.OrdinalIgnoreCase))).Take(20).Select(e => {
            var fields = e.Descendants().Where(n => n.Name.LocalName == "Data").ToArray();
            string? Field(string name) => fields.FirstOrDefault(f => (string?)f.Attribute("Name") == name)?.Value;
            var system = e.Elements().FirstOrDefault(n => n.Name.LocalName == "System");
            return (object)new { app = Clean(names.First(n => e.Value.Contains(n, StringComparison.OrdinalIgnoreCase))), historical = true, eventId = system?.Elements().FirstOrDefault(n => n.Name.LocalName == "EventID")?.Value,
                at = (string?)system?.Elements().FirstOrDefault(n => n.Name.LocalName == "TimeCreated")?.Attribute("SystemTime"),
                record = system?.Elements().FirstOrDefault(n => n.Name.LocalName == "EventRecordID")?.Value,
                appVersion = Clean(Field("AppVersion")), module = Clean(Field("ModuleName")), moduleVersion = Clean(Field("ModuleVersion")),
                exceptionCode = Clean(Field("ExceptionCode")), offset = Clean(Field("FaultingOffset")),
                // Evento .NET 1026 guarda texto livre: preservar só categorias, sem mensagem/dados de aplicação.
                managed = fields.Any(f => f.Value.Contains("Exception", StringComparison.Ordinal)) };
        }).ToArray();
    }
    public string? Export() { lock (gate) return ExportCore(); }
    string? ExportCore() {
        if (!available || disposed) return null;
        try { lastExport = DateTime.UtcNow; return ExportDirectory(RunDirectory); } catch { return null; }
    }
    string ExportDirectory(string directory) {
        var reports = Path.Combine(Root, "reports"); Directory.CreateDirectory(reports);
        if (Reparse(reports)) throw new IOException("Pasta de relatórios ligada a outro destino.");
        var path = Path.Combine(reports, "LazyGaming-" + Path.GetFileName(directory) + ".zip");
        using (var file = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            foreach (var name in ReportFiles) {
                var source = Path.Combine(directory, name);
                if (!File.Exists(source) || Reparse(source) || new FileInfo(source).Length > LogLimit + 32_768) continue;
                zip.CreateEntryFromFile(source, name, CompressionLevel.Fastest);
            }
        // O instalador pode falhar antes de o Host abrir. Aqui só há etapa e códigos.
        using (var zip = ZipFile.Open(path + ".tmp", ZipArchiveMode.Update))
            foreach (var name in new[] { "installer.jsonl", "installer.jsonl.previous" }) {
                var source = Path.Combine(Root, name);
                if (File.Exists(source) && !Reparse(source) && new FileInfo(source).Length < 70_000) zip.CreateEntryFromFile(source, name, CompressionLevel.Fastest);
            }
        File.Move(path + ".tmp", path, true); return path;
    }
    static bool Reparse(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    void Prune(int keep) {
        var directories = Directory.GetDirectories(Root).Where(p => RunName.IsMatch(Path.GetFileName(p)) && !Reparse(p)).OrderByDescending(Directory.GetLastWriteTimeUtc).ToArray();
        foreach (var path in directories.Skip(keep - 1)) {
            var full = Path.GetFullPath(path);
            if (!full.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Directory.GetDirectories(full).Length > 0) continue;
            var report = Path.Combine(Root, "reports", "LazyGaming-" + Path.GetFileName(path) + ".zip");
            Directory.Delete(full, true); if (File.Exists(report) && !Reparse(report)) File.Delete(report);
        }
        var reportRoot = Path.Combine(Root, "reports");
        if (Directory.Exists(reportRoot) && !Reparse(reportRoot))
            foreach (var report in Directory.GetFiles(reportRoot, "LazyGaming-run-*.zip").OrderByDescending(File.GetLastWriteTimeUtc).Skip(keep - 1)) {
                if (RunName.IsMatch(Path.GetFileNameWithoutExtension(report)["LazyGaming-".Length..]) && !Reparse(report)) File.Delete(report);
            }
    }
    public void Complete() { lock (gate) { if (!available || disposed) return; completed = true; Record("lifecycle", "stopped", false); } }
    public void Dispose() {
        lock (gate) {
            if (disposed) return;
            try {
                if (available) { if (!completed) Record("lifecycle", "incomplete-exit", true, detail: new { fatal }); SaveState(completed && !fatal ? "completed" : "interrupted"); if (issueCount > 0) ExportCore(); }
            } catch { }
            disposed = true; exportTimer.Dispose();
            if (attached) { AppDomain.CurrentDomain.UnhandledException -= Unhandled; TaskScheduler.UnobservedTaskException -= Unobserved; System.Windows.Forms.Application.ThreadException -= UiException; }
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }
}
