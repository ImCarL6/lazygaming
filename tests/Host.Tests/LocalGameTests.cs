using LazyGaming;
using System.Drawing;
using System.Text.Json;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

static class LocalGameTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "lg-local-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var installed = Path.Combine(root, "drive-d", "Joguetes", "TotK Recomp"); Directory.CreateDirectory(installed);
            var executable = Path.Combine(installed, "totk.exe"); File.WriteAllText(executable, "game, never executed");
            File.WriteAllText(Path.Combine(installed, "totk_xlate.exe"), new string('x', 10000));
            File.WriteAllText(Path.Combine(installed, "totk.ini"), "lang = pt-BR\npad_layout = nintendo");
            var image = Path.Combine(root, "art.jpg");
            using (var bitmap = new Bitmap(900, 1200)) {
                using var g = Graphics.FromImage(bitmap); g.Clear(Color.SeaGreen); g.FillRectangle(Brushes.Gold, 0, 0, 450, 600); bitmap.Save(image, System.Drawing.Imaging.ImageFormat.Jpeg);
            }
            var data = Path.Combine(root, "cache");
            var metadata = new GameMetadata(null, "The Legend of Zelda: Tears of the Kingdom", 2023, [], ["Ação", "Aventura"], "https://www.nintendo.com/", DateTime.UtcNow,
                Source: "local", Description: "Explore os céus de Hyrule.", Language: "Textos em português brasileiro", Artwork: new(image, image, "https://assets.nintendo.com/cover", "https://assets.nintendo.com/backdrop"));
            var registration = new LocalGameRegistration(executable, installed, metadata);
            var manifest = Path.Combine(root, "import.json"); File.WriteAllText(manifest, JsonSerializer.Serialize(registration));
            var store = new LocalGameStore(data); var id = store.Import(manifest); store.Import(manifest);
            var library = new GameLibrary(data, false, Path.Combine(root, "no-steam"), [Path.GetDirectoryName(installed)!, installed + Path.DirectorySeparatorChar], true);
            Check(library.Games.Count == 1, "cadastro repetido e descoberta da mesma pasta não duplicam o jogo");
            var game = library.Games.Single();
            Check(game.Id == id && !game.Steam && game.ArtworkId == null && game.Launch == executable && game.ProcessPath == executable && game.ProcessArgument == null && game.LaunchDirectory == installed, "identidade local separada da Steam e lançamento exato sem argumentos");
            var launch = WindowsGames.LaunchInfo(game);
            Check(launch.FileName == executable && launch.WorkingDirectory == installed && launch.ArgumentList.Count == 0 && launch.Arguments == "", "informação de lançamento sem executar o jogo");
            Check(GameLibrary.Exes(installed).SequenceEqual([executable]), "tradutor de shaders maior que o jogo não participa da descoberta");
            using var http = new HttpClient(new Offline()); var catalog = new GameMetadataStore(data, http);
            await catalog.EnsureAll([game]); catalog.Rename(game, "Meu Zelda");
            Check(catalog.ArtworkId(game) == id && catalog.For(game)?.StoreId == null, "ficha local não inventa AppID");
            bool refused = false; try { await catalog.Assign(game, "123"); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "não substituir ficha local por associação Steam");
            var art = new ArtworkStore(library); await art.EnsureAll();
            foreach (var kind in new[] { "cover", "backdrop" }) {
                var file = art.FileFor(id, kind); Check(file != null && art.Url(game, kind) != null, "arte local pelo endpoint compartilhado");
                using var size = Image.FromFile(file!); Check(size.Width <= (kind == "cover" ? 480 : 1600) && size.Height <= (kind == "cover" ? 720 : 900), "limites de arte local sem ampliação");
            }
            var cover = art.FileFor(id, "cover")!; var stamp = File.GetLastWriteTimeUtc(cover);
            // O importador nunca altera a instalação; reinício pode funcionar sem as fontes de importação.
            File.Delete(image); var reloaded = new GameLibrary(data, false, Path.Combine(root, "no-steam"), []);
            await reloaded.Metadata.EnsureAll(reloaded.Games); await new ArtworkStore(reloaded).EnsureAll(); reloaded.Refresh();
            Check(reloaded.Games.Count == 1 && reloaded.Metadata.DisplayName(reloaded.Games[0]) == "Meu Zelda" && reloaded.Metadata.For(reloaded.Games[0])?.Year == 2023, "reiniciar preserva entrada, nome pessoal e ficha offline");
            Check(File.GetLastWriteTimeUtc(cover) == stamp && File.ReadAllText(Path.Combine(installed, "totk.ini")) == "lang = pt-BR\npad_layout = nintendo", "cache reutilizado e configuração intacta");
            Check(art.FileFor("../escape", "cover") == null, "identidade de arte não atravessa diretórios");
            var before = File.ReadAllText(Path.Combine(data, "library", "local-games.json"));
            var emulatorFolder = Path.Combine(root, "emulator"); Directory.CreateDirectory(emulatorFolder);
            var emulator = Path.Combine(emulatorFolder, "eglantine.exe"); File.WriteAllText(emulator, "fixture");
            var content = Path.Combine(root, "main"); File.WriteAllText(content, "fixture"); File.WriteAllText(content + ".npdm", "fixture");
            var runtimeData = Path.Combine(root, "runtime-data");
            // Fonte da arte foi removida acima; reutilizar o cache já validado.
            var runtimeRegistration = new LocalGameRegistration(emulator, emulatorFolder, metadata with { Artwork = new(cover, cover, "local", "local") }, ContentPath: content);
            File.WriteAllText(manifest, JsonSerializer.Serialize(runtimeRegistration)); new LocalGameStore(runtimeData).Import(manifest);
            var runtimeGame = new GameLibrary(runtimeData, false, Path.Combine(root, "no-steam"), []).Games.Single();
            var runtimeLaunch = WindowsGames.LaunchInfo(runtimeGame);
            Check(!runtimeLaunch.UseShellExecute && runtimeLaunch.ArgumentList.SequenceEqual(new[] { "-f", "-g", content }) && runtimeGame.ProcessArgument == content, "runtime inicia conteúdo exato em fullscreen sem shell");
            Check(WindowsGames.RuntimeCommandMatches("eglantine.exe -f -g \"" + content + "\"", content)
                && !WindowsGames.RuntimeCommandMatches("eglantine.exe -f -g \"" + content + "-outro\"", content), "runtime identifica somente o conteúdo esperado");
            var newFolder = Path.Combine(root, "emulator-2.5"); Directory.CreateDirectory(newFolder);
            var newEmulator = Path.Combine(newFolder, "eglantine.exe"); File.WriteAllText(newEmulator, "fixture, never executed");
            var nup = Path.Combine(root, "Game translated.nup");
            var toc = new byte[48];
            var header = new byte[128]; "NUP\u0001"u8.CopyTo(header);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), 128);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8), 128);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(16), (ulong)toc.Length);
            SHA256.HashData(toc).CopyTo(header, 24);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(64), (ulong)(header.Length + toc.Length));
            File.WriteAllBytes(nup, [..header, ..toc]);
            Check(LocalGameStore.ValidRuntimeContent(nup), "NUP existente com cabeçalho/tamanho/TOC íntegros");
            var runtimeStore = new LocalGameStore(runtimeData);
            var otherManifest = Path.Combine(root, "other.json");
            File.WriteAllText(otherManifest, JsonSerializer.Serialize(registration with { Metadata = metadata with { Title = "Outro jogo", Artwork = new(cover, cover, "local", "local") } }));
            var otherId = runtimeStore.Import(otherManifest);
            var registrationsPath = Path.Combine(runtimeData, "library", "local-games.json");
            var otherBefore = JsonNode.Parse(File.ReadAllText(registrationsPath))!.AsArray().Single(e => e!["Metadata"]!["Title"]!.GetValue<string>() == "Outro jogo")!.DeepClone();
            new GameMetadataStore(runtimeData).Rename(runtimeGame, "Meu jogo");
            var runtimeCatalogPath = Path.Combine(runtimeData, "library", "catalog.json");
            var runtimeCatalogBefore = File.ReadAllBytes(runtimeCatalogPath);
            var nupRegistration = runtimeRegistration with { Executable = newEmulator, WorkingDirectory = newFolder, ContentPath = nup };
            File.WriteAllText(manifest, JsonSerializer.Serialize(nupRegistration));
            Check(runtimeStore.Import(manifest, runtimeGame.Id) == runtimeGame.Id && runtimeStore.Import(manifest) == runtimeGame.Id, "troca de versão e reimportação mantêm a identidade anterior");
            var migrated = new GameLibrary(runtimeData, false, Path.Combine(root, "no-steam"), []);
            var upgraded = migrated.Games.Single(g => g.Id == runtimeGame.Id);
            Check(migrated.Games.Count == 2 && migrated.Games.Any(g => g.Id == otherId) && upgraded.ProcessPath == newEmulator
                && upgraded.ProcessArgument == nup && WindowsGames.LaunchInfo(upgraded).ArgumentList.SequenceEqual(new[] { "-f", "-g", nup }), "biblioteca substitui executável/conteúdo sem duplicar, sem shell e com fullscreen");
            Check(migrated.Metadata.DisplayName(upgraded) == "Meu jogo" && File.ReadAllBytes(runtimeCatalogPath).SequenceEqual(runtimeCatalogBefore), "troca de pasta conserva nome personalizado e catálogo intacto");
            var otherAfter = JsonNode.Parse(File.ReadAllText(registrationsPath))!.AsArray().Single(e => e!["Metadata"]!["Title"]!.GetValue<string>() == "Outro jogo");
            Check(JsonNode.DeepEquals(otherBefore, otherAfter), "outro cadastro permanece intacto semanticamente");
            var migratedBefore = File.ReadAllBytes(registrationsPath);
            refused = false; try { runtimeStore.Import(manifest, "local-000000000000"); } catch (InvalidDataException) { refused = true; }
            Check(refused && File.ReadAllBytes(registrationsPath).SequenceEqual(migratedBefore), "substituição sem identidade existente não altera biblioteca");
            toc[0] = 1; File.WriteAllBytes(nup, [..header, ..toc]);
            Check(!LocalGameStore.ValidRuntimeContent(nup), "NUP com TOC adulterada recusado");
            refused = false; try { runtimeStore.Import(manifest, runtimeGame.Id); } catch (InvalidDataException) { refused = true; }
            Check(refused && File.ReadAllBytes(registrationsPath).SequenceEqual(migratedBefore), "NUP inválido preserva cadastro vigente");
            File.WriteAllBytes(nup, header[..32]);
            Check(!LocalGameStore.ValidRuntimeContent(nup), "NUP truncado recusado sem executar");
            File.WriteAllText(manifest, JsonSerializer.Serialize(runtimeRegistration with { Arguments = "-outro" }));
            refused = false; try { new LocalGameStore(runtimeData).Import(manifest); } catch (InvalidDataException) { refused = true; }
            Check(refused, "runtime não aceita comandos arbitrários");
            File.WriteAllText(manifest, JsonSerializer.Serialize(registration with { Executable = Path.Combine(installed, "totk_xlate.exe") }));
            refused = false; try { store.Import(manifest); } catch (InvalidDataException) { refused = true; }
            Check(refused && before == File.ReadAllText(Path.Combine(data, "library", "local-games.json")), "cadastro inválido preserva biblioteca existente");
            var missing = Path.Combine(root, "cache", "library", "imports"); Directory.Delete(missing, true);
            await new ArtworkStore(reloaded).EnsureAll(); Check(File.Exists(cover), "perda da fonte privada também conserva a arte já armazenada");
            Console.WriteLine("PASS: cadastro local fora da raiz padrão, duplicação, exe/sem argumentos, ficha sem Steam, limites/artes/cache offline e preservação da instalação.");
        } finally { Directory.Delete(root, true); }
    }
    sealed class Offline : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new Exception("Ficha local nunca consulta Steam.");
    }
}
