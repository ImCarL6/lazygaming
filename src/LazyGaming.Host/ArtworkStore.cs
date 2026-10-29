using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;

namespace LazyGaming;

public sealed class ArtworkStore(GameLibrary library)
{
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
    readonly SemaphoreSlim downloads = new(2, 2);
    readonly SemaphoreSlim refresh = new(1, 1);
    public string Root => Path.Combine(library.Directory, "library", "art");
    public string? FileFor(string id, string kind)
    {
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
            try { await Ensure(game with { ArtworkId = library.Metadata.ArtworkId(game) }); } catch { /* Arte ausente tem fallback; não impede administrar o jogo. */ }
            finally { downloads.Release(); }
        })); } finally { refresh.Release(); }
    }
    void EnsureIcon(Game game)
    {
        var folder = Path.Combine(Root, game.Id); System.IO.Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, "icon.jpg"); if (File.Exists(target) || !File.Exists(game.ProcessPath)) return;
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
    static void Optimize(Stream input, string output, string kind)
    {
        using var image = Image.FromStream(input);
        if (!Useful(image)) throw new InvalidDataException("Placeholder de arte sem conteúdo.");
        if ((long)image.Width * image.Height > 50_000_000) throw new InvalidDataException();
        double scale = kind == "icon" ? Math.Min(1, 256.0 / Math.Max(image.Width, image.Height)) : kind == "cover" ? Math.Min(1, Math.Min(600.0 / image.Width, 900.0 / image.Height)) : Math.Min(1, Math.Min(2560.0 / image.Width, 1440.0 / image.Height));
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
