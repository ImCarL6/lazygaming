using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LazyGaming;

// Instalação automática do app LazyGaming numa TV Samsung Tizen, somente pelas ferramentas oficiais
// (SDB/Tizen CLI) e com argumentos fixos. Cada condição tem evidência própria; nenhuma etapa prova a seguinte.
// Nunca: root, reinício da TV, desinstalação automática, outros apps, comandos vindos da página.
public sealed record TvDevice(string Ip, string Model, string Name, bool DeveloperMode, string DeveloperIp, string Id);
public sealed record TizenTools(string Sdb, string Cli, string? JavaHome, string? Profile, string? ProfilesFile, string Root = "", string? PackageManager = null, string? CertificateManager = null);
public sealed record ToolResult(int Code, string Output, bool TimedOut);
public interface IToolRunner { Task<ToolResult> Run(string file, IReadOnlyList<string> arguments, TimeSpan timeout, IDictionary<string, string>? environment, CancellationToken cancel); }

public sealed class TvInstaller
{
    public const string AppId = "LazyGame01.LazyGaming", SdbPort = "26101";
    readonly Func<TizenTools?> locateTools;
    readonly IToolRunner runner;
    readonly Func<string[]> pcAddresses;
    readonly Func<string, CancellationToken, Task<TvDevice?>> probe;
    readonly Func<IEnumerable<string>> candidates;
    readonly Func<string>? invite;
    readonly string bundle, work;
    readonly SemaphoreSlim gate = new(1, 1);
    readonly object sync = new();
    string? failedFingerprint; long retryAfter;
    public string BundledVersion { get; }
    public Status Current { get; private set; } = new("idle", [], null, null, null, null, false, null);

    public sealed record Step(string Id, string State, string Text);
    public sealed record Status(string Phase, Step[] Steps, TvDevice? Tv, string? InstalledVersion, string? Message, string? Help, bool Busy, DateTimeOffset? CheckedAt,
        string? Duid = null, bool CertificateManager = false, bool PackageManager = false);

    public TvInstaller(string bundle, string work, Func<TizenTools?> locateTools, IToolRunner runner, Func<string[]> pcAddresses,
        Func<string, CancellationToken, Task<TvDevice?>> probe, Func<IEnumerable<string>> candidates, Func<string>? invite = null) {
        this.invite = invite; this.bundle = bundle; this.work = work; this.locateTools = locateTools; this.runner = runner; this.pcAddresses = pcAddresses; this.probe = probe; this.candidates = candidates;
        BundledVersion = BundleVersion(bundle) ?? "0.0.0";
    }

    public static string? BundleVersion(string bundle) {
        try { return XDocument.Load(Path.Combine(bundle, "config.xml")).Root?.Attribute("version")?.Value; } catch { return null; }
    }
    public static bool Newer(string candidate, string? installed) =>
        installed == null || Version.TryParse(candidate, out var a) && Version.TryParse(installed, out var b) && a > b;

