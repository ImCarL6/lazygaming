using LazyGaming;

// Emulador: uma capa por jogo, arquivos do mesmo jogo viram escolha de idioma; o emulador sozinho não vira jogo.
static class EmulatorLibraryTests
{
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run() {
        var root = Path.Combine(Path.GetTempPath(), "lg-emu-" + Guid.NewGuid().ToString("N"));
        try {
            var emu = Path.Combine(root, "PokeDinner-Eglantine-2.5.0", "eglantine.exe"); Directory.CreateDirectory(Path.GetDirectoryName(emu)!); File.WriteAllText(emu, "");
            Directory.CreateDirectory(Path.Combine(root, "NUP"));
            foreach (var f in new[] { "Pokopia 2.0.1 PT-BR DualSense.nup", "Pokémon Pokopia + Update 2.0.1.nup", "Mario Kart World + Update 1.8.0.nup", "invalido.nup" }) File.WriteAllText(Path.Combine(root, "NUP", f), "");
            var found = EmulatorLibrary.Index(emu, root, f => !f.EndsWith("invalido.nup"));
            var poko = found.Single(g => g.Name == "Pokémon Pokopia");
            Assert(found.Count == 2 && found.Any(g => g.Name == "Mario Kart World" && g.Choices == null), "dois jogos, sem o arquivo inválido");
            Assert(poko.ChoiceLabel == "Idioma" && poko.Choices!.Length == 2 && poko.Choices[0].Id == "pt-br" && poko.Choices[1].Name == "Original", "Pokopia com Português primeiro e Original");
            Assert(poko.ProcessPath == emu && poko.LaunchArguments!.SequenceEqual(new[] { "-f", "-g", poko.Choices[0].ContentPath }), "abre pelo Eglantine com o conteúdo escolhido");
            Assert(EmulatorLibrary.Index(emu, root, f => !f.EndsWith("invalido.nup")).First(g => g.Name == poko.Name).Id == poko.Id, "id estável entre buscas");
            Assert(poko.LocalMetadata == null, "sem imagem ao lado, sem ficha local");
            File.WriteAllText(Path.Combine(root, "NUP", "Pokopia - capa.jpg"), "x"); File.WriteAllText(Path.Combine(root, "NUP", "Pokopia - fundo.png"), "x");
            var withArt = EmulatorLibrary.Index(emu, root, f => !f.EndsWith("invalido.nup")).First(g => g.Name == poko.Name);
            Assert(withArt.LocalMetadata?.Artwork is { } art && art.Cover.EndsWith("Pokopia - capa.jpg") && art.Backdrop.EndsWith("Pokopia - fundo.png"), "capa e fundo ao lado do jogo");
            Assert(EmulatorLibrary.Index(emu, root, f => !f.EndsWith("invalido.nup")).First(g => g.Name == "Mario Kart World").LocalMetadata == null, "arte de outro jogo não vaza");
            var metadata = new GameMetadataStore(root);
            metadata.EnsureAll([withArt]).GetAwaiter().GetResult();
            Assert(metadata.For(withArt)?.Source == "local", "imagens presentes têm prioridade");
            File.Delete(Path.Combine(root, "NUP", "Pokopia - capa.jpg")); File.Delete(Path.Combine(root, "NUP", "Pokopia - fundo.png"));
            var withoutArt = EmulatorLibrary.Index(emu, root, f => !f.EndsWith("invalido.nup")).First(g => g.Id == poko.Id);
            var reloadedMetadata = new GameMetadataStore(root);
            Assert(reloadedMetadata.Customization(withoutArt).Metadata?.Source == "local", "reproduz ficha antiga persistida");
            Assert(reloadedMetadata.For(withoutArt) == null && reloadedMetadata.ArtworkId(withoutArt) == poko.Id, "ficha antiga sem imagens permite busca pública após reiniciar");
            var library = new GameLibrary(root, test: true); library.Games.Clear(); library.Games.Add(withoutArt);
            var handler = new EncyclopediaHandler(); using var client = new HttpClient(handler);
            var store = new ArtworkStore(library, client); store.EnsureAll().GetAwaiter().GetResult();
            Assert(handler.ImageRequests == 1 && store.Url(withoutArt, "cover") != null && store.Url(withoutArt, "icon") != null && store.Url(withoutArt, "backdrop") == null, "download com identificação gera capa/ícone e mantém fundo padrão");
            store.EnsureAll().GetAwaiter().GetResult();
            Assert(handler.ImageRequests == 1 && File.ReadAllText(store.FileFor(poko.Id, "cover")! + ".source.json").Contains("wikipedia"), "cache com proveniência evita repetir download");
            Assert(GameLibrary.IsHelper("python.exe") && GameLibrary.IsHelper("pythonw.exe") && GameLibrary.IsHelper("pip3.13.exe") && !GameLibrary.IsHelper("Balatro.exe"), "ferramentas de programação não viram jogo");
        } finally { Directory.Delete(root, true); }
        Console.WriteLine("PASS: emuladores indexados por jogo, com escolha de idioma e conteúdo validado.");
        // Fonte pública real (informativo; sem rede o teste não falha).
        try {
            var cover = ArtworkStore.EncyclopediaCover(new HttpClient { Timeout = TimeSpan.FromSeconds(10) }, "Pokémon Pokopia").GetAwaiter().GetResult();
            Console.WriteLine("INFO capa pública: " + (cover?.Page ?? "não encontrada") + " " + cover?.Image);
            Assert(ArtworkStore.EncyclopediaCover(new HttpClient { Timeout = TimeSpan.FromSeconds(10) }, "Jogo Que Nao Existe Zzqx 9931").GetAwaiter().GetResult() == null, "título sem artigo correspondente não traz capa errada");
        } catch (HttpRequestException) { }
        var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "pokopia");
        if (Directory.Exists(real) && Directory.EnumerateFiles(real, "eglantine.exe", SearchOption.AllDirectories).FirstOrDefault() is { } realEmu)
            foreach (var g in EmulatorLibrary.Index(realEmu, real))
                Console.WriteLine("INFO pasta real: " + g.Name + " | " + (g.ChoiceLabel ?? "") + ": " + string.Join(", ", g.Choices?.Select(c => c.Name + " = " + Path.GetFileName(c.ContentPath)) ?? [Path.GetFileName(g.ProcessArgument!)]));
    }
    sealed class EncyclopediaHandler : HttpMessageHandler {
        public int ImageRequests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            if (!request.Headers.UserAgent.ToString().StartsWith("LazyGaming/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden));
            if (request.RequestUri!.Host == "en.wikipedia.org")
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("""{"query":{"pages":{"1":{"title":"Pokémon Pokopia","index":1,"original":{"source":"https://upload.wikimedia.org/test-cover.png"}}}}}""") });
            Assert(request.RequestUri.Host == "upload.wikimedia.org", "somente imagem na origem permitida");
            ImageRequests++;
            using var bitmap = new System.Drawing.Bitmap(270, 370);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) { graphics.Clear(System.Drawing.Color.SeaGreen); graphics.FillRectangle(System.Drawing.Brushes.Gold, 0, 0, 135, 185); }
            using var bytes = new MemoryStream(); bitmap.Save(bytes, System.Drawing.Imaging.ImageFormat.Png);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes.ToArray()) });
        }
    }
}
