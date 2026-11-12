using LazyGaming;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Text.Json;

static class WallpaperTests
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static async Task Live() {
        var root = Path.GetFullPath("outputs/wallpapers-20261111/live"); Directory.CreateDirectory(root);
        var catalog = new WallpaperCatalog(root);
        var result = await catalog.Search("The Legend of Zelda Ocarina of Time", null);
        Check(result.Images.Length > 1 && result.Images.Any(i => i.Source == "Wallhaven"), "galeria real do Zelda disponível");
        var chosen = result.Images.OrderByDescending(i => i.Width >= 3840 && i.Height >= 2160).ThenBy(i => Math.Abs((double)i.Width / i.Height - 16.0 / 9)).First();
        var image = await catalog.Select(chosen.Id);
        File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new { result, chosen, image }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { count = result.Images.Length, result.Warnings, chosen, image }));
        var steam = await catalog.Search("Hollow Knight", "367520");
        Console.WriteLine(JsonSerializer.Serialize(new { steamImages = steam.Images.Count(i => i.Source == "Steam"), count = steam.Images.Length, steam.Warnings }));
    }
    public static async Task Run() {
        var root = Path.Combine(Path.GetTempPath(), "lg-wallpapers-" + Guid.NewGuid().ToString("N"));
        try {
            var handler = new ImagesHttp(); using var client = new HttpClient(handler); var catalog = new WallpaperCatalog(root, client);
            var result = await catalog.Search("Adventure", "123");
            Check(result.Images.Length == 4 && result.HasMore, "galeria junta Wallhaven, Commons e Steam e oferece mais páginas");
            Check(result.Images.All(i => i.Width >= 1920 && i.Height >= 1080), "retratos, imagens pequenas e conteúdo não SFW são excluídos");
            Check(handler.Sfw && handler.UserAgent, "Wallhaven é consultado com filtro SFW e identificação");
            var wallpaper = result.Images.First(i => i.Source == "Wallhaven");
            var files = await Task.WhenAll(catalog.ImageFile(wallpaper.Id, "full"), catalog.ImageFile(wallpaper.Id, "full"));
            Check(files[0] != null && files[0] == files[1] && handler.FullDownloads == 1, "download único por imagem concorrente");
            var selected = await catalog.Select(wallpaper.Id);
            Check(selected.Width == 3840 && selected.Height == 2160, "fundo 4K preservado, sem ampliar");
            var library = new GameLibrary(root, true); var game = library.Games[0] with { ArtworkId = library.Games[0].Id };
            var art = new ArtworkStore(library); var original = Path.Combine(art.Root, game.Id, "backdrop.jpg"); Directory.CreateDirectory(Path.GetDirectoryName(original)!); File.WriteAllBytes(original, handler.Picture);
            var before = File.ReadAllBytes(original); var fallback = art.Url(game, "backdrop");
            art.Wallpapers.Save(game.Id, selected);
            var custom = art.Url(game, "backdrop");
            Check(custom != fallback && art.FileFor(game.Id, "backdrop")!.Contains("selected"), "a biblioteca compartilhada PC/TV usa a escolha, sem trocar arquivo original");
            Check(new WallpaperOverrides(root).Image(game.Id) != null, "escolha persiste no reinício");
            File.Delete(files[0]!);
            handler.Offline = true;
            Check((await new WallpaperCatalog(root, client).Search("Adventure", "123")).Cached, "busca guardada funciona offline");
            Check(art.FileFor(game.Id, "backdrop") != null, "fundo escolhido independe do cache temporário e da rede");
            var offline = await catalog.Search("Other adventure", null);
            Check(offline.Warnings.Length == 2 && offline.Images.Length == 0, "indisponibilidade das fontes é explícita e não inventa fundos");
            art.Wallpapers.Save(game.Id, null);
            Check(art.Url(game, "backdrop") == fallback && File.ReadAllBytes(original).SequenceEqual(before), "restaurar automático conserva o fundo anterior e a instalação");
            handler.Offline = false; handler.WallhavenOffline = true;
            var partial = await catalog.Search("Another adventure", null);
            Check(partial.Images.Length == 1 && partial.Images[0].Source == "Wikimedia Commons" && partial.Warnings.Length == 1, "uma fonte indisponível não bloqueia as outras");
            Check(!WallpaperCatalog.AllowedImage("https://w.wallhaven.cc.evil.test/a.jpg") && !WallpaperCatalog.AllowedImage("http://127.0.0.1/a.jpg") && !WallpaperCatalog.AllowedImage("https://x@w.wallhaven.cc/a.jpg"), "download não aceita host externo, HTTP ou credencial");
            var invalid = false; try { await catalog.Select("../../pairing.json"); } catch (InvalidOperationException) { invalid = true; }
            Check(invalid, "identidade nunca vira caminho ou URL arbitrário");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); invalid = false;
            try { await catalog.Search("Cancel", null, cancellation:cancelled.Token); } catch (OperationCanceledException) { invalid = true; }
            Check(invalid, "cancelamento é propagado");
            Console.WriteLine("PASS: galeria multi-fonte/HD/SFW, downloads restritos, 4K, cache/offline, persistência, restauração e isolamento da arte por jogo.");
        } finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    sealed class ImagesHttp : HttpMessageHandler {
        public bool Offline, WallhavenOffline, Sfw, UserAgent; public int FullDownloads;
        public byte[] Picture { get; }
        public ImagesHttp() {
            using var bitmap = new Bitmap(3840, 2160); using (var g = Graphics.FromImage(bitmap)) { g.Clear(Color.MidnightBlue); g.FillEllipse(Brushes.Gold, 500, 500, 2000, 1000); }
            using var bytes = new MemoryStream(); bitmap.Save(bytes, ImageFormat.Jpeg); Picture = bytes.ToArray();
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            ct.ThrowIfCancellationRequested(); var url = request.RequestUri!;
            if (Offline || WallhavenOffline && url.Host == "wallhaven.cc") throw new HttpRequestException("offline");
            UserAgent |= request.Headers.UserAgent.Count > 0;
            if (url.Host is "w.wallhaven.cc" or "th.wallhaven.cc" or "upload.wikimedia.org" or "shared.fastly.steamstatic.com") {
                if (url.Host == "w.wallhaven.cc") FullDownloads++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Picture) });
            }
            object data;
            if (url.Host == "wallhaven.cc") {
                Sfw |= url.Query.Contains("purity=100") && url.Query.Contains("atleast=1920x1080");
                object Entry(string id, int w, int h, string purity = "sfw") => new { id, dimension_x = w, dimension_y = h, purity, path = "https://w.wallhaven.cc/full/"+id+".jpg", url = "https://wallhaven.cc/w/"+id, thumbs = new { large = "https://th.wallhaven.cc/"+id+".jpg" } };
                data = new { data = new[] { Entry("a",3840,2160), Entry("b",1920,1080), Entry("c",1080,1920), Entry("d",1280,720), Entry("e",3840,2160,"nsfw") }, meta = new { current_page = 1, last_page = 3 } };
            } else if (url.Host == "commons.wikimedia.org") data = new { query = new { pages = new Dictionary<string,object> { ["1"] = new { title = "File:Adventure.jpg", imageinfo = new[] { new { width = 3840, height = 2160, mime = "image/jpeg", url = "https://upload.wikimedia.org/adventure.jpg", thumburl = "https://upload.wikimedia.org/thumbnail.jpg", descriptionurl = "https://commons.wikimedia.org/wiki/File:Adventure.jpg", extmetadata = new { Artist = new { value = "<b>Artista</b>" }, LicenseShortName = new { value = "CC BY-SA" } } } } } } } };
            else data = new Dictionary<string,object> { ["123"] = new { success = true, data = new { type = "game", screenshots = new[] { new { path_full = "https://shared.fastly.steamstatic.com/shot.jpg", path_thumbnail = "https://shared.fastly.steamstatic.com/thumb.jpg" } } } } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data)) });
        }
    }
}