    // Avalia as condições e instala quando todas forem verdadeiras e a TV não tiver esta versão (ou mais nova).
    // force: tentativa pedida pelo usuário, ignora o intervalo após falha repetida.
    public async Task<Status> Check(bool install, bool force, string? preferredIp, CancellationToken cancel) {
        if (!await gate.WaitAsync(0, cancel)) return Current;
        try {
            var steps = new List<Step>();
            Status Done(string phase, string? message = null, string? help = null, TvDevice? tv = null, string? installed = null) {
                var status = new Status(phase, steps.ToArray(), tv, installed, message, help, false, DateTimeOffset.UtcNow);
                lock (sync) Current = status; return status;
            }
            void Publish(string phase, TvDevice? tv = null) { lock (sync) Current = new(phase, steps.ToArray(), tv, null, null, null, true, DateTimeOffset.UtcNow); }

            Publish("searching");
            var tvs = await Discover(preferredIp, cancel);
            var mine = pcAddresses();
            if (tvs.Count == 0) {
                steps.Add(new("tv", "fail", "Nenhuma TV Samsung encontrada na rede"));
                return Done("tv-not-found", "Ligue a TV e confira se ela e o PC estão na mesma rede.");
            }
            var matching = tvs.Where(t => t.DeveloperMode && mine.Contains(t.DeveloperIp)).ToList();
            var tv = matching.Count == 1 ? matching[0] : tvs.FirstOrDefault(t => t.Ip == preferredIp) ?? (tvs.Count == 1 ? tvs[0] : null);
            if (tv == null) {
                steps.Add(new("tv", "fail", tvs.Count + " TVs Samsung encontradas"));
                return Done("tv-ambiguous", "Ligue o modo desenvolvedor só na TV em que vai jogar, com o endereço deste PC.");
            }
            steps.Add(new("tv", "ok", tv.Name + " (" + tv.Model + ") em " + tv.Ip));
            if (!tv.DeveloperMode) {
                steps.Add(new("developer", "fail", "Modo desenvolvedor desligado"));
                return Done("developer-off", "Na TV: Apps, digite 12345, ligue Developer Mode e informe " + Join(mine) + ".",
                    "Se a TV pedir, reinicie-a você mesmo. O LazyGaming continua tentando sozinho.", tv);
            }
            if (!mine.Contains(tv.DeveloperIp)) {
                steps.Add(new("developer", "fail", "Modo desenvolvedor aponta para " + (tv.DeveloperIp.Length > 0 ? tv.DeveloperIp : "outro endereço")));
                return Done("developer-ip", "Na TV: Apps, digite 12345 e troque o endereço para " + Join(mine) + ".",
                    "A TV só aceita instalação do PC informado no Developer Mode.", tv);
            }
            steps.Add(new("developer", "ok", "Modo desenvolvedor com o endereço deste PC"));
            var tools = locateTools();
            if (tools == null) {
                steps.Add(new("tools", "fail", "Ferramentas Tizen não encontradas neste PC"));
                return Done("tools-missing", "Baixe e instale o Tizen Studio pelo botão abaixo, ou escolha a pasta se ele já estiver instalado.",
                    "Assim que o instalador terminar, o LazyGaming encontra as ferramentas sozinho.", tv);
            }
            steps.Add(new("tools", "ok", "Ferramentas Tizen em " + tools.Root));
            var serial = tv.Ip + ":" + SdbPort;
            var connect = await runner.Run(tools.Sdb, ["connect", serial], TimeSpan.FromSeconds(20), null, cancel);
            if (connect.Code != 0 || !Regex.IsMatch(connect.Output, @"connected to|already connected", RegexOptions.IgnoreCase)) {
                steps.Add(new("access", "fail", "A TV recusou a conexão de desenvolvimento"));
                return Done("sdb-failed", "Confira o endereço no Developer Mode e permita o LazyGaming/SDB no Firewall desta rede.", Trim(connect.Output), tv);
            }
            if (tools.Profile == null) {
                // O certificado Samsung precisa do DUID desta TV: ler pelo SDB para o usuário copiar no Certificate Manager.
                var duid = (await runner.Run(tools.Sdb, ["-s", serial, "shell", "0", "getduid"], TimeSpan.FromSeconds(15), null, cancel)).Output.Trim();
                duid = Regex.IsMatch(duid, @"^[A-Za-z0-9._-]{6,64}$") ? duid : null;
                steps.Add(new("certificate", "fail", "Nenhum certificado Samsung para assinar o app"));
                var hasManager = tools.CertificateManager != null;

                var status = new Status("profile-missing", steps.ToArray(), tv, null,
                    hasManager ? "Abra o Certificate Manager, entre com sua conta Samsung e crie um certificado de TV (Samsung) incluindo o DUID desta TV."
                               : "Instale a Samsung Certificate Extension: no Package Manager, aba Extension SDK, instale Samsung Certificate Extension.",
                    hasManager ? "Use o tipo TV, privilégio Public e a opção Samsung (se ela não aparecer, instale a Samsung Certificate Extension no Package Manager, aba Extension SDK). Ao salvar, o LazyGaming continua sozinho." : "Depois abra o Certificate Manager por aqui.",
                    false, DateTimeOffset.UtcNow, duid, hasManager, tools.PackageManager != null);
                lock (sync) Current = status; return status;
            }
            steps.Add(new("certificate", "ok", "Certificado Samsung (perfil " + tools.Profile + ")"));
            var list = await runner.Run(tools.Sdb, ["-s", serial, "shell", "0", "vd_applist"], TimeSpan.FromSeconds(30), null, cancel);
            if (list.Code != 0 && list.Output.Length == 0) {
                steps.Add(new("access", "fail", "Não foi possível consultar os apps da TV"));
                return Done("sdb-failed", "Tente novamente em instantes.", Trim(list.Output), tv);
            }
            steps.Add(new("access", "ok", "Acesso de desenvolvimento confirmado"));
            var installed = InstalledVersion(list.Output);
            if (!Newer(BundledVersion, installed)) {
                steps.Add(new("app", "ok", "LazyGaming " + installed + " instalado na TV"));
                failedFingerprint = null;
                return Done("installed", null, null, tv, installed);
            }
            steps.Add(new("app", install ? "running" : "pending", installed == null ? "Instalar LazyGaming " + BundledVersion : "Atualizar de " + installed + " para " + BundledVersion));
            if (!install) return Done("ready", null, null, tv, installed);
            var fingerprint = tv.Ip + "|" + installed + "|" + BundledVersion + "|" + tools.Profile;
            if (!force && fingerprint == failedFingerprint && System.Environment.TickCount64 < retryAfter) { steps[^1] = steps[^1] with { State = "fail" }; return Done("install-failed", "A última tentativa falhou. Use Tentar agora depois de corrigir.", null, tv, installed); }
            Publish("installing", tv);
            var result = await Install(tools, serial, cancel);
            if (result.Error != null) {
                failedFingerprint = fingerprint; retryAfter = System.Environment.TickCount64 + 5 * 60_000;
                steps[^1] = steps[^1] with { State = "fail", Text = "A instalação não terminou" };
                return Done("install-failed", result.Error, result.Detail, tv, installed);
            }
            failedFingerprint = null;
            // Só a TV confirma a versão nova: a saída do CLI não basta.
            var after = InstalledVersion((await runner.Run(tools.Sdb, ["-s", serial, "shell", "0", "vd_applist"], TimeSpan.FromSeconds(30), null, cancel)).Output);
            if (after != BundledVersion) { steps[^1] = steps[^1] with { State = "fail", Text = "A TV não confirmou a versão instalada" }; return Done("install-failed", "Tente novamente.", null, tv, after); }
            steps[^1] = steps[^1] with { State = "ok", Text = "LazyGaming " + after + " instalado na TV" };
            await runner.Run(tools.Cli, ["run", "-p", AppId, "-s", serial], TimeSpan.FromSeconds(30), ToolEnvironment(tools), cancel);
            return Done("installed", null, null, tv, after);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { lock (sync) Current = new("error", Current.Steps, Current.Tv, null, "Falha inesperada: " + e.Message, null, false, DateTimeOffset.UtcNow); return Current; }
        finally { gate.Release(); }
    }

    public sealed record InstallResult(string? Error, string? Detail);
    async Task<InstallResult> Install(TizenTools tools, string serial, CancellationToken cancel) {
        var root = Path.Combine(work, "tv-install-" + Guid.NewGuid().ToString("N"));
        var stage = Path.Combine(root, "stage"); var output = Path.Combine(root, "out");
        try {
            var packaged = await Package(tools, stage, output, cancel, invite?.Invoke());
            if (packaged.Error != null) return packaged;
            var env = ToolEnvironment(tools); var name = "LazyGaming.wgt";
            var install = await runner.Run(tools.Cli, ["install", "-n", name, "-s", serial, "--", output], TimeSpan.FromMinutes(4), env, cancel);
            if (install.TimedOut) return new("A instalação demorou demais. Confira a TV e tente novamente.", Trim(install.Output));
            if (Regex.IsMatch(install.Output, "install completed", RegexOptions.IgnoreCase) && !Regex.IsMatch(install.Output, @"fail|error", RegexOptions.IgnoreCase)) return new(null, null);
            return new(ExplainInstall(install.Output), Trim(install.Output));
        }
        finally { try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { } }
    }

    // Monta e assina o WGT com o perfil local (sem tocar na TV). Resultado: output\LazyGaming.wgt.
    public async Task<InstallResult> Package(TizenTools tools, string stage, string output, CancellationToken cancel, string? inviteJson = null) {
        Stage(bundle, stage); Directory.CreateDirectory(output);
        // Convite de pareamento automático: só no pacote instalado por este PC, nunca no repositório/WGT genérico.
        if (inviteJson != null) File.WriteAllText(Path.Combine(stage, "pairing.js"), "window.LG_BOOTSTRAP = " + inviteJson + ";\n");
        var package = await runner.Run(tools.Cli, ["package", "-t", "wgt", "-s", tools.Profile!, "-o", output, "--", stage], TimeSpan.FromMinutes(3), ToolEnvironment(tools), cancel);
        var wgt = Directory.Exists(output) ? Directory.GetFiles(output, "*.wgt").FirstOrDefault() : null;
        if (package.Code != 0 || wgt == null)
            return new(Regex.IsMatch(package.Output, "password|certificate|profile", RegexOptions.IgnoreCase)
                ? "Não foi possível assinar com o perfil " + tools.Profile + ". Confira o perfil no Certificate Manager." : "Não foi possível empacotar o app da TV.", Trim(package.Output));
        if (Path.GetFileName(wgt) != "LazyGaming.wgt") File.Move(wgt, Path.Combine(output, "LazyGaming.wgt"), true);
        return new(null, null);
    }

    public static string ExplainInstall(string output) =>
        Regex.IsMatch(output, @"author|signature|-12\b|certificate", RegexOptions.IgnoreCase)
            ? "A TV recusou a assinatura. O certificado precisa incluir o DUID desta TV e ser o mesmo autor do app já instalado."
        : Regex.IsMatch(output, @"space|storage", RegexOptions.IgnoreCase) ? "Falta espaço na TV."
        : "A TV recusou a instalação.";

    static Dictionary<string, string> ToolEnvironment(TizenTools tools) => tools.JavaHome != null ? new() { ["JAVA_HOME"] = tools.JavaHome } : [];
    static string Join(string[] addresses) => addresses.Length == 0 ? "o endereço deste PC" : string.Join(" ou ", addresses);
    static string Trim(string text) { text = text.Trim(); return text.Length > 600 ? text[^600..] : text; }

    // vd_applist: blocos "--------------chave = valor-------------".
    public static string? InstalledVersion(string applist) {
        string? current = null;
        foreach (Match m in Regex.Matches(applist, @"-+\s*(app_id|app_tizen_id|app_version)\s*=\s*([^\r\n]*?)-{3,}")) {
            var key = m.Groups[1].Value; var value = m.Groups[2].Value.Trim();
            if (key is "app_id" or "app_tizen_id") { if (value == AppId) current = AppId; else if (key == "app_id") current = null; }
            else if (current == AppId && value.Length > 0) return value;
        }
        return null;
    }

    // Copia somente os arquivos do app da TV e confere que todo recurso local do index existe.
    public static readonly string[] Files = ["pairing.js", "index.html","style.css","controller-art.css","notices.js","diagnostics.js","app.js","platform.js","presentation.js","crypto.js","config.xml","stream.js","stream-policy.js","controller-art.js","first-run.js","advanced.js"];
    public static void Stage(string bundle, string stage) {
        Directory.CreateDirectory(stage);
        foreach (var file in Files) File.Copy(Path.Combine(bundle, file), Path.Combine(stage, file));
        foreach (var folder in new[] { "assets", "stream-engine" }) CopyTree(Path.Combine(bundle, folder), Path.Combine(stage, folder));
        foreach (var notice in new[] { "LICENSE", "THIRD_PARTY.md" }) {
            var source = Path.Combine(Path.GetDirectoryName(bundle.TrimEnd('\\', '/'))!, notice);
            if (File.Exists(source)) File.Copy(source, Path.Combine(stage, notice));
        }
        var index = File.ReadAllText(Path.Combine(stage, "index.html"));
        var missing = Regex.Matches(index, "(?:src|href)=\"([^\"$:#]+)\"").Select(m => m.Groups[1].Value)
            .Where(p => !File.Exists(Path.Combine(stage, p.Replace('/', Path.DirectorySeparatorChar)))).Distinct().ToArray();
        if (missing.Length > 0) throw new InvalidOperationException("Pacote da TV incompleto: " + string.Join(", ", missing));
    }
    static void CopyTree(string source, string target) {
        if (!Directory.Exists(source)) throw new InvalidOperationException("Pacote da TV incompleto: " + Path.GetFileName(source));
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(source)) CopyTree(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    async Task<List<TvDevice>> Discover(string? preferredIp, CancellationToken cancel) {
        if (preferredIp != null && await probe(preferredIp, cancel) is { } known) return [known];
        var found = new System.Collections.Concurrent.ConcurrentBag<TvDevice>();
        var queue = new System.Collections.Concurrent.ConcurrentQueue<string>(candidates());
        await Task.WhenAll(Enumerable.Range(0, 48).Select(async _ => {
            while (queue.TryDequeue(out var ip)) if (await probe(ip, cancel) is { } tv) found.Add(tv);
        }));
        return found.OrderBy(t => t.Ip).ToList();
    }

    // API pública de informações da TV (porta 8001): somente leitura, sem pareamento nem comandos.
    public static TvDevice? ParseInfo(string ip, string json) {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("device", out var d)) return null;
        string S(string name) => d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        if (!S("OS").Equals("Tizen", StringComparison.OrdinalIgnoreCase) || !S("type").Contains("Samsung", StringComparison.OrdinalIgnoreCase)) return null;
        var name = WebUtility.HtmlDecode(S("name"));
        return new(ip, S("modelName"), name.Length > 0 ? name : "TV Samsung", S("developerMode") == "1", S("developerIP"), S("id"));
    }
    public static async Task<TvDevice?> Probe(HttpClient http, string ip, CancellationToken cancel) {
        try {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel); timeout.CancelAfter(700);
            return ParseInfo(ip, await http.GetStringAsync("http://" + ip + ":8001/api/v2/", timeout.Token));
        } catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException) { return null; }
    }
    public static IEnumerable<string> Subnets(IEnumerable<IPAddress> local) =>
        local.Where(PrivateNetwork.IsLocal).SelectMany(a => { var b = a.GetAddressBytes(); return Enumerable.Range(1, 254).Where(i => i != b[3]).Select(i => $"{b[0]}.{b[1]}.{b[2]}.{i}"); }).Distinct();

    // Ferramentas: Tizen Studio padrão ou pasta escolhida (inclui layout sdb\data\tools + sdk\tools\ide).
    public static TizenTools? Locate(IEnumerable<string> roots) {
        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r))) {
            string? First(params string[] parts) => parts.Select(p => Path.Combine(root, p)).FirstOrDefault(File.Exists);
            var sdb = First(@"tools\sdb.exe", @"sdb\data\tools\sdb.exe", @"sdk\tools\sdb.exe");
            var cli = First(@"tools\ide\bin\tizen.bat", @"sdk\tools\ide\bin\tizen.bat");
            if (sdb == null || cli == null) continue;
            var java = new[] { Path.Combine(root, "jdk"), Path.Combine(root, @"sdk\jdk") }.FirstOrDefault(Directory.Exists);
            var parent = Path.GetDirectoryName(root.TrimEnd('\\'))!;
            var profiles = new[] { Path.Combine(root, @"sdk-data\profile\profiles.xml"), Path.Combine(parent, @"tizen-studio-data\profile\profiles.xml"),
                Path.Combine(root + "-data", @"profile\profiles.xml") }.FirstOrDefault(File.Exists);
            return new(sdb, cli, java, profiles == null ? null : ActiveProfile(File.ReadAllText(profiles)), profiles, Path.GetFullPath(root),
                First(@"package-manager\package-manager.exe", @"sdk\package-manager\package-manager.exe"),
                First(@"tools\certificate-manager\certificate-manager.exe", @"sdk\tools\certificate-manager\certificate-manager.exe", @"cert-base\data\tools\certificate-manager\certificate-manager.exe"));
        }
        return null;
    }
    public static string? ActiveProfile(string xml) {
        try {
            var doc = XDocument.Parse(xml); var names = doc.Root!.Elements("profile").Select(p => (string?)p.Attribute("name")).Where(n => !string.IsNullOrEmpty(n)).ToList();
            var active = (string?)doc.Root.Attribute("active");
            // Perfil criado pelo próprio LazyGaming tem preferência; senão o ativo do usuário.
            return names.Contains(SamsungCertificates.Profile) ? SamsungCertificates.Profile : active != null && names.Contains(active) ? active : names.FirstOrDefault();
        } catch { return null; }
    }
    public static IEnumerable<string> DefaultRoots(string? chosen) {
        if (chosen != null) yield return chosen;
        if (System.Environment.GetEnvironmentVariable("LAZYGAMING_TIZEN_HOME") is { Length: > 0 } env) yield return env;
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, "tizen-studio");
        foreach (var drive in new[] { "C", "D", "E" }) yield return drive + @":\tizen-studio";
        // Instalações registradas no Windows e ferramentas no PATH.
        foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine }) {
            string[] keys = [];
            try { using var u = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"); keys = u?.GetSubKeyNames() ?? []; } catch { }
            foreach (var name in keys) {
                string? location = null;
                try { using var k = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + name);
                    if ((k?.GetValue("DisplayName") as string)?.Contains("Tizen", StringComparison.OrdinalIgnoreCase) == true) location = k!.GetValue("InstallLocation") as string; } catch { }
                if (!string.IsNullOrWhiteSpace(location)) yield return location;
            }
        }
        // Servidor SDB já em execução (aberto por outra ferramenta) aponta para a instalação.
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("sdb")) {
            string? root = null;
            try { if (process.MainModule?.FileName is { } file) root = RootFromTool(Path.GetDirectoryName(file)!); } catch { } finally { process.Dispose(); }
            if (root != null) yield return root;
        }
        foreach (var dir in (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)) {
            string? root = null;
            try { if (File.Exists(Path.Combine(dir, "sdb.exe")) || File.Exists(Path.Combine(dir, "tizen.bat"))) root = RootFromTool(dir); } catch { }
            if (root != null) yield return root;
        }
    }
    // De ...\tools, ...\tools\ide\bin, ...\sdb\data\tools ou ...\sdk\tools até a raiz que o Locate entende.
    static string? RootFromTool(string folder) {
        var current = new DirectoryInfo(folder);
        for (int i = 0; i < 5 && current != null; i++, current = current.Parent) if (Locate([current.FullName]) != null) return current.FullName;
        return null;
    }

    // Busca limitada (profundidade e tempo) por uma instalação já existente: sdb.exe em pastas do usuário e raízes de disco.
    // Só lê nomes de pastas; não segue links nem entra em pastas de sistema/dependências.
    public static string? Search(IEnumerable<string> starts, int maxDepth, TimeSpan budget) {
        var deadline = System.Environment.TickCount64 + (long)budget.TotalMilliseconds;
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Windows", "$Recycle.Bin", "System Volume Information", "node_modules", ".git", ".nuget", ".gradle",
            ".cache", "Packages", "WindowsApps", "Microsoft", "Temp", "steamapps", "AppData", "SteamLibrary", "Steam", "Games", "Joguetes", "ProgramData", "Recovery", "PerfLogs", "go", ".vscode", ".npm", "pnpm", "site-packages" };
        var queue = new Queue<(string Path, int Depth)>(starts.Where(Directory.Exists).Select(s => (s, 0)));
        while (queue.Count > 0 && System.Environment.TickCount64 < deadline) {
            var (dir, depth) = queue.Dequeue();
            if (File.Exists(Path.Combine(dir, "sdb.exe")) && RootFromTool(dir) is { } found) return found;
            if (depth >= maxDepth) continue;
            IEnumerable<DirectoryInfo> children;
            try { children = new DirectoryInfo(dir).EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden }).ToList(); }
            catch { continue; }
            foreach (var child in children) {
                if (skip.Contains(child.Name) || child.Name.StartsWith('.')) continue;
                // Pasta com "tizen" no nome: testar na hora (instalações costumam se chamar tizen-studio, tizen, Tizen SDK…).
                if (child.Name.Contains("tizen", StringComparison.OrdinalIgnoreCase) && Locate([child.FullName]) != null) return child.FullName;
                queue.Enqueue((child.FullName, depth + 1));
            }
        }
        return null;
    }
    public static IEnumerable<string> SearchStarts() {
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        yield return home;
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady)) yield return drive.RootDirectory.FullName;
    }
}

