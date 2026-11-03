using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Forms;

namespace LazyGaming;

// Independent browser session: opening settings never takes controller ownership.
public sealed class HubSession
{
    readonly object gate = new();
    readonly Dictionary<string, DateTime> tokens = [];
    readonly string cookie = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    public string Issue() { lock (gate) {
        foreach (var expired in tokens.Where(p => p.Value < DateTime.UtcNow).Select(p => p.Key).ToArray()) tokens.Remove(expired);
        if (tokens.Count >= 16) tokens.Clear();
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)); tokens[token] = DateTime.UtcNow.AddMinutes(1); return token;
    } }
    public string? Redeem(string token) { lock (gate) { return tokens.Remove(token, out var expiry) && expiry > DateTime.UtcNow ? cookie : null; } }
    public bool Authenticate(string supplied) => Security.Equal(cookie, supplied);
}

public sealed class PcHub(GameLibrary library, GameService games, Func<Task> enrich)
{
    readonly SemaphoreSlim scan = new(1, 1);
    public bool Busy { get; private set; }
    public string? Notice { get; private set; }
    public DateTime? NextScan { get; private set; }
    public async Task Index(Action? change = null) {
        if (!await scan.WaitAsync(0)) throw new InvalidOperationException("Uma busca já está em andamento.");
        Busy = true;
        try { await games.Rescan(change); Notice = "Biblioteca atualizada."; }
        catch (Exception e) { Notice = e is InvalidOperationException ? e.Message : "A busca não foi concluída. Confira as pastas e tente novamente."; throw; }
        finally { Busy = false; scan.Release(); }
        // Artwork downloads must not hold the game service or delay configuration saves.
        _ = EnrichSafely();
    }
    async Task EnrichSafely() { try { await enrich(); } catch { } }
    public async Task AutomaticTick(DateTime now) {
        var preferences = library.Settings.Read();
        if (!preferences.Automatic) { NextScan = null; return; }
        NextScan ??= now.AddMinutes(preferences.IntervalMinutes);
        if (now < NextScan) return;
        try { await Index(); }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException) { }
        NextScan = now.AddMinutes(preferences.IntervalMinutes);
    }
    public async Task Automatic(CancellationToken cancellation) {
        while (!cancellation.IsCancellationRequested) {
            try {
                await AutomaticTick(DateTime.UtcNow);
                await Task.Delay(TimeSpan.FromSeconds(10), cancellation);
            } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) {
                Notice = "Configuração de busca indisponível. Confira o cadastro no PC.";
                await Task.Delay(TimeSpan.FromSeconds(10), cancellation);
            }
        }
    }
    public object Snapshot(ArtworkStore art) {
        var preferences = library.Settings.Read();
        return new {
            preferences, busy = Busy, notice = Notice, lastScan = library.LastScan, nextScan = NextScan,
            folders = preferences.Folders.Select(f => new { path = f.Path, enabled = f.Enabled, available = Directory.Exists(f.Path) }),
            steamPath = library.SteamPath,
            games = library.Discovered.Concat(library.Games).DistinctBy(g => g.Id).Select(g => new {
                g.Id, name = library.Metadata.DisplayName(g), origin = g.Steam ? "Steam" : g.LocalMetadata != null ? "Cadastro local" : "Pasta automática",
                executable = g.ProcessPath, folder = g.Folder, contentPath = g.ProcessArgument,
                available = File.Exists(g.ProcessPath), enabled = library.Games.Any(item => item.Id == g.Id),
                cover = art.Url(g, "cover"), metadata = library.Metadata.For(g)
            }),
            registrations = new LocalGameStore(library.Directory).Inspect(), notes = library.Notes
        };
    }
    public void Map(WebApplication app, ArtworkStore art, HubSession session, int port) {
        T Body<T>(HttpContext ctx) => JsonSerializer.Deserialize<T>((string)ctx.Items["body"]!, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidOperationException("Pedido vazio.");
        app.MapPost("/hub-session", async (HttpContext ctx) => {
            if (ctx.Connection.RemoteIpAddress == null || !IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress) || ctx.Request.Headers.Origin != $"http://127.0.0.1:{port}") return Results.StatusCode(403);
            var request = await ctx.Request.ReadFromJsonAsync<HubToken>();
            var cookie = session.Redeem(request?.Token ?? "");
            if (cookie == null) return Results.StatusCode(403);
            ctx.Response.Cookies.Append("lg-hub", cookie, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/" });
            return Results.Ok(new { accepted = true });
        });
        app.MapPost("/api/hub/open", () => Results.Ok(new { url = $"http://127.0.0.1:{port}/hub.html#" + session.Issue() }));
        app.MapPost("/api/hub/launch", () => {
            var url = $"http://127.0.0.1:{port}/hub.html#" + session.Issue();
            _ = Task.Run(() => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { } });
            return Results.Ok(new { accepted = true });
        });
        app.MapGet("/api/hub", () => Results.Ok(Snapshot(art)));
        app.MapPost("/api/hub/scan", async () => { await Index(); return Results.Ok(Snapshot(art)); });
        app.MapPost("/api/hub/settings", async (HttpContext ctx) => {
            var preferences = Body<LibraryPreferences>(ctx);
            await Index(() => library.Settings.Save(preferences)); NextScan = null; return Results.Ok(Snapshot(art));
        });
        app.MapPost("/api/hub/games/{id}/enabled", async (string id, HttpContext ctx) => {
            var request = Body<HubEnabled>(ctx);
            if (!library.Discovered.Any(g => g.Id == id) && !library.Games.Any(g => g.Id == id)) throw new InvalidOperationException("Jogo desconhecido.");
            await Index(() => {
                var preferences = library.Settings.Read(); var excluded = (preferences.Excluded ?? []).ToHashSet();
                if (request.Enabled) excluded.Remove(id); else excluded.Add(id);
                library.Settings.Save(preferences with { Excluded = excluded.ToArray() });
            });
            return Results.Ok(Snapshot(art));
        });
        app.MapPost("/api/hub/import", async (HttpContext ctx) => {
            var request = Body<HubImport>(ctx);
            await Index(() => {
                var store = new LocalGameStore(library.Directory);
                if (!string.IsNullOrWhiteSpace(request.Manifest)) { store.Import(request.Manifest); return; }
                var executable = request.Executable ?? "";
                var metadata = new GameMetadata(null, request.Title ?? "", null, [], [], "", DateTime.UtcNow, Source: "local", Artwork: new(request.Cover ?? "", request.Backdrop ?? "", "", ""));
                var registration = new LocalGameRegistration(executable, Path.GetDirectoryName(executable) ?? "", metadata, ContentPath: string.IsNullOrWhiteSpace(request.ContentPath) ? null : request.ContentPath);
                // Same import validation as the CLI. No commands or arbitrary arguments.
                var temp = Path.Combine(Path.GetTempPath(), "lg-import-" + Guid.NewGuid().ToString("N") + ".json");
                try { File.WriteAllText(temp, JsonSerializer.Serialize(registration)); store.Import(temp); }
                finally { File.Delete(temp); }
            });
            return Results.Ok(Snapshot(art));
        });
        app.MapPost("/api/hub/repair", async (HttpContext ctx) => {
            var request = Body<HubRepair>(ctx);
            await Index(() => {
                var path = Path.Combine(library.Directory, "library", "local-games.json");
                var entries = LibraryJson.Read<List<LocalGameRegistration>>(path, () => []);
                var item = entries.SingleOrDefault(e => (e.Id ?? GameLibrary.LocalId(e.WorkingDirectory)) == request.Id) ?? throw new InvalidOperationException("Cadastro local não encontrado.");
                var artRoot = Path.Combine(library.Directory, "library"); var images = item.Metadata.Artwork!;
                var updated = item with { Executable = request.Executable, WorkingDirectory = Path.GetDirectoryName(request.Executable) ?? "", Id = null,
                    ContentPath = string.IsNullOrWhiteSpace(request.ContentPath) ? null : request.ContentPath,
                    Metadata = item.Metadata with { Artwork = images with { Cover = string.IsNullOrEmpty(images.Cover) ? "" : Path.Combine(artRoot, images.Cover), Backdrop = string.IsNullOrEmpty(images.Backdrop) ? "" : Path.Combine(artRoot, images.Backdrop) } } };
                var temp = Path.Combine(Path.GetTempPath(), "lg-repair-" + Guid.NewGuid().ToString("N") + ".json");
                try { File.WriteAllText(temp, JsonSerializer.Serialize(updated)); new LocalGameStore(library.Directory).Import(temp, request.Id); }
                finally { File.Delete(temp); }
            });
            return Results.Ok(Snapshot(art));
        });
        app.MapPost("/api/hub/pick", async (HttpContext ctx) => {
            var request = Body<HubPick>(ctx);
            if (request.Kind is not ("folder" or "executable" or "manifest" or "image" or "content")) throw new InvalidOperationException("Escolha de arquivo inválida.");
            var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() => {
                try {
                    if (request.Kind == "folder") { using var dialog = new FolderBrowserDialog { Description = "Escolha a pasta dos jogos", UseDescriptionForTitle = true }; result.SetResult(dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null); }
                    else { using var dialog = new OpenFileDialog { Filter = request.Kind switch { "executable" => "Jogo (*.exe)|*.exe", "manifest" => "Ficha de jogo (*.json)|*.json", "image" => "Imagens|*.jpg;*.jpeg;*.png;*.webp", _ => "Conteúdo do jogo|*.nup;main" } }; result.SetResult(dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null); }
                } catch (Exception e) { result.TrySetException(e); }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
            return Results.Ok(new { path = await result.Task });
        });
    }
    record HubToken(string Token);
    record HubEnabled(bool Enabled);
    record HubImport(string? Title, string? Executable, string? Cover, string? Backdrop, string? ContentPath, string? Manifest);
    record HubRepair(string Id, string Executable, string? ContentPath);
    record HubPick(string Kind);
}
