using System.Text.RegularExpressions;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;

namespace LazyGaming;

public sealed class ArtworkStore(GameLibrary library, HttpClient? client = null)
{
    readonly HttpClient http = client ?? new() { Timeout = TimeSpan.FromSeconds(10) };
    readonly SemaphoreSlim downloads = new(2, 2);
    readonly SemaphoreSlim refresh = new(1, 1);
    public string Root => Path.Combine(library.Directory, "library", "art");
    public string? FileFor(string id, string kind)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-zA-Z0-9-]{1,80}$")) return null;
        if (!library.Games.Any(g => library.Metadata.ArtworkId(g) == id || g.Id == id) || kind is not ("cover" or "backdrop" or "icon")) return null;
        var path = Path.Combine(Root, id, kind + ".jpg"); return File.Exists(path) ? path : null;
    }
    public string? Url(Game game, string kind)
    {
        var id = kind == "icon" ? game.Id : library.Metadata.ArtworkId(game);
        if (id == null) return null;
        var file = FileFor(id, kind);
        return file == null ? null : $"/art/{id}/{kind}.jpg?v={File.GetLastWriteTimeUtc(file).Ticks}";
    }
    public async Task EnsureAll()
    {
        await refresh.WaitAsync();
        try {
        var games = library.Games.ToArray();
        foreach (var game in games) EnsureIcon(game);
        await Task.WhenAll(games.DistinctBy(g => library.Metadata.ArtworkId(g) ?? g.Id).Select(async game =>
        {
            await downloads.WaitAsync();
            try {
                if (library.Metadata.For(game)?.Source == "local") EnsureLocal(game);
                else if (game.Id.StartsWith("emu-", StringComparison.Ordinal) && library.Metadata.For(game)?.StoreId == null) await EnsureEncyclopedia(game);
                else await Ensure(game with { ArtworkId = library.Metadata.ArtworkId(game) });
            } catch (Exception e) { RuntimeDiagnostics.Report("artwork", "prepare-failed", e); }
            finally { downloads.Release(); }
        })); } finally { refresh.Release(); }
    }
    void EnsureLocal(Game game)
    {
        var metadata = library.Metadata.For(game)!;
        if (metadata.Artwork is not LocalArtwork art) return;
        var folder = Path.Combine(Root, game.Id); Directory.CreateDirectory(folder);
        foreach (var (kind, relative, url) in new[] { ("cover", art.Cover, art.CoverSourceUrl), ("backdrop", art.Backdrop, art.BackdropSourceUrl) }) {
            // Jogos de emulador usam a imagem ao lado do jogo (caminho absoluto achado pela própria varredura); fichas importadas, só dentro da biblioteca.
            var source = game.Id.StartsWith("emu-", StringComparison.Ordinal) && Path.IsPathFullyQualified(relative) && Regex.IsMatch(Path.GetExtension(relative), @"^\.(jpe?g|png|webp)$", RegexOptions.IgnoreCase)
                ? relative : GameLibrary.SafeInside(Path.Combine(library.Directory, "library"), relative);
            if (source == null || !File.Exists(source)) continue;
            var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source)));
            var target = Path.Combine(folder, kind + ".jpg"); var stamp = target + ".source.json";
            var provenance = JsonSerializer.Serialize(new { source = "local", page = metadata.SourceUrl, image = url, sha256 = hash });
            if (File.Exists(target) && File.Exists(stamp) && File.ReadAllText(stamp) == provenance) continue;
            using (var input = File.OpenRead(source)) OptimizeLocal(input, target, kind);
            if (kind == "cover" && game.Id.StartsWith("emu-", StringComparison.Ordinal)) { using var icon = File.OpenRead(source); OptimizeLocal(icon, Path.Combine(folder, "icon.jpg"), "icon"); }
            File.WriteAllText(stamp, provenance);
        }
    }
    // Jogos de emulador (sem loja no PC): capa oficial pela Wikipédia, só quando o título do artigo confere com o jogo.
    // Sem achar, tenta de novo no máximo uma vez por dia. Funciona igual em qualquer PC; a TV recebe a mesma imagem pelo auxiliar.
    async Task EnsureEncyclopedia(Game game)
    {
        var folder = Path.Combine(Root, game.Id); Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, "cover.jpg"); var stamp = target + ".source.json";
        if (File.Exists(stamp) && (File.Exists(target) || File.GetLastWriteTimeUtc(stamp) > DateTime.UtcNow.AddDays(-1))) return;
        var found = await EncyclopediaCover(http, game.Name);
        if (found is not { } cover) { File.WriteAllText(stamp, JsonSerializer.Serialize(new { source = "wikipedia", found = false })); return; }
        using var request = new HttpRequestMessage(HttpMethod.Get, cover.Image);
        request.Headers.UserAgent.ParseAdd(EncyclopediaUserAgent);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 8_000_000) return;
        var bytes = await response.Content.ReadAsByteArrayAsync();
        if (bytes.Length > 8_000_000) return;
        using (var input = new MemoryStream(bytes)) OptimizeLocal(input, target, "cover");
        using (var input = new MemoryStream(bytes)) OptimizeLocal(input, Path.Combine(folder, "icon.jpg"), "icon");
        File.WriteAllText(stamp, JsonSerializer.Serialize(new { source = "wikipedia", page = cover.Page, image = cover.Image }));
    }
    public static async Task<(string Page, string Image)?> EncyclopediaCover(HttpClient http, string title)
    {
        foreach (var language in new[] { "en", "pt" })
            try { if (await EncyclopediaCover(http, title, language) is { } found) return found; }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException) { }
        return null;
    }
    static string EncyclopediaUserAgent => "LazyGaming/" + (typeof(ArtworkStore).Assembly.GetName().Version?.ToString(3) ?? "1") + " (game launcher artwork)";
    static async Task<(string Page, string Image)?> EncyclopediaCover(HttpClient http, string title, string language)
    {
        var url = "https://" + language + ".wikipedia.org/w/api.php?action=query&format=json&generator=search&gsrlimit=3&prop=pageimages&piprop=original&pilicense=any&gsrsearch="
            + Uri.EscapeDataString(title + (language == "pt" ? " jogo eletrônico" : " video game"));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(EncyclopediaUserAgent);
        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages)) return null;
        static string C(string s) => Regex.Replace(s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD), "[^a-z0-9]", "");
        var wanted = C(title);
        foreach (var page in pages.EnumerateObject().Select(p => p.Value).OrderBy(p => p.TryGetProperty("index", out var i) ? i.GetInt32() : 99)) {
            var name = page.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            var key = C(Regex.Replace(name, @"\s*\(.*\)$", ""));
            if (key.Length == 0 || !(key == wanted || key.Contains(wanted) && wanted.Length >= 5 || wanted.Contains(key) && key.Length >= 5)) continue;
            if (page.TryGetProperty("original", out var original) && original.TryGetProperty("source", out var source) && source.GetString() is { } image
                && image.StartsWith("https://upload.wikimedia.org/", StringComparison.Ordinal)) return (name, image);
        }
        return null;
    }
    void EnsureIcon(Game game)
    {
        var folder = Path.Combine(Root, game.Id); System.IO.Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, "icon.jpg"); if (game.Id.StartsWith("emu-", StringComparison.Ordinal) || File.Exists(target) || !File.Exists(game.ProcessPath)) return;
        try { using var icon = Icon.ExtractIcon(game.ProcessPath, 0, 256); if (icon == null) return;
            using var bitmap = icon.ToBitmap(); using var input = new MemoryStream(); bitmap.Save(input, ImageFormat.Png); input.Position = 0; Optimize(input, target, "icon"); }
        catch (Exception e) when (e is ArgumentException or IOException or System.ComponentModel.Win32Exception) { }
    }
    async Task Ensure(Game game)
    {
        if (game.ArtworkId is not string id) return;
        var output = Path.Combine(Root, id); System.IO.Directory.CreateDirectory(output);
        var cover = Path.Combine(output, "cover.jpg"); var backdrop = Path.Combine(output, "backdrop.jpg");
        var assets = library.ArtworkMetadata.GetValueOrDefault(id) ?? new();
        var publicAssets = assets.Count == 0 ? await PublicLibraryAssets.Load(http, output, id) : new Dictionary<string, string[]>();
        string AssetPath(string kind)
        {
            var asset = assets.Child(kind == "cover" ? "library_capsule" : "library_hero");
            return new[] { asset.Child("image2x").Text("brazilian"), asset.Child("image").Text("brazilian"), asset.Child("image2x").Text("portuguese"), asset.Child("image").Text("portuguese"), asset.Child("image2x").Text("english"), asset.Child("image").Text("english") }.FirstOrDefault(s => s != "") ?? "";
        }
        var sourceKey = "hq3|" + AssetPath("cover") + "|" + AssetPath("backdrop") + "|" + string.Join("|", publicAssets.Values.SelectMany(v => v));
        var stamp = Path.Combine(output, "art-source.txt");
        bool changed = !File.Exists(stamp) || File.ReadAllText(stamp) != sourceKey;
        foreach (var file in new[] { cover, backdrop })
            if (File.Exists(file)) { using var existing = Image.FromFile(file); if (!Useful(existing)) { existing.Dispose(); File.Delete(file); } }
        if (File.Exists(cover) && File.Exists(backdrop) && !changed) return;
        var cache = Path.Combine(library.SteamPath, "appcache", "librarycache");
        string? Local(string kind)
        {
            var name = kind == "cover" ? "library_600x900.jpg" : "library_hero.jpg";
            return new[] { Path.Combine(cache, id, name), Path.Combine(cache, id + "_" + name) }.FirstOrDefault(File.Exists);
        }
        foreach (var kind in new[] { "cover", "backdrop" })
        {
            var target = kind == "cover" ? cover : backdrop; if (File.Exists(target)) continue;
            if (Local(kind) is string local) { try { using var source = File.OpenRead(local); Optimize(source, target, kind); } catch { } }
        }
        JsonElement? details = null;
        if (!File.Exists(cover) || !File.Exists(backdrop))
        {
            var metadata = Path.Combine(output, "store.json");
            try
            {
                string json = File.Exists(metadata) ? File.ReadAllText(metadata) : await http.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={id}&l=brazilian&cc=BR");
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(id, out var entry) && entry.TryGetProperty("data", out var data))
                { details = data.Clone(); if (!File.Exists(metadata)) File.WriteAllText(metadata, json); }
            }
            catch { }
        }
        foreach (var kind in new[] { "cover", "backdrop" })
        {
            var target = kind == "cover" ? cover : backdrop; if (File.Exists(target) && !changed) continue;
            var name = kind == "cover" ? "library_600x900.jpg" : "library_hero.jpg";
            var urls = new List<string> { $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{id}/{name}", $"https://cdn.cloudflare.steamstatic.com/steam/apps/{id}/{name}" };
            if (publicAssets.TryGetValue(kind, out var resolved)) urls.InsertRange(0, resolved);
            var relative = AssetPath(kind);
            if (relative != "" && !relative.Contains("..") && !relative.Contains("://")) urls.Insert(0, $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{id}/{relative}");
            if (details is JsonElement data)
            {
                if (data.TryGetProperty(kind == "cover" ? "header_image" : "background_raw", out var image)) urls.Add(image.GetString() ?? "");
                if (kind == "backdrop" && data.TryGetProperty("screenshots", out var screenshots) && screenshots.GetArrayLength() > 0)
                    urls.Add(screenshots[0].GetProperty("path_full").GetString() ?? "");
            }
            foreach (var url in urls)
            {
                try
                {
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !(uri.Host.Equals("steamstatic.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".steamstatic.com", StringComparison.OrdinalIgnoreCase))) continue;
                    using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
                    if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 8_000_000) continue;
                    var bytes = await response.Content.ReadAsByteArrayAsync(); if (bytes.Length > 8_000_000) continue;
                    using var input = new MemoryStream(bytes); Optimize(input, target, kind); break;
                }
                catch { }
            }
        }
        if (File.Exists(cover) && File.Exists(backdrop)) File.WriteAllText(stamp, sourceKey);
    }
    public static void OptimizeLocal(Stream input, string output, string kind) => Optimize(input, output, kind, true);
    static void Optimize(Stream input, string output, string kind, bool local = false)
    {
        using var image = Image.FromStream(input);
        if (!Useful(image)) throw new InvalidDataException("Placeholder de arte sem conteúdo.");
        if ((long)image.Width * image.Height > 50_000_000) throw new InvalidDataException();
        double scale = kind == "icon" ? Math.Min(1, 256.0 / Math.Max(image.Width, image.Height)) : kind == "cover" ? Math.Min(1, Math.Min((local ? 480.0 : 600.0) / image.Width, (local ? 720.0 : 900.0) / image.Height)) : Math.Min(1, Math.Min((local ? 1600.0 : 2560.0) / image.Width, (local ? 900.0 : 1440.0) / image.Height));
        using var bitmap = new Bitmap(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)), PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        { graphics.Clear(Color.Black); graphics.InterpolationMode = InterpolationMode.HighQualityBicubic; graphics.DrawImage(image, 0, 0, bitmap.Width, bitmap.Height); }
        var encoder = ImageCodecInfo.GetImageEncoders().Single(c => c.MimeType == "image/jpeg");
        using var quality = new EncoderParameters(1); quality.Param[0] = new(System.Drawing.Imaging.Encoder.Quality, kind == "backdrop" ? 90L : 92L);
        var temp = output + ".tmp"; bitmap.Save(temp, encoder, quality); File.Move(temp, output, true);
    }
    static bool Useful(Image image)
    {
        using var thumb = new Bitmap(16, 16); using (var g = Graphics.FromImage(thumb)) g.DrawImage(image, 0, 0, 16, 16);
        int min = 765, max = 0;
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) { var c = thumb.GetPixel(x, y); int value = c.R + c.G + c.B; min = Math.Min(min, value); max = Math.Max(max, value); }
        return max - min >= 18;
    }
}