public sealed class ProcessToolRunner : IToolRunner
{
    public async Task<ToolResult> Run(string file, IReadOnlyList<string> arguments, TimeSpan timeout, IDictionary<string, string>? environment, CancellationToken cancel) {
        // .bat precisa de cmd; argumentos são fixos do próprio Host (nunca vindos da página) e sem metacaracteres.
        foreach (var a in arguments) if (a.IndexOfAny(['&', '|', '<', '>', '^', '"', '%']) >= 0) throw new ArgumentException("Argumento inválido.");
        var batch = file.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
        var info = new ProcessStartInfo(batch ? System.Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe" : file) {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        if (batch) { info.ArgumentList.Add("/d"); info.ArgumentList.Add("/c"); info.ArgumentList.Add("call"); info.ArgumentList.Add(file); }
        foreach (var a in arguments) info.ArgumentList.Add(a);
        if (environment != null) foreach (var (k, v) in environment) info.Environment[k] = v;
        using var process = Process.Start(info)!;
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel); limit.CancelAfter(timeout);
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException) {
            try { process.Kill(true); } catch { }
            if (cancel.IsCancellationRequested) throw;
            lock (output) return new(-1, output.ToString(), true);
        }
        lock (output) return new(process.ExitCode, output.ToString(), false);
    }
}

