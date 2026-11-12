using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LazyGaming;

public record CatalogEntry(string Id, GameMetadata Metadata, string? Cover = null, string? Backdrop = null);
public record CatalogResults(CatalogEntry[] Matches, string[] Warnings, bool Cached = false);

// Public identity and artwork only. A catalog entry never supplies a command, executable or arguments.
public sealed class GameCatalog
{
    public record Saved(CatalogEntry Entry, string? CoverUrl, string? BackdropUrl, string? WikiTitle = null, string? WikiLanguage = null, bool Detailed = false, string[]? SteamIds = null);
    record SearchCache(DateTime At, CatalogResults Results);
    readonly string root;
    readonly HttpClient http;
    readonly SemaphoreSlim gate = new(1, 1);
    readonly SemaphoreSlim images = new(2, 2);
    readonly SemaphoreSlim[] imageLocks = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    sealed class InvalidSelection(string message) : InvalidOperationException(message);
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    const string UserAgent = "LazyGaming/0.28.4 (https://github.com/ImCarL6/lazygaming; game metadata)";
    public GameCatalog(string directory, HttpClient? client = null)
    {
        root = Path.Combine(directory, "library", "catalog-search");
        http = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(8) };
    }
    static bool ValidId(string id) => Regex.IsMatch(id, @"^(steam-[1-9][0-9]{0,9}|wiki-Q[1-9][0-9]{0,12})$");
    string PathFor(string id, string suffix) => Path.Combine(root, id + suffix);
    static string? Str(JsonElement e, string key) => e.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    static JsonElement[] Array(JsonElement e, string key) => e.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray().ToArray() : [];
    static bool Failure(Exception e) => e is HttpRequestException or IOException or JsonException or OperationCanceledException or ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException;
    static string Clean(string? text, int max = 2000) => WebUtility.HtmlDecode(Regex.Replace(text ?? "", "<[^>]+>", " ")).Trim() is var s ? s[..Math.Min(s.Length, max)] : "";
    // Only remove catalog disambiguation suffixes. Keep meaningful edition names such as "3D", "HD" and "Remastered".
    public static string BaseTitle(string title) => Regex.Replace(title, @"\s*\((?:(?:19|20)\d{2})(?:\s+(?:video game|jogo eletr[oô]nico|remake))?\)\s*$", "", RegexOptions.IgnoreCase);
    static string Url(string id, string kind) => $"/api/hub/catalog/art/{id}/{kind}";
    public static bool AllowedImage(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https" && u.Port == 443 && u.UserInfo == "" &&
        (u.Host == "upload.wikimedia.org" || u.Host == "steamstatic.com" || u.Host.EndsWith(".steamstatic.com", StringComparison.Ordinal));
    async Task<byte[]> Download(string url, int max, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.UserAgent.ParseAdd(UserAgent);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > max) throw new InvalidDataException("Resposta do catálogo muito grande.");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token); using var output = new MemoryStream();
        var buffer = new byte[8192]; int read;
        while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0) { if (output.Length + read > max) throw new InvalidDataException("Resposta do catálogo muito grande."); output.Write(buffer, 0, read); }
        return output.ToArray();
    }
    async Task<JsonDocument> Json(string url, CancellationToken ct) => JsonDocument.Parse(await Download(url, 3_000_000, ct));
    void Write<T>(string file, T value) { Directory.CreateDirectory(root); LibraryJson.Write(file, value); }
    Saved? Read(string id)
    {
        if (!ValidId(id)) throw new InvalidOperationException("Escolha um jogo na lista de resultados.");
        try { return JsonSerializer.Deserialize<Saved>(File.ReadAllText(PathFor(id, ".json")), JsonOptions); }
        catch (Exception e) when (e is IOException or JsonException) { return null; }
    }
    void Save(Saved item) {
        var previous = Read(item.Entry.Id);
        if (previous != null) foreach (var kind in new[] { "cover", "backdrop" })
            if ((kind == "cover" ? previous.CoverUrl != item.CoverUrl : previous.BackdropUrl != item.BackdropUrl)) File.Delete(PathFor(item.Entry.Id, "-" + kind + ".jpg"));
        Write(PathFor(item.Entry.Id, ".json"), item);
    }
    public async Task<CatalogResults> Search(string query, CancellationToken cancellation = default)
    {
        query = query.Trim();
        if (query.Length is < 2 or > 120 || query.Any(char.IsControl)) throw new InvalidOperationException("Digite de 2 a 120 caracteres do nome do jogo.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(23)); var ct = deadline.Token;
        var key = "search-v2-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(query.ToUpperInvariant())));
        SearchCache? cached = null;
        try { cached = JsonSerializer.Deserialize<SearchCache>(File.ReadAllText(PathFor(key, ".json")), JsonOptions); } catch (Exception e) when (e is IOException or JsonException) { }
        cancellation.ThrowIfCancellationRequested();
        if (cached != null && DateTime.UtcNow - cached.At < TimeSpan.FromHours(6) && cached.Results.Matches.All(m => Read(m.Id) != null))
            return cached.Results with { Cached = true, Matches = cached.Results.Matches.Select(m => Read(m.Id)!.Entry).ToArray() };
        // Providers are independent: an outage does not hide successful matches from another source.
        async Task<(Saved[] Items, string? Warning)> Source(Func<Task<Saved[]>> load, string name) {
            try { return (await load(), null); } catch (Exception e) when (Failure(e) && !cancellation.IsCancellationRequested) { return ([], name + " indisponível. Tente novamente mais tarde."); }
        }
        var providers = await Task.WhenAll(Source(() => SteamSearch(query, ct), "Steam"), Source(() => WikiSearch(query, ct), "Wikipédia/Wikidata"));
        cancellation.ThrowIfCancellationRequested();
        var found = providers.SelectMany(p => p.Items).DistinctBy(e => e.Entry.Id).ToArray();
        var linkedSteamIds = found.SelectMany(e => e.SteamIds ?? []).ToHashSet();
        // Same work on two sources: merge only with a proven public identity link, never merely equal names.
        var candidates = found.Where(e => e.Entry.Metadata.Source != "steam" || !linkedSteamIds.Contains(e.Entry.Metadata.StoreId ?? "")).ToArray();
        var exact = candidates.Any(e => GameMetadataStore.Exact(BaseTitle(e.Entry.Metadata.Title), BaseTitle(query)));
        var items = candidates.Where(e => !exact || GameMetadataStore.Normalize(BaseTitle(e.Entry.Metadata.Title)).StartsWith(GameMetadataStore.Normalize(BaseTitle(query))))
            .OrderByDescending(e => GameMetadataStore.Exact(BaseTitle(e.Entry.Metadata.Title), BaseTitle(query)))
            .ThenByDescending(e => GameMetadataStore.Normalize(BaseTitle(e.Entry.Metadata.Title)).StartsWith(GameMetadataStore.Normalize(BaseTitle(query))))
            .ThenBy(e => e.Entry.Metadata.Year ?? 9999).Take(18).ToArray();
        var warnings = providers.Where(p => p.Warning != null).Select(p => p.Warning!).ToArray();
        await gate.WaitAsync(cancellation);
        try {
            foreach (var item in items) { var previous = Read(item.Entry.Id); if (previous?.Detailed != true || DateTime.UtcNow - previous.Entry.Metadata.CheckedAt > TimeSpan.FromDays(7)) Save(item); }
            var matches = items.Select(e => (Read(e.Entry.Id) ?? e).Entry).ToArray();
            if (warnings.Length > 0 && cached != null) matches = matches.Concat(cached.Results.Matches).DistinctBy(e => e.Id).Take(18).ToArray();
            var results = new CatalogResults(matches, warnings, warnings.Length > 0 && cached != null);
            // Do not cache outages or empty misses; a retry can recover immediately.
            if (warnings.Length == 0 && matches.Length > 0) { Write(PathFor(key, ".json"), new SearchCache(DateTime.UtcNow, results)); TrimCache(); }
            return results;
        } finally { gate.Release(); }
    }
    void TrimCache()
    {
        // Flat, private cache only; never touch registrations, library artwork or game files.
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("search-*.json*").OrderByDescending(f => f.LastWriteTimeUtc).Skip(160)) file.Delete();
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*.json").Where(f => ValidId(Path.GetFileNameWithoutExtension(f.Name))).OrderByDescending(f => f.LastWriteTimeUtc).Skip(300)) {
            var id = Path.GetFileNameWithoutExtension(file.Name);
            foreach (var suffix in new[] { ".json", ".json.bak", "-cover.jpg", "-backdrop.jpg" }) File.Delete(PathFor(id, suffix));
        }
    }
    async Task<Saved[]> SteamSearch(string query, CancellationToken ct)
    {
        using var doc = await Json("https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(query) + "&l=english&cc=BR", ct);
        var matches = Array(doc.RootElement, "items").Where(e => Str(e, "type") == "app" && e.TryGetProperty("id", out var id) && uint.TryParse(id.ToString(), out var n) && n > 0)
            .Take(8).Select(e => {
                var n = e.GetProperty("id").ToString(); var id = "steam-" + n; var title = Clean(Str(e, "name"), 120);
                return new Saved(new(id, new(n, title, null, [], [], "https://store.steampowered.com/app/" + n + "/", DateTime.UtcNow, CatalogId: id), Url(id, "cover")), Str(e, "tiny_image"), null);
            }).Where(e => e.Entry.Metadata.Title.Length > 0).ToArray();
        using var limit = new SemaphoreSlim(3, 3);
        var detailed = await Task.WhenAll(matches.Select(async item => {
            await limit.WaitAsync(ct);
            try { return (await SteamDetails(item, ct)) with { Detailed = true }; }
            catch (InvalidSelection) { return null; } // DLC, soundtracks, cosmetics and tools never become game suggestions.
            catch (Exception e) when (Failure(e) && !ct.IsCancellationRequested) { return item; }
            finally { limit.Release(); }
        }));
        return detailed.Where(e => e != null).Select(e => e!).ToArray();
    }
    static string WikiApi(string lang) => "https://" + lang + ".wikipedia.org/w/api.php?format=json&";
    static IEnumerable<JsonElement> Pages(JsonDocument doc) => doc.RootElement.TryGetProperty("query", out var q) && q.TryGetProperty("pages", out var p) ? p.EnumerateObject().Select(e => e.Value) : [];
    static string[] Claims(JsonElement entity, string property) => entity.TryGetProperty("claims", out var claims) ? Array(claims, property)
        .Where(e => Str(e, "rank") != "deprecated" && e.TryGetProperty("mainsnak", out var s) && s.TryGetProperty("datavalue", out _))
        .Select(e => Str(e.GetProperty("mainsnak").GetProperty("datavalue").GetProperty("value"), "id") ?? "").Where(s => Regex.IsMatch(s, "^Q[0-9]+$")).Distinct().ToArray() : [];
    static int? Year(JsonElement entity)
    {
        if (!entity.TryGetProperty("claims", out var c)) return null;
        var years = Array(c, "P577").Where(e => Str(e, "rank") != "deprecated" && e.GetProperty("mainsnak").TryGetProperty("datavalue", out _))
            .Select(e => Str(e.GetProperty("mainsnak").GetProperty("datavalue").GetProperty("value"), "time"))
            .Select(s => int.TryParse(s?.TrimStart('+').Split('-')[0], out var year) ? year : 0).Where(y => y is > 1900 and < 2200).ToArray();
        return years.Length == 0 ? null : years.Min();
    }
    async Task<Saved[]> WikiSearch(string query, CancellationToken ct)
    {
        async Task<(string Lang, JsonElement[] Pages, bool Success)> SearchLanguage(string lang) {
            using var d = await Json(WikiApi(lang) + "action=query&generator=search&gsrnamespace=0&gsrlimit=8&prop=pageprops|pageimages|extracts&piprop=original&pilicense=any&exintro=1&explaintext=1&exchars=1400&gsrsearch=" + Uri.EscapeDataString(query), ct);
            return (lang, Pages(d).Select(p => p.Clone()).ToArray(), true);
        }
        // One language may be unavailable without losing the other.
        var languages = await Task.WhenAll(new[] { "en", "pt" }.Select(async l => { try { return await SearchLanguage(l); } catch (Exception e) when (Failure(e) && !ct.IsCancellationRequested) { return (Lang: l, Pages: System.Array.Empty<JsonElement>(), Success: false); } }));
        var pages = languages.SelectMany(l => l.Pages.Where(p => p.TryGetProperty("pageprops", out var props) && !props.TryGetProperty("disambiguation", out _) && Str(props, "wikibase_item") != null).Select(p => (l.Lang, Page: p)))
            .GroupBy(p => Str(p.Page.GetProperty("pageprops"), "wikibase_item")!).Select(g => g.OrderBy(p => p.Lang == "pt" ? 0 : 1).First()).Take(14).ToArray();
        if (pages.Length == 0) {
            if (languages.All(l => !l.Success)) throw new InvalidOperationException("Busca pública indisponível.");
            return [];
        }
        using var entities = await Json("https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&props=claims|labels&languages=pt|en&ids=" + string.Join('|', pages.Select(p => Str(p.Page.GetProperty("pageprops"), "wikibase_item"))), ct);
        var all = entities.RootElement.GetProperty("entities");
        var games = pages.Where(p => all.TryGetProperty(Str(p.Page.GetProperty("pageprops"), "wikibase_item")!, out var e) &&
            !Claims(e, "P31").Intersect(new[] { "Q5398426", "Q7058673", "Q13406463", "Q196600" }).Any() &&
            (Claims(e, "P31").Contains("Q7889") || Claims(e, "P400").Length > 0 && Claims(e, "P178").Length > 0)).ToArray();
        var labelIds = games.SelectMany(p => {
            var e = all.GetProperty(Str(p.Page.GetProperty("pageprops"), "wikibase_item")!);
            return Claims(e, "P178").Concat(Claims(e, "P136")).Concat(Claims(e, "P400")).Concat(Claims(e, "P123"));
        }).Distinct().Take(50).ToArray();
        using var labels = labelIds.Length == 0 ? JsonDocument.Parse("{\"entities\":{}}") : await Json("https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&props=labels&languages=pt|en&ids=" + string.Join('|', labelIds), ct);
        string[] Names(JsonElement entity, string prop) => Claims(entity, prop).Select(id => labels.RootElement.GetProperty("entities").TryGetProperty(id, out var e) && e.TryGetProperty("labels", out var l)
            ? l.TryGetProperty("pt", out var pt) ? Str(pt, "value") : l.TryGetProperty("en", out var en) ? Str(en, "value") : null : null).Where(s => s != null).Select(s => Clean(s, 160)).ToArray();
        return games.Select(p => {
            var qid = Str(p.Page.GetProperty("pageprops"), "wikibase_item")!; var id = "wiki-" + qid; var e = all.GetProperty(qid);
            var title = Clean(Str(p.Page, "title"), 120); var image = p.Page.TryGetProperty("original", out var o) ? Str(o, "source") : null;
            var source = "https://" + p.Lang + ".wikipedia.org/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_'));
            var metadata = new GameMetadata(null, BaseTitle(title), Year(e), Names(e, "P178"), Names(e, "P136"), source, DateTime.UtcNow, "wikipedia", Clean(Str(p.Page, "extract")),
                Platforms: Names(e, "P400"), Publishers: Names(e, "P123"), CatalogId: id);
            var steamIds = e.TryGetProperty("claims", out var claims) ? Array(claims, "P1733").Where(c => Str(c, "rank") != "deprecated" && c.GetProperty("mainsnak").TryGetProperty("datavalue", out _))
                .Select(c => c.GetProperty("mainsnak").GetProperty("datavalue").GetProperty("value")).Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).Where(v => uint.TryParse(v, out _)).ToArray() : [];
            return new Saved(new(id, metadata, AllowedImage(image) ? Url(id, "cover") : null), image, null, title, p.Lang, SteamIds: steamIds);
        }).ToArray();
    }
    public async Task<CatalogEntry> Details(string id, CancellationToken cancellation = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(23)); var ct = deadline.Token;
        await gate.WaitAsync(ct);
        try {
            var item = Read(id) ?? throw new InvalidOperationException("Busque o jogo novamente para escolher a ficha.");
            if (item.Detailed && DateTime.UtcNow - item.Entry.Metadata.CheckedAt < TimeSpan.FromDays(7)) return item.Entry;
            if (id.StartsWith("steam-")) item = await SteamDetails(item, ct);
            else item = await WikiDetails(item, ct);
            Save(item with { Detailed = true }); return item.Entry;
        } catch (Exception e) when (Failure(e) && e is not InvalidSelection && !cancellation.IsCancellationRequested) {
            if (Read(id) is { } saved) return saved.Entry; // Keep the usable partial entry; never invent absent fields.
            throw new InvalidOperationException("Não foi possível carregar a ficha. Tente buscar o jogo novamente.");
        } finally { gate.Release(); }
    }
    public string? KnownSteamId(string id) {
        if (!ValidId(id)) return null;
        var saved = Read(id); return saved?.Entry.Metadata.StoreId ?? saved?.SteamIds?.FirstOrDefault();
    }
    async Task<Saved> SteamDetails(Saved item, CancellationToken ct)
    {
        var n = item.Entry.Id[6..];
        using var doc = await Json("https://store.steampowered.com/api/appdetails?appids=" + n + "&l=brazilian&cc=BR", ct);
        var response = doc.RootElement.GetProperty(n);
        if (!response.TryGetProperty("data", out var data) || Str(data, "type") is not ("game" or "mod")) throw new InvalidSelection("Este resultado não é um jogo completo. Escolha outra ficha.");
        int? year = null;
        if (data.TryGetProperty("release_date", out var r) && !(r.TryGetProperty("coming_soon", out var coming) && coming.GetBoolean())) { var m = Regex.Match(Str(r, "date") ?? "", @"\b(19\d{2}|20\d{2})\b"); if (m.Success) year = int.Parse(m.Value); }
        var name = Clean(Str(data, "name"), 120);
        var editionYear = Regex.Match(name, @"\(((?:19|20)\d{2})\)\s*$"); if (editionYear.Success) year = int.Parse(editionYear.Groups[1].Value);
        var platforms = data.TryGetProperty("platforms", out var ps) ? ps.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.True).Select(p => p.Name == "windows" ? "Windows" : p.Name == "mac" ? "macOS" : "Linux").ToArray() : [];
        var metadata = item.Entry.Metadata with { Title = BaseTitle(name), Year = year, Developers = Array(data, "developers").Select(p => Clean(p.GetString(), 160)).ToArray(),
            Genres = Array(data, "genres").Select(p => Clean(Str(p, "description"), 160)).ToArray(), Description = Clean(Str(data, "short_description")), Platforms = platforms,
            Publishers = Array(data, "publishers").Select(p => Clean(p.GetString(), 160)).ToArray(), CheckedAt = DateTime.UtcNow };
        var backdrop = Str(data, "background_raw") ?? Array(data, "screenshots").Select(p => Str(p, "path_full")).FirstOrDefault();
        return item with { Entry = new(item.Entry.Id, metadata, Url(item.Entry.Id, "cover"), AllowedImage(backdrop) ? Url(item.Entry.Id, "backdrop") : null),
            CoverUrl = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{n}/library_600x900.jpg", BackdropUrl = backdrop };
    }
    async Task<Saved> WikiDetails(Saved item, CancellationToken ct)
    {
        if (item.WikiLanguage is not ("en" or "pt") || string.IsNullOrEmpty(item.WikiTitle)) return item;
        // Images must belong to this article and have a game-specific filename; excludes portraits, flags and navigation art.
        using var doc = await Json(WikiApi(item.WikiLanguage) + "action=query&prop=images&imlimit=50&titles=" + Uri.EscapeDataString(item.WikiTitle), ct);
        var titleWords = Regex.Split(item.WikiTitle.ToLowerInvariant(), @"[^\p{L}\p{N}]+").Where(w => w.Length >= 4 && w is not ("legend" or "video" or "game" or "jogo" or "time")).ToArray();
        var images = Pages(doc).SelectMany(p => Array(p, "images")).Select(p => Str(p, "title") ?? "")
            .Where(t => Regex.IsMatch(t, @"\.(jpe?g|png)$", RegexOptions.IgnoreCase) && titleWords.Any(w => t.Contains(w, StringComparison.OrdinalIgnoreCase)) &&
                !Regex.IsMatch(t, "cover|box.?art|logo|portrait|flag|capa", RegexOptions.IgnoreCase)).Take(12).ToArray();
        if (images.Length == 0) return item;
        using var info = await Json(WikiApi(item.WikiLanguage) + "action=query&prop=imageinfo&iiprop=url|size&iiurlwidth=1600&titles=" + Uri.EscapeDataString(string.Join('|', images)), ct);
        var candidates = Pages(info).SelectMany(p => Array(p, "imageinfo")).Where(i => i.TryGetProperty("width", out var w) && i.TryGetProperty("height", out var h) && w.GetInt32() >= 300 && w.GetInt32() >= h.GetInt32() * 1.3)
            .OrderByDescending(i => i.GetProperty("width").GetInt32() * i.GetProperty("height").GetInt32())
            .Select(i => Str(i, "thumburl") ?? Str(i, "url")).Where(AllowedImage)
            .Where(u => item.CoverUrl == null || new Uri(u!).AbsolutePath != new Uri(item.CoverUrl).AbsolutePath).ToArray();
        var backdrop = candidates.FirstOrDefault();
        return item with { BackdropUrl = backdrop, Entry = item.Entry with { Backdrop = backdrop == null ? null : Url(item.Entry.Id, "backdrop") } };
    }
    public async Task<string?> Image(string id, string kind, CancellationToken ct = default)
    {
        if (!ValidId(id)) throw new InvalidOperationException("Escolha um jogo na lista de resultados.");
        if (kind is not ("cover" or "backdrop")) throw new InvalidOperationException("Imagem inválida.");
        var imageLock = imageLocks[StringComparer.Ordinal.GetHashCode(id + kind) & 31];
        await imageLock.WaitAsync(ct);
        try { return await ImageCore(id, kind, ct); } finally { imageLock.Release(); }
    }
    async Task<string?> ImageCore(string id, string kind, CancellationToken ct)
    {
        await images.WaitAsync(ct);
        try {
            var item = Read(id); if (item == null) return null;
            var target = PathFor(id, "-" + kind + ".jpg");
            if (File.Exists(target)) return target;
            var url = kind == "cover" ? item.CoverUrl : item.BackdropUrl;
            if (!AllowedImage(url)) return null;
            byte[] bytes;
            try { bytes = await Download(url!, 8_000_000, ct); }
            catch (HttpRequestException) when (id.StartsWith("steam-") && kind == "cover") {
                bytes = await Download($"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{id[6..]}/header.jpg", 8_000_000, ct);
            }
            using var input = new MemoryStream(bytes); ArtworkStore.OptimizeLocal(input, target, kind); return target;
        } catch (Exception e) when (Failure(e) && !ct.IsCancellationRequested) { return null; }
        finally { images.Release(); }
    }
}
