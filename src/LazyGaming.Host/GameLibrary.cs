using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LazyGaming;

public sealed class GameLibrary
{
    public List<Game> Games { get; private set; } = [];
    public List<Game> Discovered { get; private set; } = [];
    public DateTime? LastScan { get; private set; }
    public LibrarySettings Settings { get; }
    public string SteamPath { get; }
    public string Directory { get; }
    public List<string> Notes { get; private set; } = [];
    public int MetadataCount { get; private set; }
    public Dictionary<string, VdfNode> ArtworkMetadata { get; private set; } = [];
    public GameMetadataStore Metadata { get; }
    readonly string[] localFolders;
    readonly bool discoverSteam;
    public GameLibrary(string directory, bool test = false, string? steamPath = null, IEnumerable<string>? nonSteamFolders = null, bool discoverChildren = false)
    {
        Directory = directory;
        Metadata = new(directory);
        Settings = new(directory);
        // Um filho por instalação; reavaliado a cada busca para descobrir jogos novos.
        localFolders = nonSteamFolders?.ToArray() ?? [@"C:\Joguetes"];
        scanChildren = nonSteamFolders == null || discoverChildren;
        discoverSteam = !test && steamPath == null;
        SteamPath = steamPath != null ? Path.GetFullPath(steamPath) : test ? Path.Combine(directory, "simulated-steam") : FindSteam();
        if (test) {
            var fixtures = Path.Combine(directory, "simulated-games"); System.IO.Directory.CreateDirectory(fixtures);
            foreach (var item in new[] { ("wilds", "Monster Hunter Wilds"), ("mycopunk", "Mycopunk") }) {
                var executable = Path.Combine(fixtures, item.Item1 + ".exe"); File.WriteAllText(executable, "Simulation fixture; never executed.");
                Games.Add(new(item.Item1, item.Item2, executable, executable));
            }
        } else Refresh();
    }
    readonly bool scanChildren;
    public static string? ChooseSteam(IEnumerable<string?> candidates) => candidates.Where(p => !string.IsNullOrWhiteSpace(p))
        .Select(p => Path.GetFullPath(p!)).FirstOrDefault(p => File.Exists(Path.Combine(p, "steam.exe")) && File.Exists(Path.Combine(p, "steamapps", "libraryfolders.vdf")));
    static string FindSteam()
    {
        using var user = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        using var machine = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
        using var machine64 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return ChooseSteam(new[] { user?.GetValue("SteamPath") as string, machine?.GetValue("InstallPath") as string, machine64?.GetValue("InstallPath") as string, fallback }.Concat(CommonSteamRoots())) ?? fallback;
    }
    // Caminhos mais comuns de instalação/biblioteca Steam em todos os discos fixos (sem varredura profunda).
    static readonly string[] SteamNames = ["Steam", "SteamLibrary", "Steam Library", "SteamGames", @"Program Files (x86)\Steam", @"Program Files\Steam", @"Games\Steam", @"Games\SteamLibrary", @"Jogos\Steam", @"Jogos\SteamLibrary", @"Programas\Steam"];
    public static IEnumerable<string> CommonSteamRoots() {
        IEnumerable<string> drives;
        try { drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName).ToArray(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { yield break; }
        foreach (var drive in drives) foreach (var name in SteamNames) yield return Path.Combine(drive, name);
    }
    // Biblioteca Steam a partir de uma pasta escolhida: a raiz, a própria steamapps ou steamapps\common.
    public static string? SteamLibraryOf(string folder) {
        try {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            foreach (var candidate in new[] { full, Path.GetDirectoryName(full), Path.GetDirectoryName(Path.GetDirectoryName(full) ?? "") })
                if (!string.IsNullOrEmpty(candidate) && System.IO.Directory.Exists(Path.Combine(candidate, "steamapps")) && System.IO.Directory.EnumerateFiles(Path.Combine(candidate, "steamapps"), "appmanifest_*.acf").Any()) return candidate;
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return null;
    }
    static string Compact(string text) => Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]", "");
    public static bool IsHelper(string name) => Regex.IsMatch(name, "crash|unins|setup|install|server|anticheat|redist|dxsetup|webhelper|unitycrash|vc_redist|_xlate(?:\\.exe)?$|shader.*compil", RegexOptions.IgnoreCase);
    public static string LocalId(string folder) => "local-" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant())))[..12];
    public static string IdentifyLocalTitle(string folder, string executable)
    {
        var name = Path.GetFileNameWithoutExtension(executable);
        var unity = Path.Combine(Path.GetDirectoryName(executable)!, name + "_Data", "app.info");
        if (File.Exists(unity))
        {
            var lines = File.ReadAllLines(unity); if (lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1])) return lines[1].Trim();
        }
        try
        {
            var product = FileVersionInfo.GetVersionInfo(executable).ProductName?.Trim();
            if (!string.IsNullOrEmpty(product) && !Regex.IsMatch(product, "^(GSE|Unity|Unreal|Bootstrap|Launcher)", RegexOptions.IgnoreCase)) return product;
        }
        catch { }
        return Path.GetFileName(folder);
    }
    public static string? SafeInside(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
    public static List<string> Exes(string root)
    {
        var result = new List<string>();
        void Scan(string folder, int depth)
        {
            if (depth > 5 || result.Count > 300) return;
            try
            {
                result.AddRange(System.IO.Directory.EnumerateFiles(folder, "*.exe").Where(p => !IsHelper(Path.GetFileName(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0));
                foreach (var child in System.IO.Directory.EnumerateDirectories(folder))
                {
                    var name = Path.GetFileName(child);
                    if (Regex.IsMatch(name, "redist|extras|anticheat|support|prereq|crash|\\.git|node_modules|Engine|dotnet", RegexOptions.IgnoreCase)) continue;
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) Scan(child, depth + 1);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        Scan(root, 0); return result;
    }
    public void Refresh()
    {
        var preferences = Settings.Read();
        var notes = new List<string>(); var installed = new List<(uint Id, string Name, string Folder)>();
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SteamPath };
        var folders = Path.Combine(SteamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(folders)) foreach (var entry in VdfNode.TextFile(File.ReadAllText(folders)).Child("libraryfolders").Values.OfType<VdfNode>())
            if (!string.IsNullOrEmpty(entry.Text("path"))) libraries.Add(Path.GetFullPath(entry.Text("path")));
        if (discoverSteam) foreach (var root in CommonSteamRoots()) if (SteamLibraryOf(root) is string found) libraries.Add(found);
        foreach (var folder in preferences.Folders.Where(f => f.Enabled)) if (SteamLibraryOf(folder.Path) is string chosen) libraries.Add(chosen);
        foreach (var library in libraries)
        {
            var apps = Path.Combine(library, "steamapps"); if (!System.IO.Directory.Exists(apps)) continue;
            foreach (var manifest in System.IO.Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                try
                {
                    var app = VdfNode.TextFile(File.ReadAllText(manifest)).Child("AppState");
                    if (!uint.TryParse(app.Text("appid"), out var id)) continue;
                    var folder = SafeInside(Path.Combine(apps, "common"), app.Text("installdir"));
                    if (folder == null || !System.IO.Directory.Exists(folder)) { notes.Add(app.Text("name") + ": pasta não encontrada"); continue; }
                    if (!uint.TryParse(app.Text("StateFlags"), out uint state) || (state & 4) == 0) { notes.Add(app.Text("name") + ": instalação incompleta"); continue; }
                    installed.Add((id, app.Text("name"), folder));
                }
                catch (Exception e) when (e is IOException or InvalidDataException) { notes.Add(Path.GetFileName(manifest) + ": leitura indisponível"); }
            }
        }
        Dictionary<uint, VdfNode> info;
        try { info = SteamData.Read(Path.Combine(SteamPath, "appcache", "appinfo.vdf"), installed.Select(g => g.Id).ToHashSet()); }
        catch (Exception e) when (e is IOException or ArgumentException or IndexOutOfRangeException)
        { info = []; notes.Add("Cache Steam temporariamente indisponível; identificação pelos arquivos locais."); }
        MetadataCount = info.Values.Count(n => n.Child("common").Count > 0);
        ArtworkMetadata = info.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value.Child("common").Child("library_assets_full"));
        var games = new List<Game>();
        foreach (var item in installed)
        {
            if (!preferences.Steam) continue;
            var data = info.GetValueOrDefault(item.Id) ?? new(); var common = data.Child("common");
            string type = common.Text("type");
            if (item.Id is 228980 or 431960 || (type != "" && !type.Equals("game", StringComparison.OrdinalIgnoreCase) && item.Id != 1281930))
            { notes.Add(item.Name + ": componente/aplicativo fora da biblioteca de jogos"); continue; }
            string name = common.Text("name"); if (name == "") name = item.Name;
            var candidates = Exes(item.Folder);
            var configured = data.Child("config").Child("launch").Values.OfType<VdfNode>()
                .Where(n => n.Child("config").Text("oslist") is "" or "windows")
                .Select(n => SafeInside(item.Folder, n.Text("executable")))
                .Where(p => p != null && File.Exists(p) && Path.GetExtension(p).Equals(".exe", StringComparison.OrdinalIgnoreCase) && !IsHelper(Path.GetFileName(p)))
                .Cast<string>().ToList();
            string? executable = candidates.FirstOrDefault(p => p.EndsWith("-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase))
                ?? configured.FirstOrDefault()
                ?? candidates.OrderByDescending(p => Compact(Path.GetFileNameWithoutExtension(p)) == Compact(name) ? 100 :
                    Compact(name).Contains(Compact(Path.GetFileNameWithoutExtension(p))) ? 40 : 0).ThenBy(p => p.Length).FirstOrDefault();
            string? argument = null;
            if (item.Id == 1281930)
            {
                executable = Path.Combine(item.Folder, "dotnet", "dotnet.exe"); argument = "tModLoader.dll";
                if (!File.Exists(executable) || !File.Exists(Path.Combine(item.Folder, argument))) executable = null;
            }
            if (executable == null) { notes.Add(name + ": executável ainda não identificado"); }
            games.Add(new(item.Id == 2246340 ? "wilds" : "steam-" + item.Id, name, "steam://rungameid/" + item.Id,
                executable ?? "", true, item.Id.ToString(), item.Folder, argument));
        }
        // Não Steam: identificação local pelo executável/pasta; identidade de arte independente do launcher.
        foreach (var registered in new LocalGameStore(Directory).Read(notes)) {
            if (games.Any(g => string.Equals(g.Folder, registered.Folder, StringComparison.OrdinalIgnoreCase) || string.Equals(g.ProcessPath, registered.ProcessPath, StringComparison.OrdinalIgnoreCase))) continue;
            games.Add(registered);
        }
        IEnumerable<string> Folders() {
            var configured = scanChildren ? preferences.Folders.Where(f => f.Enabled).Select(f => f.Path).Concat(localFolders.Where(p => p != @"C:\Joguetes")) : localFolders;
            foreach (var root in configured.Distinct(StringComparer.OrdinalIgnoreCase)) {
                if (!System.IO.Directory.Exists(root)) { notes.Add("Pasta de busca indisponível: " + root); continue; }
                string[] children;
                try { children = scanChildren ? System.IO.Directory.EnumerateDirectories(root).Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0).ToArray() : [root]; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { notes.Add("Não foi possível ler a pasta de busca: " + root); continue; }
                // Include the chosen folder itself when it is an installation.
                if (scanChildren && System.IO.Directory.EnumerateFiles(root, "*.exe").Any(p => !IsHelper(Path.GetFileName(p)))) yield return root;
                foreach (var child in children) yield return child;
            }
        }
        var roots = Folders();
        foreach (var folder in roots.Where(root => (File.GetAttributes(root) & FileAttributes.ReparsePoint) == 0))
        {
            if (Regex.IsMatch(Path.GetFileName(folder), "setup|installer|redist|prodkeys", RegexOptions.IgnoreCase)) continue;
            if (games.Any(g => string.Equals(g.Folder, Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase)
                || g.Folder != null && folder.StartsWith(g.Folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
            var candidates = Exes(folder);
            var process = SelectLocalProcess(folder, candidates);
            if (process == null) { notes.Add(Path.GetFileName(folder) + ": nenhum executável de jogo identificado"); continue; }
            if (games.Any(g => string.Equals(g.ProcessPath, process, StringComparison.OrdinalIgnoreCase))) continue;
            var title = IdentifyLocalTitle(folder, process);
            // Nome curto de projeto Unreal não substitui o título da instalação.
            if (process.EndsWith("-Shipping.exe", StringComparison.OrdinalIgnoreCase)) title = Path.GetFileName(folder);
            var rootLauncher = Path.Combine(folder, Path.GetFileName(folder) + ".exe");
            bool mycopunk = Compact(title) == "mycopunk";
            var id = mycopunk ? "mycopunk" : LocalId(folder);
            var rootExes = candidates.Where(p => string.Equals(Path.GetDirectoryName(p), folder, StringComparison.OrdinalIgnoreCase)).ToArray();
            var launch = File.Exists(rootLauncher) ? rootLauncher : rootExes.Length == 1 ? rootExes[0] : process;
            games.Add(new(id, title, launch, process, false, mycopunk ? "3247750" : null, folder,
                LaunchDirectory: mycopunk ? null : Path.GetDirectoryName(launch)));
        }
        Discovered = games.GroupBy(g => g.Id).Select(g => g.First()).OrderBy(g => g.Id == "wilds" ? 0 : g.Id == "mycopunk" ? 1 : 2).ThenBy(g => g.Name).ToList();
        Games = Discovered.Where(g => !(preferences.Excluded ?? []).Contains(g.Id)).ToList();
        LastScan = DateTime.UtcNow;
        Notes = notes.Distinct().ToList();
    }
    public static string? SelectLocalProcess(string folder, IEnumerable<string> candidates) => candidates
        .OrderByDescending(p => p.EndsWith("-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase) ? 1000 :
            File.Exists(Path.Combine(Path.GetDirectoryName(p)!, Path.GetFileNameWithoutExtension(p) + "_Data", "app.info")) ? 900 :
            // Wrappers no topo têm precedência para abrir, nunca para acompanhar o jogo.
            !string.Equals(Path.GetDirectoryName(p), folder, StringComparison.OrdinalIgnoreCase) ? 500 : 0)
        .ThenByDescending(p => Compact(Path.GetFileNameWithoutExtension(p)) == Compact(Path.GetFileName(folder)))
        .ThenByDescending(p => new FileInfo(p).Length).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    public object Inspect() => new { MetadataCount, ArtworkMetadata, games = Games.Select(g => new { g.Id, name = Metadata.DisplayName(g), g.Launch, g.ProcessPath, g.ProcessArgument, artworkId = Metadata.ArtworkId(g), g.Steam, metadata = Metadata.For(g) }), notes = Notes };
}
