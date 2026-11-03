using LazyGaming;
using System.Text.Json;

static class HubLibraryTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static async Task Run() {
        var root = Path.Combine(Path.GetTempPath(), "lg-hub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var data = Path.Combine(root, "data"); var games = Path.Combine(root, "Games"); Directory.CreateDirectory(games);
            var folder = Path.Combine(games, "Example"); Directory.CreateDirectory(folder);
            var exe = Path.Combine(folder, "Example.exe"); File.WriteAllText(exe, "never executed");
            var setup = Path.Combine(games, "Example-Setup"); Directory.CreateDirectory(setup); File.WriteAllText(Path.Combine(setup, "tool.exe"), "never executed");
            var preferences = new LibrarySettings(data); preferences.Save(new([new(games), new(games + Path.DirectorySeparatorChar)], IntervalMinutes: 1));
            Check(preferences.Read().Folders.Length == 1, "pastas normalizadas sem duplicação");
            var library = new GameLibrary(data, steamPath: Path.Combine(root, "no-steam"));
            Check(library.Games.Count == 1 && library.Games[0].ProcessPath == exe, "nova pasta descoberta sem executar nem indexar instalador");
            var id = library.Games[0].Id;
            preferences.Save(preferences.Read() with { Excluded = [id] }); library.Refresh();
            Check(library.Games.Count == 0 && library.Discovered.Single().Id == id, "oculto permanece gerenciável");
            File.WriteAllText(Path.Combine(data, "library", "settings.json"), "{");
            Check(new LibrarySettings(data).Read().Excluded!.Contains(id), "configuração recuperada da reserva");
            var store = new LocalGameStore(data);
            string Manifest(string title, string executable) {
                var path = Path.Combine(root, title + ".json");
                File.WriteAllText(path, JsonSerializer.Serialize(new LocalGameRegistration(executable, Path.GetDirectoryName(executable)!, new(null, title, 2023, [], [], "", DateTime.UtcNow, Source: "local", Artwork: new("", "", "", "")))));
                return path;
            }
            var first = Manifest("First", exe); store.Import(first);
            var secondFolder = Path.Combine(root, "Second"); Directory.CreateDirectory(secondFolder);
            var secondExe = Path.Combine(secondFolder, "second.exe"); File.WriteAllText(secondExe, "never executed");
            var second = Manifest("Second", secondExe);
            await Task.WhenAll(Task.Run(() => new LocalGameStore(data).Import(first)), Task.Run(() => new LocalGameStore(data).Import(second)));
            var registrations = Path.Combine(data, "library", "local-games.json");
            Check(JsonDocument.Parse(File.ReadAllText(registrations)).RootElement.GetArrayLength() == 2, "importações concorrentes preservam ambos os jogos");
            File.WriteAllText(registrations, "[");
            Check(store.Read([]).Count() == 2, "cadastro truncado recupera última gravação confirmada");
            File.Delete(registrations);
            Check(store.Read([]).Count() == 2, "cadastro ausente recupera reserva");
            File.Delete(secondExe);
            var inspect = JsonSerializer.SerializeToElement(store.Inspect());
            var unavailable = inspect.EnumerateArray().Single(entry => entry.GetProperty("executable").GetString() == secondExe);
            var available = inspect.EnumerateArray().Single(entry => entry.GetProperty("executable").GetString() == exe);
            Check(inspect.GetArrayLength() == 2 && !unavailable.GetProperty("available").GetBoolean()
                && !string.IsNullOrWhiteSpace(unavailable.GetProperty("reason").GetString())
                && available.GetProperty("available").GetBoolean(), "jogo indisponível permanece visível no gerenciador com motivo, independentemente da ordem das importações concorrentes");
            var platform = new SimulatedGames();
            var service = new GameService(library, platform, new SimulatedEnvironment());
            var hub = new PcHub(library, service, () => Task.CompletedTask);
            await hub.Index(() => preferences.Save(preferences.Read() with { Excluded = [] }));
            Check(library.Games.Any(g => g.ProcessPath == exe) && library.Games.Count == 1, "reindexação mantém prioridade do cadastro sobre pasta automática");
            var now = DateTime.UtcNow; await hub.AutomaticTick(now);
            var third = Path.Combine(games, "Third"); Directory.CreateDirectory(third); File.WriteAllText(Path.Combine(third, "third.exe"), "never executed");
            await hub.AutomaticTick(now.AddMinutes(1));
            Check(library.Games.Count == 2, "busca automática descobre nova instalação sem executar");
            await service.Start(library.Games[0].Id);
            var fourth = Path.Combine(games, "Fourth"); Directory.CreateDirectory(fourth); File.WriteAllText(Path.Combine(fourth, "fourth.exe"), "never executed");
            await hub.AutomaticTick(now.AddMinutes(2));
            Check(library.Games.Count == 2 && platform.LaunchCount == 1, "busca automática adiada durante jogo sem duplicar ou fechar");
            var changed = false; var refused = false;
            try { await hub.Index(() => changed = true); } catch (InvalidOperationException) { refused = true; }
            Check(refused && !changed, "configuração também aguarda fim da partida");
            var session = new HubSession(); var token = session.Issue(); var cookie = session.Redeem(token);
            Check(cookie != null && session.Authenticate(cookie) && session.Redeem(token) == null && !session.Authenticate("wrong"), "bootstrap PC é descartável e autenticação independente");
            Console.WriteLine("PASS: central PC, pastas, busca automática adiada durante jogo, deduplicação, importação concorrente e recuperação do cadastro.");
        } finally { Directory.Delete(root, true); }
    }
}
