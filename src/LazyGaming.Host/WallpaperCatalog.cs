using System.Drawing;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LazyGaming;

public record Wallpaper(string Id, string Title, string Source, int Width, int Height, string SourceUrl, string? Artist = null, string? License = null)
{
    public string Thumbnail => $"/api/hub/wallpapers/image/{Id}/thumbnail";
    public string Preview => $"/api/hub/wallpapers/image/{Id}/full";
}
public record WallpaperResults(Wallpaper[] Images, string[] Warnings, bool HasMore, bool Cached = false);
public record WallpaperSelection(string Id, string File, string Title, string Source, string SourceUrl, int Width, int Height, string? Artist, string? License);

// Independent of metadata and installations: choosing art cannot change an executable or launch arguments.
public sealed class WallpaperOverrides(string directory)
{
    readonly object gate = new();
    string FilePath => Path.Combine(directory, "library", "wallpapers", "choices.json");
    static Dictionary<string, WallpaperSelection> Load(string path) {
        try { return LibraryJson.Read(path, () => new Dictionary<string, WallpaperSelection>()); }
        catch (Exception e) when (e is IOException or JsonException) { return []; }
    }
    Dictionary<string, WallpaperSelection> entries = Load(Path.Combine(directory, "library", "wallpapers", "choices.json"));
    public WallpaperSelection? For(string gameId) { lock (gate) return entries.GetValueOrDefault(gameId); }
    public string? Image(string gameId) {
        var item = For(gameId);
        if (item == null) return null;
        var path = GameLibrary.SafeInside(Path.Combine(directory, "library", "wallpapers"), item.File);
        return path != null && File.Exists(path) ? path : null;
    }
    public void Save(string gameId, WallpaperSelection? selection) {
        if (!Regex.IsMatch(gameId, "^[a-zA-Z0-9-]{1,80}$")) throw new InvalidOperationException("Jogo inválido.");
        lock (gate) {
            using var held = new LibraryFileLock(FilePath);
            var next = LibraryJson.Read(FilePath, () => new Dictionary<string, WallpaperSelection>());
            if (selection == null) next.Remove(gameId); else next[gameId] = selection;
            LibraryJson.Write(FilePath, next); entries = next;
        }
    }
}

