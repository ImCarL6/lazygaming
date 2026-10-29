using System.Text.Json;
using System.Text.RegularExpressions;

namespace LazyGaming;

public static class PublicLibraryAssets
{
    public static Dictionary<string, string[]> Parse(string json, string id)
    {
        if (!uint.TryParse(id, out _)) return [];
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("response", out var response) || !response.TryGetProperty("store_items", out var items)) return [];
        var item = items.EnumerateArray().FirstOrDefault(e => e.TryGetProperty("appid", out var app) && app.ToString() == id);
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("assets", out var assets)) return [];
        string[] Urls(params string[] keys) => keys.Select(k => assets.TryGetProperty(k, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null)
            .Where(p => p != null && !p.Contains("..") && Regex.IsMatch(p, @"^(?:[a-zA-Z0-9_-]+/)*[a-zA-Z0-9_.-]+\.(?:jpg|png|webp)$"))
            .Select(p => $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{id}/{p}").Distinct().ToArray();
        return new() { ["cover"] = Urls("library_capsule_2x", "library_capsule"), ["backdrop"] = Urls("library_hero_2x", "library_hero") };
    }
    public static async Task<Dictionary<string, string[]>> Load(HttpClient http, string directory, string id)
    {
        var file = Path.Combine(directory, "store-assets.json");
        try {
            if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromDays(7)) return Parse(File.ReadAllText(file), id);
            var input = JsonSerializer.Serialize(new { ids = new[] { new { appid = uint.Parse(id) } }, context = new { language = "brazilian", country_code = "BR" }, data_request = new { include_assets = true } });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            using var response = await http.GetAsync("https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json=" + Uri.EscapeDataString(input), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 2_000_000) return [];
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token); using var buffer = new MemoryStream();
            var bytes = new byte[8192]; int count;
            while ((count = await stream.ReadAsync(bytes, timeout.Token)) > 0) { if (buffer.Length + count > 2_000_000) return []; buffer.Write(bytes, 0, count); }
            var json = System.Text.Encoding.UTF8.GetString(buffer.ToArray()); var parsed = Parse(json, id);
            if (parsed.Values.Any(urls => urls.Length > 0)) { File.WriteAllText(file + ".tmp", json); File.Move(file + ".tmp", file, true); }
            return parsed;
        }
        catch (Exception e) when (e is IOException or HttpRequestException or OperationCanceledException or JsonException or UnauthorizedAccessException) { return []; }
    }
}
