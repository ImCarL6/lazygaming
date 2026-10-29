using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LazyGaming;

public record CatalogMatch(string Id, string Name, string? Image = null);
public record GameMetadata(string StoreId, string Title, int? Year, string[] Developers, string[] Genres, string SourceUrl, DateTime CheckedAt);
public record GameCustomization(string? Name = null, GameMetadata? Metadata = null, bool Manual = false, DateTime AttemptedAt = default);

// Identidade pública e apresentação nunca autorizam abrir/fechar um processo.
public sealed class GameMetadataStore
{
    readonly object sync = new();
    readonly SemaphoreSlim updates = new(1, 1);
    readonly string file;
    readonly HttpClient http;
    Dictionary<string, GameCustomization> entries = [];
    public GameMetadataStore(string directory, HttpClient? client = null)
    {
        file = Path.Combine(directory, "library", "catalog.json");
        http = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        try { if (File.Exists(file)) entries = JsonSerializer.Deserialize<Dictionary<string, GameCustomization>>(File.ReadAllText(file)) ?? []; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
    }
    public GameCustomization Customization(Game game) { lock (sync) return entries.GetValueOrDefault(game.Id) ?? new(); }
    public GameMetadata? For(Game game) => Customization(game).Metadata;
    public string DisplayName(Game game) { var item = Customization(game); return item.Name ?? item.Metadata?.Title ?? game.Name; }
    public string? ArtworkId(Game game) => For(game)?.StoreId ?? game.ArtworkId;
    void Save(string id, GameCustomization item)
    {
        lock (sync) {
            var next = new Dictionary<string, GameCustomization>(entries) { [id] = item };
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(next)); File.Move(file + ".tmp", file, true);
            entries = next;
        }
    }
    public void Rename(Game game, string? name)
    {
        name = name?.Trim();
        if (name?.Length > 120 || name?.Any(char.IsControl) == true) throw new InvalidOperationException("Use um nome de até 120 caracteres.");
        lock (sync) Save(game.Id, Customization(game) with { Name = string.IsNullOrEmpty(name) ? null : name });
    }
    public static string Normalize(string name) => Regex.Replace(new string(WebUtility.HtmlDecode(name).Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant(), "[^a-z0-9]", "");
    public static bool Exact(string title, params string?[] names) => names.Any(n => n != null && Normalize(title) == Normalize(n));
    static string? StoreId(string text)
    {
        if (uint.TryParse(text, out uint id) && id > 0) return id.ToString();
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "store.steampowered.com") {
            var match = Regex.Match(uri.AbsolutePath, @"^/app/(\d+)(?:/|$)");
            if (match.Success && uint.TryParse(match.Groups[1].Value, out id) && id > 0) return id.ToString();
        }
        return null;
    }
    async Task<JsonDocument> Json(string url)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 2_000_000) throw new InvalidDataException();
        using var input = await response.Content.ReadAsStreamAsync();
        using var output = new MemoryStream();
        var buffer = new byte[8192]; int read;
        while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0) { if (output.Length + read > 2_000_000) throw new InvalidDataException(); output.Write(buffer, 0, read); }
        return JsonDocument.Parse(output.ToArray());
    }
    public async Task<CatalogMatch[]> Search(string query)
    {
        query = query.Trim();
        if (query.Length is < 2 or > 120 || query.Any(char.IsControl)) throw new InvalidOperationException("Digite o nome ou o link Steam do jogo.");
        try {
            if (StoreId(query) is string id) { var metadata = await Details(id); return metadata == null ? [] : [new(id, metadata.Title)]; }
            query = Regex.Replace(Regex.Replace(query, @"[^\p{L}\p{N}\s]", " "), @"\s+", " ").Trim();
            if (query.Length < 2) return [];
            using var document = await Json("https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(query) + "&l=english&cc=BR");
            if (!document.RootElement.TryGetProperty("items", out var items)) return [];
            return items.EnumerateArray().Where(e => e.TryGetProperty("type", out var type) && type.GetString() == "app")
                .Select(e => new CatalogMatch(e.GetProperty("id").ToString(), e.GetProperty("name").GetString() ?? ""))
                .Where(e => uint.TryParse(e.Id, out _) && e.Name.Length > 0).Take(12).ToArray();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException) {
            throw new InvalidOperationException("O catálogo está indisponível. Tente novamente; seus dados locais foram preservados.");
        }
    }
    public async Task<GameMetadata?> Details(string id)
    {
        if (StoreId(id) != id) throw new InvalidOperationException("Escolha um jogo válido no catálogo.");
        using var document = await Json($"https://store.steampowered.com/api/appdetails?appids={id}&l=english&cc=BR");
        if (!document.RootElement.TryGetProperty(id, out var entry) || !entry.TryGetProperty("success", out var success) || !success.GetBoolean() || !entry.TryGetProperty("data", out var data)) return null;
        if (!data.TryGetProperty("type", out var type) || type.GetString() is not ("game" or "mod")) return null;
        var title = data.GetProperty("name").GetString(); if (string.IsNullOrWhiteSpace(title)) return null;
        int? year = null;
        if (data.TryGetProperty("release_date", out var release) && release.TryGetProperty("coming_soon", out var coming) && !coming.GetBoolean() && release.TryGetProperty("date", out var date)) {
            var match = Regex.Match(date.GetString() ?? "", @"\b(19\d{2}|20\d{2})\b"); if (match.Success) year = int.Parse(match.Value);
        }
        var developers = data.TryGetProperty("developers", out var devs) ? devs.EnumerateArray().Select(e => e.GetString() ?? "").ToArray() : [];
        var genres = data.TryGetProperty("genres", out var tags) ? tags.EnumerateArray().Select(e => e.GetProperty("description").GetString() ?? "").ToArray() : [];
        return new(id, title, year, developers, genres, $"https://store.steampowered.com/app/{id}/", DateTime.UtcNow);
    }
    public async Task Assign(Game game, string id)
    {
        await updates.WaitAsync();
        try {
            GameMetadata? metadata;
            try { metadata = await Details(id); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException) { throw new InvalidOperationException("Não foi possível consultar esse jogo. Os metadados anteriores foram preservados."); }
            if (metadata == null) throw new InvalidOperationException("O catálogo não retornou um jogo válido.");
            lock (sync) Save(game.Id, Customization(game) with { Metadata = metadata, Manual = true, AttemptedAt = DateTime.UtcNow });
        } finally { updates.Release(); }
    }
    public async Task EnsureAll(IEnumerable<Game> games, bool retryMissing = false)
    {
        await updates.WaitAsync();
        try {
            foreach (var game in games.ToArray()) {
                var saved = Customization(game);
                if (saved.Manual || (!(retryMissing && saved.Metadata == null) && DateTime.UtcNow - saved.AttemptedAt < TimeSpan.FromHours(saved.Metadata == null ? 24 : 168))) continue;
                try {
                    GameMetadata? metadata = null;
                    if (game.ArtworkId is string known) metadata = await Details(known);
                    if (metadata == null && !game.Steam) {
                        var names = new[] { game.Name, game.Folder == null ? null : Path.GetFileName(game.Folder) };
                        var hints = new[] { Path.Combine(Path.GetDirectoryName(game.ProcessPath)!, "steam_appid.txt"), Path.Combine(game.Folder ?? Path.GetDirectoryName(game.ProcessPath)!, "steam_appid.txt") };
                        foreach (var hint in hints.Distinct()) if (File.Exists(hint) && new FileInfo(hint).Length < 32 && StoreId(File.ReadAllText(hint).Trim()) is string id) {
                            var candidate = await Details(id); if (candidate != null && Exact(candidate.Title, names)) { metadata = candidate; break; }
                        }
                        if (metadata == null) {
                            var candidates = (await Search(game.Name)).Where(c => Exact(c.Name, names)).ToArray();
                            if (candidates.Length == 1) { var candidate = await Details(candidates[0].Id); if (candidate != null && Exact(candidate.Title, names)) metadata = candidate; }
                        }
                    }
                    lock (sync) Save(game.Id, Customization(game) with { Metadata = metadata ?? saved.Metadata, AttemptedAt = DateTime.UtcNow });
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException) {
                    // Rede/cache de um título não bloqueia os demais nem remove o que já funciona.
                }
            }
        } finally { updates.Release(); }
    }
}
