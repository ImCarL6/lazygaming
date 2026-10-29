using LazyGaming;
using System.Net;
using System.Text.Json;

static class LibraryMetadataTests
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "lg-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var folder = Path.Combine(root, "New Adventure"); Directory.CreateDirectory(Path.Combine(folder, "bin"));
            var wrapper = Path.Combine(folder, "New Adventure.exe"); File.WriteAllText(wrapper, "wrapper");
            var executable = Path.Combine(folder, "bin", "NA.exe"); File.WriteAllText(executable, "game");
            File.WriteAllText(Path.Combine(folder, "bin", "UnityCrashHandler64.exe"), "helper");
            var library = new GameLibrary(root, false, Path.Combine(root, "no-steam"), [folder]);
            var game = library.Games.Single();
            var discovery = new GameLibrary(Path.Combine(root, "discovery-cache"), false, Path.Combine(root, "no-steam"), [root], true);
            Check(discovery.Games.Count == 1, "raiz genérica encontra instalação sem cadastrar título");
            var added = Path.Combine(root, "Added Later"); Directory.CreateDirectory(added); File.WriteAllText(Path.Combine(added, "Added.exe"), "fixture");
            discovery.Refresh(); Check(discovery.Games.Count == 2 && discovery.Games.Any(g => g.Name == "Added Later"), "nova busca descobre jogo adicionado depois sem alterar código");
            Check(game.Name == "New Adventure" && game.ProcessPath == executable && game.Launch == wrapper && game.LaunchDirectory == folder, "descobrir jogo abreviado separando launcher e processo, sem executar nada");
            var shipping = Path.Combine(folder, "bin", "Project-Win64-Shipping.exe"); File.WriteAllText(shipping, "unreal");
            Check(GameLibrary.SelectLocalProcess(folder, GameLibrary.Exes(folder)) == shipping, "Unreal acompanha Shipping em vez do bootstrap");
            var handler = new CatalogHandler(); using var client = new HttpClient(handler);
            var store = new GameMetadataStore(root, client);
            await store.EnsureAll([game]);
            Check(store.For(game)?.StoreId == "123" && store.For(game)?.Year == 2021, "nome normalizado exato seleciona metadados públicos");
            var launch = game.Launch; var process = game.ProcessPath;
            store.Rename(game, "Meu jogo"); await store.Assign(game, "456");
            Check(store.DisplayName(game) == "Meu jogo" && store.For(game)?.Title == "Another Game", "nome pessoal independente do título dos metadados");
            Check(game.Launch == launch && game.ProcessPath == process && !game.Steam, "atribuição manual nunca modifica origem ou executável");
            store.Rename(game, null); Check(store.DisplayName(game) == "Another Game", "restaurar nome usa o título escolhido");
            var reloaded = new GameMetadataStore(root, client);
            await reloaded.EnsureAll([game]); Check(reloaded.For(game)?.StoreId == "456", "escolha manual persiste e não é sobrescrita pelo automático");
            Check((await store.Search("https://store.steampowered.com/app/123/"))[0].Id == "123", "aceitar link público sem visitar origem arbitrária");
            bool rejected = false; try { await store.Assign(game, "../other"); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && store.For(game)?.StoreId == "456", "ID inválido não altera cache nem vira caminho/URL");
            handler.Fail = true; rejected = false;
            try { await store.Assign(game, "123"); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && store.For(game)?.StoreId == "456", "falha de rede conserva metadados anteriores");
            Check(!GameMetadataStore.Exact("New Adventure 2", "New Adventure") && GameMetadataStore.Exact("Otherside Tapes: Favela", "Otherside Tapes - Favela"), "não confundir sequências; pontuação não muda identidade");
            var assetJson = """{"response":{"store_items":[{"appid":123,"assets":{"library_capsule_2x":"abc/library_capsule_2x.jpg","library_capsule":"abc/library_capsule.jpg","library_hero_2x":"https://evil.invalid/a.jpg","library_hero":"../escape.jpg"}}]}}""";
            var urls = PublicLibraryAssets.Parse(assetJson, "123");
            Check(urls["cover"].Length == 2 && urls["cover"][0].EndsWith("abc/library_capsule_2x.jpg") && urls["backdrop"].Length == 0, "arte pública prioriza alta resolução, recusa URL externa e travessia");
            Check(PublicLibraryAssets.Parse(assetJson, "999").Count == 0, "arte de outro aplicativo não é atribuída");
            handler.Fail = false; handler.Ambiguous = true;
            var uncertain = game with { Id = "uncertain" }; await store.EnsureAll([uncertain]);
            Check(store.For(uncertain) == null, "duas correspondências exatas exigem escolha manual");
            handler.Ambiguous = false; handler.Dlc = true;
            var dlc = game with { Id = "dlc" }; await store.EnsureAll([dlc]);
            Check(store.For(dlc) == null, "DLC não é aceito como identidade de jogo");
            Console.WriteLine("PASS: descoberta genérica, launcher/processo separados, metadados exatos, persistência, escolha manual, ambiguidade e rede sem alterar execução.");
        } finally { Directory.Delete(root, true); }
    }
    sealed class CatalogHandler : HttpMessageHandler
    {
        public bool Fail, Ambiguous, Dlc;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Fail) throw new HttpRequestException("offline");
            string json;
            if (request.RequestUri!.AbsolutePath.Contains("storesearch")) json = JsonSerializer.Serialize(new { items = Ambiguous ? new[] { new { id = 123, name = "New Adventure", type = "app" }, new { id = 456, name = "New Adventure", type = "app" } } : new[] { new { id = 123, name = "New Adventure", type = "app" } } });
            else { var id = request.RequestUri.Query.Contains("456") ? "456" : "123";
                json = JsonSerializer.Serialize(new Dictionary<string, object> { [id] = new { success = true, data = new { type = Dlc ? "dlc" : "game", name = id == "456" ? "Another Game" : "New Adventure", developers = new[] { "Studio" }, genres = new[] { new { description = "Adventure" } }, release_date = new { coming_soon = false, date = "10 Oct, 2021" } } } }); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
