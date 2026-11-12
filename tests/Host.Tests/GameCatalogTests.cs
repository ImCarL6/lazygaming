using LazyGaming;
using System.Net;
using System.Text.Json;
using System.Drawing;
using System.Drawing.Imaging;

static class GameCatalogTests
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static async Task Live()
    {
        var root = Path.GetFullPath("outputs/catalog-20261111/live-editions-final"); Directory.CreateDirectory(root);
        var catalog = new GameCatalog(root);
        foreach (var query in new[] { "The Legend of Zelda: Ocarina of Time", "Resident Evil 4", "Shadow of the Colossus", "Hollow Knight" }) {
            var result = await catalog.Search(query);
            Console.WriteLine(JsonSerializer.Serialize(new { query, result }));
            if (query.StartsWith("The Legend")) {
                var original = result.Matches.Single(m => m.Id == "wiki-Q213911");
                Check(result.Matches.Any(m => m.Metadata.Year == 2026 && GameCatalog.BaseTitle(m.Metadata.Title) == "The Legend of Zelda: Ocarina of Time"), "Zelda original e remake de 2026 na mesma busca");
                var entry = await catalog.Details(original.Id);
                var cover = await catalog.Image(entry.Id, "cover"); var backdrop = await catalog.Image(entry.Id, "backdrop");
                Console.WriteLine(JsonSerializer.Serialize(new { entry, cover, backdrop }));
                Check(entry.Metadata.Year == 1998 && entry.Metadata.Platforms?.Length > 0 && entry.Metadata.Developers.Length > 0 && cover != null && backdrop != null, "Zelda original: dados públicos e capa reais");
                File.WriteAllText(Path.Combine(root, "zelda-result.json"), JsonSerializer.Serialize(new { entry, cover, backdrop }, new JsonSerializerOptions { WriteIndented = true }));
            }
            if (query == "Resident Evil 4") Check(result.Matches.Any(m => m.Metadata.Year == 2005) && result.Matches.Any(m => m.Metadata.Year == 2023), "Resident Evil 4 original e remake na mesma busca");
            if (query.StartsWith("The Legend") || query == "Resident Evil 4") foreach (var match in result.Matches.Where(m => GameCatalog.BaseTitle(m.Metadata.Title) == query))
                Check(await catalog.Image(match.Id, "cover") != null, "cada edição tem uma capa pública que pode ser baixada: " + match.Metadata.Year);
        }
    }
    public static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "lg-catalog-tests-" + Guid.NewGuid().ToString("N"));
        try {
            var handler = new CatalogHttp(); using var http = new HttpClient(handler);
            var catalog = new GameCatalog(root, http);
            var results = await catalog.Search("Ocarina of Time");
            Check(results.Matches.Length == 3, "Steam + jogos de console, sem personagem nem série");
            var original = results.Matches.Single(m => m.Id == "wiki-Q213911");
            Check(original.Metadata.Year == 1998 && original.Metadata.Platforms!.Contains("Nintendo 64") && original.Metadata.Developers.Contains("Nintendo"), "ano, plataforma e desenvolvedor vêm da entidade correta");
            Check(results.Matches.Any(m => m.Id == "wiki-Q642526" && m.Metadata.Year == 2011), "remake permanece separado do original");
            var detailed = await catalog.Details(original.Id);
            Check(detailed.Backdrop != null && detailed.Metadata.Description == "Aventura de Link.", "descrição e fundo pertencem ao jogo escolhido");
            var concurrent = await Task.WhenAll(catalog.Image(detailed.Id, "backdrop"), catalog.Image(detailed.Id, "backdrop"));
            var image = concurrent[0]; Check(concurrent[0] == concurrent[1], "pedidos simultâneos compartilham a mesma arte");
            Check(image != null && handler.ImageRequests == 1 && handler.UserAgent, "arte é baixada pelo auxiliar com identificação");
            using (var bmp = Image.FromFile(image!)) Check(bmp.Width == 800 && bmp.Height == 450, "imagem preservada sem ampliar");
            Check(await catalog.Image(detailed.Id, "backdrop") == image && handler.ImageRequests == 1, "arte em cache não repete download");
            handler.Offline = true;
            var offline = await new GameCatalog(root, http).Search("Ocarina of Time");
            Check(offline.Cached && offline.Matches.Length == 3, "cache persiste no reinício e funciona offline");
            var partial = await catalog.Search("Zelda");
            Check(partial.Matches.Length == 0 && partial.Warnings.Length == 2, "falha total não fabrica correspondências");
            handler.Offline = false; handler.SteamOffline = true;
            partial = await catalog.Search("Zelda original");
            Check(partial.Matches.Length == 2 && partial.Warnings.Length == 1, "falha Steam não bloqueia jogos de console");
            handler.SteamOffline = false; handler.Dlc = true;
            var noDlc = await catalog.Search("Only complete games");
            Check(noDlc.Matches.All(m => !m.Id.StartsWith("steam-")), "DLC e trilha sonora não entram na lista de jogos");
            var invalid = false;
            foreach (var id in new[] { "../pairing", "https://localhost", "wiki-../../x" }) {
                invalid = false; try { await catalog.Image(id, "cover"); } catch (InvalidOperationException) { invalid = true; }
                Check(invalid, "identidade não pode ler caminhos ou buscar endereço arbitrário");
            }
            Check(!GameCatalog.AllowedImage("https://upload.wikimedia.org.evil.test/a.jpg") && !GameCatalog.AllowedImage("http://127.0.0.1/a.jpg") && !GameCatalog.AllowedImage("https://user@upload.wikimedia.org/a.jpg"), "fontes de imagem restritas a HTTPS e hosts exatos");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var aborted = false; try { await catalog.Search("Cancellation", cancelled.Token); } catch (OperationCanceledException) { aborted = true; }
            Check(aborted, "cancelamento não grava resultado parcial");
            Check(GameCatalog.BaseTitle("Resident Evil 4 (2023 video game)") == GameCatalog.BaseTitle("Resident Evil 4 (2005)") && GameCatalog.BaseTitle("Ocarina of Time 3D") != GameCatalog.BaseTitle("Ocarina of Time"), "ano agrupa títulos homônimos sem perder edições 3D/HD");
            handler.Dlc = false; handler.Linked = true;
            var linked = await catalog.Search("Linked editions");
            Check(linked.Matches.Length == 2 && linked.Matches.All(m => m.Id.StartsWith("wiki-")), "a mesma edição em Steam e Wikipédia não é duplicada quando há vínculo público comprovado");
            Console.WriteLine("PASS: catálogo Steam/console, identidade original/remake, metadados, fundo, cache durável, falhas parciais, cancelamento e fontes restritas.");
        } finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    sealed class CatalogHttp : HttpMessageHandler
    {
        public bool Offline, SteamOffline, Dlc, UserAgent, Linked;
        public int ImageRequests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = request.RequestUri!;
            if (Offline || SteamOffline && url.Host == "store.steampowered.com") throw new HttpRequestException("offline");
            UserAgent |= request.Headers.UserAgent.Count > 0;
            if (url.Host == "upload.wikimedia.org") {
                ImageRequests++; using var image = new Bitmap(800, 450); using (var g = Graphics.FromImage(image)) { g.Clear(Color.Navy); g.FillEllipse(Brushes.White, 100, 100, 500, 300); }
                using var bytes = new MemoryStream(); image.Save(bytes, ImageFormat.Jpeg);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes.ToArray()) });
            }
            object value;
            if (url.AbsolutePath.Contains("storesearch")) value = new { items = new[] { new { id = 123, name = "Ocarina indie", type = "app", tiny_image = "https://shared.fastly.steamstatic.com/a.jpg" } } };
            else if (url.AbsolutePath.Contains("appdetails")) value = new Dictionary<string, object> { ["123"] = new { success = true, data = new { type = Dlc ? "dlc" : "game", name = "Ocarina indie" } } };
            else if (url.Host == "www.wikidata.org") {
                if (url.Query.Contains("props=labels")) value = new { entities = new Dictionary<string, object> { ["Q64"] = new { labels = new { en = new { value = "Nintendo 64" } } }, ["Q1"] = new { labels = new { pt = new { value = "Nintendo" } } } } };
                else {
                    object Id(string id) => new { mainsnak = new { datavalue = new { value = new { id } } } };
                    object Game(int year) => new { claims = new Dictionary<string, object> { ["P31"] = new[] { Id("Q7889") }, ["P178"] = new[] { Id("Q1") }, ["P400"] = new[] { Id("Q64") }, ["P577"] = new[] { new { mainsnak = new { datavalue = new { value = new { time = $"+{year}-01-01T00:00:00Z" } } } } }, ["P1733"] = Linked && year == 1998 ? new[] { new { mainsnak = new { datavalue = new { value = "123" } } } } : [] } };
                    value = new { entities = new Dictionary<string, object> { ["Q213911"] = Game(1998), ["Q642526"] = Game(2011), ["Q999"] = new { claims = new Dictionary<string, object> { ["P31"] = new[] { Id("Q5") } } } } };
                }
            } else if (url.Query.Contains("prop=imageinfo")) value = new { query = new { pages = new Dictionary<string, object> { ["2"] = new { imageinfo = new[] { new { width = 800, height = 450, url = "https://upload.wikimedia.org/zelda.jpg" } } } } } };
            else if (url.Query.Contains("prop=images")) value = new { query = new { pages = new Dictionary<string, object> { ["2"] = new { images = new[] { new { title = "File:Zelda battle.jpg" }, new { title = "File:Flag of Japan.svg" } } } } } };
            else {
                object Page(string title, string q) => new { title, pageprops = new { wikibase_item = q }, extract = "Aventura de Link.", original = new { source = "https://upload.wikimedia.org/cover.jpg" } };
                value = new { query = new { pages = new Dictionary<string, object> { ["1"] = Page("The Legend of Zelda: Ocarina of Time", "Q213911"), ["2"] = Page("The Legend of Zelda: Ocarina of Time 3D", "Q642526"), ["3"] = Page("Link", "Q999") } } };
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) });
        }
    }
}