// Download do instalador oficial do Tizen Studio (CLI) só quando o usuário pede. Verifica origem fixa HTTPS, tamanho exato
// e assinatura digital válida do Windows antes de abrir; quem instala e aceita a licença é o próprio usuário no instalador.
public sealed class TizenStudioDownload(HttpClient http, string folder)
{
    public const string Url = "https://download.tizen.org/sdk/Installer/tizen-studio_6.1/web-cli_Tizen_Studio_6.1_windows-64.exe";
    public const long Size = 295377744;
    public sealed record State(string Phase, long Received, long Total, string? Error);
    public State Current { get; private set; } = new("idle", 0, Size, null);
    int running;
    public string File => Path.Combine(folder, "web-cli_Tizen_Studio_6.1_windows-64.exe");
    // Pasta padrão sem espaços (o instalador não aceita espaços no destino).
    public static string InstallPath() {
        var home = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "tizen-studio");
        return home.Contains(' ') ? @"C:\tizen-studio" : home;
    }
    // Baixa, verifica e instala sem telas (--accept-license); o Windows pede a confirmação de administrador.
    // install devolve o código de saída do instalador; tools confirma que as ferramentas apareceram.
    public bool StartInstall(Func<string, bool> verify, Func<string, Task<int>> install, Func<bool> tools, CancellationToken cancel) =>
        Start(verify, file => {
            Current = new("installing", Size, Size, null);
            int code; try { code = install(file).GetAwaiter().GetResult(); }
            catch (System.ComponentModel.Win32Exception) { Current = new("install-failed", Size, Size, "A instalação foi cancelada na confirmação do Windows."); return; }
            Current = code == 0 && tools() ? new("installed", Size, Size, null)
                : new("install-failed", Size, Size, "O instalador terminou sem concluir (código " + code + "). Use Abrir instalador para instalar pela tela dele.");
        }, cancel);
    public bool Start(Func<string, bool> verify, Action<string> open, CancellationToken cancel) {
        if (Interlocked.Exchange(ref running, 1) == 1) return false;
        _ = Task.Run(async () => {
            try {
                Directory.CreateDirectory(folder);
                if (!(System.IO.File.Exists(File) && new FileInfo(File).Length == Size && verify(File))) {
                    var partial = File + ".part";
                    Current = new("downloading", 0, Size, null);
                    using (var response = await http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, cancel)) {
                        response.EnsureSuccessStatusCode();
                        await using var source = await response.Content.ReadAsStreamAsync(cancel);
                        await using var target = System.IO.File.Create(partial);
                        var buffer = new byte[1 << 16]; long total = 0; int read;
                        while ((read = await source.ReadAsync(buffer, cancel)) > 0) {
                            await target.WriteAsync(buffer.AsMemory(0, read), cancel); total += read;
                            if (total > Size) throw new InvalidOperationException("O download ficou maior que o esperado.");
                            Current = Current with { Received = total };
                        }
                    }
                    if (new FileInfo(partial).Length != Size) throw new InvalidOperationException("Download incompleto. Tente de novo.");
                    System.IO.File.Move(partial, File, true);
                    Current = new("verifying", Size, Size, null);
                    if (!verify(File)) { System.IO.File.Delete(File); throw new InvalidOperationException("A assinatura digital do instalador não confere. O arquivo foi descartado."); }
                }
                Current = new("opened", Size, Size, null);
                open(File);
            }
            catch (OperationCanceledException) { Current = new("idle", 0, Size, null); }
            catch (Exception e) { Current = new("failed", Current.Received, Size, e is HttpRequestException ? "Não foi possível baixar. Confira a internet e tente de novo." : e.Message); }
            finally { Interlocked.Exchange(ref running, 0); }
        }, cancel);
        return true;
    }
}