public sealed class WallpaperCatalog
{
    public record Saved(Wallpaper Image, string FullUrl, string ThumbnailUrl);
    record Cached(DateTime At, WallpaperResults Results);
    readonly string root;
    readonly HttpClient http;
    readonly SemaphoreSlim files = new(1, 1), wallhaven = new(1, 1), downloads = new(3, 3);
    readonly SemaphoreSlim[] imageLocks = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    DateTime wallhavenNext;
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    public WallpaperCatalog(string directory, HttpClient? client = null) {
        root = Path.Combine(directory, "library", "wallpapers");
        http = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) };
    }
    static string? Str(JsonElement e, string key) => e.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    static int Num(JsonElement e, string key) => e.TryGetProperty(key, out var p) && p.TryGetInt32(out var n) ? n : 0;
    static JsonElement[] Items(JsonElement e, string key) => e.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray().ToArray() : [];
    static string Clean(string? text) { var s = WebUtility.HtmlDecode(Regex.Replace(text ?? "", "<[^>]+>", " ")).Trim(); return s[..Math.Min(s.Length, 300)]; }
    static bool Failure(Exception e) => e is HttpRequestException or IOException or JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException or OperationCanceledException or System.Runtime.InteropServices.ExternalException;
    public static bool AllowedImage(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https" && u.Port == 443 && u.UserInfo == "" &&
        (u.Host is "w.wallhaven.cc" or "th.wallhaven.cc" or "upload.wikimedia.org" || u.Host == "steamstatic.com" || u.Host.EndsWith(".steamstatic.com", StringComparison.Ordinal));
    public static bool Landscape(int width, int height) => width >= 1920 && height >= 1080 && width >= height * 1.3 && width <= height * 3.6 && (long)width * height <= 50_000_000;
    static bool ValidId(string id) => Regex.IsMatch(id, "^wp-[a-f0-9]{32}$");
    static string Key(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];
    string Cache(string id, string suffix) => Path.Combine(root, "cache", id + suffix);
    Saved? Read(string id) {
        if (!ValidId(id)) throw new InvalidOperationException("Escolha uma imagem da galeria.");
        try {
            var saved = LibraryJson.Read<Saved?>(Cache(id, ".json"), () => null);
            return saved?.Image != null && saved.Image.Id == id && AllowedImage(saved.FullUrl) && AllowedImage(saved.ThumbnailUrl) ? saved : null;
        } catch (Exception e) when (e is IOException or JsonException) { return null; }
    }
    Saved Entry(string title, string source, int width, int height, string full, string thumb, string page, string? artist = null, string? license = null) =>
        new(new("wp-" + Key(full), Clean(title), source, width, height, page, artist, license), full, thumb);
    async Task<byte[]> Download(string url, int max, CancellationToken ct) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("LazyGaming/0.28.4 (https://github.com/ImCarL6/lazygaming; artwork gallery)");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if ((int)response.StatusCode == 429) throw new InvalidOperationException("Limite temporário de consultas. Tente novamente em instantes.");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > max) throw new InvalidDataException("A imagem excede o tamanho permitido.");
        using var output = new MemoryStream(); await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        var buffer = new byte[8192]; int read;
        while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0) { if (output.Length + read > max) throw new InvalidDataException("Resposta muito grande."); output.Write(buffer, 0, read); }
        return output.ToArray();
    }
    async Task<JsonDocument> Json(string url, CancellationToken ct) => JsonDocument.Parse(await Download(url, 3_000_000, ct));
    public async Task<WallpaperResults> Search(string query, string? steamId, int page = 1, CancellationToken cancellation = default) {
        query = query.Trim();
        if (query.Length is < 2 or > 120 || query.Any(char.IsControl) || page is < 1 or > 5) throw new InvalidOperationException("Digite o nome do jogo para buscar fundos.");
        if (steamId != null && !Regex.IsMatch(steamId, "^[1-9][0-9]{0,9}$")) throw new InvalidOperationException("Jogo inválido.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(24)); var ct = deadline.Token;
        var cache = Cache("search-" + Key(query.ToUpperInvariant() + "|" + steamId + "|" + page), ".json");
        Cached? cached = null;
        try { cached = LibraryJson.Read<Cached?>(cache, () => null); } catch (Exception e) when (e is IOException or JsonException) { }
        cancellation.ThrowIfCancellationRequested();
        if (cached != null && DateTime.UtcNow - cached.At < TimeSpan.FromHours(6) && cached.Results.Images.All(i => Read(i.Id) != null)) return cached.Results with { Cached = true };
        async Task<(Saved[] Entries, bool More, string? Warning)> Provider(Func<Task<(Saved[], bool)>> load, string name) {
            try { var result = await load(); return (result.Item1, result.Item2, null); }
            catch (Exception e) when (Failure(e) && !cancellation.IsCancellationRequested) { return ([], false, name + ": busca indisponível no momento."); }
        }
        var results = await Task.WhenAll(Provider(() => Wallhaven(query, page, ct), "Wallhaven"),
            Provider(() => page == 1 ? Commons(query, ct) : Task.FromResult<(Saved[], bool)>(([], false)), "Wikimedia"),
            Provider(() => page == 1 && steamId != null ? Steam(query, steamId, ct) : Task.FromResult<(Saved[], bool)>(([], false)), "Steam"));
        cancellation.ThrowIfCancellationRequested();
        var entries = results.SelectMany(r => r.Entries).Where(e => Landscape(e.Image.Width, e.Image.Height) && AllowedImage(e.FullUrl) && AllowedImage(e.ThumbnailUrl)).DistinctBy(e => e.Image.Id).Take(40).ToArray();
        await files.WaitAsync(cancellation);
        try {
            foreach (var entry in entries) LibraryJson.Write(Cache(entry.Image.Id, ".json"), entry);
            var images = entries.Select(e => e.Image).ToArray(); var warnings = results.Where(r => r.Warning != null).Select(r => r.Warning!).ToArray();
            if (warnings.Length > 0 && cached != null) images = images.Concat(cached.Results.Images.Where(i => Read(i.Id) != null)).DistinctBy(i => i.Id).ToArray();
            var result = new WallpaperResults(images, warnings, page < 5 && results.Any(r => r.More), warnings.Length > 0 && cached != null);
            if (warnings.Length == 0) LibraryJson.Write(cache, new Cached(DateTime.UtcNow, result));
            Trim(); return result;
        } finally { files.Release(); }
    }
    async Task<(Saved[], bool)> Wallhaven(string query, int page, CancellationToken ct) {
        await wallhaven.WaitAsync(ct);
        JsonDocument doc;
        try {
            if (wallhavenNext > DateTime.UtcNow) await Task.Delay(wallhavenNext - DateTime.UtcNow, ct);
            wallhavenNext = DateTime.UtcNow.AddSeconds(2); // At most 30 searches/minute, including different queries/pages.
            var term = Regex.Replace(query, @"[^\p{L}\p{N}\s]", " ");
            doc = await Json("https://wallhaven.cc/api/v1/search?purity=100&categories=111&atleast=1920x1080&sorting=relevance&page=" + page + "&q=" + Uri.EscapeDataString(term), ct);
        } finally { wallhaven.Release(); }
        using (doc) {
            var entries = Items(doc.RootElement, "data").Where(e => Str(e, "purity") == "sfw" && e.TryGetProperty("thumbs", out _))
                .Select(e => Entry(query, "Wallhaven", Num(e, "dimension_x"), Num(e, "dimension_y"), Str(e, "path") ?? "", Str(e.GetProperty("thumbs"), "large") ?? "", Str(e, "url") ?? "")).ToArray();
            var more = doc.RootElement.TryGetProperty("meta", out var m) && Num(m, "current_page") < Num(m, "last_page"); return (entries, more);
        }
    }
    async Task<(Saved[], bool)> Commons(string query, CancellationToken ct) {
        using var doc = await Json("https://commons.wikimedia.org/w/api.php?action=query&format=json&generator=search&gsrnamespace=6&gsrlimit=8&prop=imageinfo&iiprop=url|size|mime|extmetadata&iiextmetadatafilter=Artist|LicenseShortName&iiurlwidth=600&gsrsearch=" + Uri.EscapeDataString(query + " wallpaper"), ct);
        if (!doc.RootElement.TryGetProperty("query", out var q) || !q.TryGetProperty("pages", out var pages)) return ([], false);
        var entries = new List<Saved>();
        foreach (var p in pages.EnumerateObject().Select(p => p.Value)) foreach (var i in Items(p, "imageinfo")) {
            if (Str(i, "mime") is not ("image/jpeg" or "image/png")) continue;
            string? Meta(string key) => i.TryGetProperty("extmetadata", out var m) && m.TryGetProperty(key, out var v) ? Clean(Str(v, "value")) : null;
            entries.Add(Entry(Clean(Str(p, "title")), "Wikimedia Commons", Num(i, "width"), Num(i, "height"), Str(i, "url") ?? "", Str(i, "thumburl") ?? "", Str(i, "descriptionurl") ?? "", Meta("Artist"), Meta("LicenseShortName")));
        }
        return (entries.ToArray(), false);
    }
    async Task<(Saved[], bool)> Steam(string title, string steamId, CancellationToken ct) {
        using var doc = await Json("https://store.steampowered.com/api/appdetails?appids=" + steamId + "&l=english&cc=BR", ct);
        if (!doc.RootElement.TryGetProperty(steamId, out var item) || !item.TryGetProperty("data", out var data) || Str(data, "type") is not ("game" or "mod")) return ([], false);
        var entries = new List<Saved>();
        foreach (var shot in Items(data, "screenshots").Take(4)) {
            var full = Str(shot, "path_full") ?? ""; var thumb = Str(shot, "path_thumbnail") ?? "";
            if (!AllowedImage(full) || !AllowedImage(thumb)) continue;
            var bytes = await Download(full, 20_000_000, ct);
            using var input = new MemoryStream(bytes); using var image = Image.FromStream(input);
            if (!Landscape(image.Width, image.Height)) continue;
            var entry = Entry(title, "Steam", image.Width, image.Height, full, thumb, "https://store.steampowered.com/app/" + steamId + "/");
            entries.Add(entry); Directory.CreateDirectory(Path.Combine(root, "cache"));
            using var optimized = new MemoryStream(bytes); ArtworkStore.OptimizeWallpaper(optimized, Cache(entry.Image.Id, "-full.jpg"));
        }
        return (entries.ToArray(), false);
    }
    public async Task<string?> ImageFile(string id, string kind, CancellationToken ct = default) {
        if (!ValidId(id) || kind is not ("thumbnail" or "full")) throw new InvalidOperationException("Imagem inválida.");
        var imageLock = imageLocks[StringComparer.Ordinal.GetHashCode(id + kind) & 31]; await imageLock.WaitAsync(ct);
        try {
            var item = Read(id); if (item == null) return null;
            var target = Cache(id, "-" + kind + ".jpg"); if (File.Exists(target)) return target;
            await downloads.WaitAsync(ct);
            try {
                var bytes = await Download(kind == "full" ? item.FullUrl : item.ThumbnailUrl, kind == "full" ? 20_000_000 : 4_000_000, ct);
                using var input = new MemoryStream(bytes);
                if (kind == "full") { using var image = Image.FromStream(input); if (!Landscape(image.Width, image.Height)) throw new InvalidDataException("Esta imagem não tem resolução suficiente para fundo."); input.Position = 0; }
                ArtworkStore.OptimizeWallpaper(input, target, kind == "thumbnail"); return target;
            } finally { downloads.Release(); }
        } catch (Exception e) when (Failure(e) && !ct.IsCancellationRequested) { return null; }
        finally { imageLock.Release(); }
    }
    public async Task<WallpaperSelection> Select(string id, CancellationToken ct = default) {
        var saved = Read(id) ?? throw new InvalidOperationException("Busque a imagem novamente para escolher o fundo.");
        var source = await ImageFile(id, "full", ct) ?? throw new InvalidOperationException("Não foi possível baixar esta imagem. O fundo anterior foi preservado.");
        using var image = Image.FromFile(source);
        if (!Landscape(image.Width, image.Height)) throw new InvalidOperationException("Esta imagem não tem resolução suficiente. O fundo anterior foi preservado.");
        var bytes = await File.ReadAllBytesAsync(source, ct); var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var relative = Path.Combine("selected", hash + ".jpg"); var destination = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (!File.Exists(destination)) { var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp"; try { await File.WriteAllBytesAsync(temp, bytes, ct); File.Move(temp, destination, true); } finally { File.Delete(temp); } }
        return new(id, relative, saved.Image.Title, saved.Image.Source, saved.Image.SourceUrl, image.Width, image.Height, saved.Image.Artist, saved.Image.License);
    }
    void Trim() {
        try {
        var folder = new DirectoryInfo(Path.Combine(root, "cache")); if (!folder.Exists) return;
        foreach (var file in folder.EnumerateFiles("search-*.json*").OrderByDescending(f => f.LastWriteTimeUtc).Skip(120)) file.Delete();
        foreach (var file in folder.EnumerateFiles("wp-*.json").Where(f => ValidId(Path.GetFileNameWithoutExtension(f.Name))).OrderByDescending(f => f.LastWriteTimeUtc).Skip(240)) {
            var id = Path.GetFileNameWithoutExtension(file.Name);
            foreach (var suffix in new[] { ".json", ".json.bak", "-thumbnail.jpg", "-full.jpg" }) File.Delete(Cache(id, suffix));
        }
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* A thumbnail being served must not fail the search. */ }
    }
}