public static class Authenticode
{
    // WinVerifyTrust: assinatura íntegra e cadeia confiável; o assinante precisa ser Samsung.
    public static bool SignedBySamsung(string file) {
        try {
            if (WinVerifyTrust(file) != 0) return false;
#pragma warning disable SYSLIB0057
            using var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(file));
#pragma warning restore SYSLIB0057
            return cert.Subject.Contains("Samsung", StringComparison.OrdinalIgnoreCase);
        } catch { return false; }
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    struct FileInfoData { public int cbStruct; public string pcwszFilePath; public nint hFile; public nint pgKnownSubject; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct TrustData { public int cbStruct; public nint pPolicyCallbackData; public nint pSIPClientData; public int dwUIChoice; public int fdwRevocationChecks; public int dwUnionChoice;
        public nint pFile; public int dwStateAction; public nint hWVTStateData; public nint pwszURLReference; public int dwProvFlags; public int dwUIContext; public nint pSignatureSettings; }
    [System.Runtime.InteropServices.DllImport("wintrust.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int WinVerifyTrust(nint hwnd, ref Guid action, ref TrustData data);
    static int WinVerifyTrust(string file) {
        var info = new FileInfoData { cbStruct = System.Runtime.InteropServices.Marshal.SizeOf<FileInfoData>(), pcwszFilePath = file };
        var pInfo = System.Runtime.InteropServices.Marshal.AllocHGlobal(info.cbStruct);
        try {
            System.Runtime.InteropServices.Marshal.StructureToPtr(info, pInfo, false);
            var data = new TrustData { cbStruct = System.Runtime.InteropServices.Marshal.SizeOf<TrustData>(), dwUIChoice = 2, fdwRevocationChecks = 0, dwUnionChoice = 1, pFile = pInfo, dwStateAction = 1 };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            var result = WinVerifyTrust(-1, ref action, ref data);
            data.dwStateAction = 2; WinVerifyTrust(-1, ref action, ref data);
            return result;
        } finally { System.Runtime.InteropServices.Marshal.DestroyStructure<FileInfoData>(pInfo); System.Runtime.InteropServices.Marshal.FreeHGlobal(pInfo); }
    }
}
