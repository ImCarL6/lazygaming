using LazyGaming;

static class GameMoveTests
{
    public static async Task Run(Action<bool, string> assert)
    {
        var folder = Path.Combine(Path.GetTempPath(), "lazygaming-move-" + Guid.NewGuid().ToString("N"));
        try {
            var library = new GameLibrary(folder, true);
            var game = library.Games[0];
            var platform = new SimulatedGames();
            var environment = new SimulatedEnvironment();
            var service = new GameService(library, platform, environment);
            async Task Reject(string id, string label) {
                bool rejected = false;
                try { await service.MoveToTv(id); } catch (InvalidOperationException) { rejected = true; }
                assert(rejected, label);
            }
            await Reject("unknown", "mover recusa jogo desconhecido");
            await Reject(game.Id, "mover não abre um jogo fechado");
            assert(!environment.Active && platform.Moved.Count == 0, "recusa antes de preparar telas ou mover");
            platform.Active.Add(new(42, game.ProcessPath));
            await service.MoveToTv(game.Id);
            assert(environment.Active && environment.Mode == "cable" && platform.Moved.Single().Pid == 42,
                "mover prepara TV e usa só o processo identificado");
            assert(platform.LaunchCount == 0 && platform.Active.Count == 1, "reparo não abre nem fecha jogo");
            environment.Prepare("stream");
            await service.MoveToTv(game.Id);
            assert(environment.Mode == "stream" && environment.Preparations == 1, "reparo preserva transporte e sessão existentes");
            platform.MoveAccepted = false;
            await Reject(game.Id, "reparo recusado pela janela informa falha");
            platform.MoveAccepted = true;
            var movedBefore = platform.Moved.Count;
            platform.Active.Add(new(43, game.ProcessPath));
            await Reject(game.Id, "não escolher arbitrariamente entre duas instâncias");
            assert(platform.Moved.Count == movedBefore, "instância ambígua não recebe movimento");
            platform.Active.Clear();
            var runtime = game with { Id = "runtime", ProcessArgument = "Game.dll" };
            library.Games.Add(runtime);
            platform.Active.Add(new(44, runtime.ProcessPath, "Other.dll"));
            await Reject(runtime.Id, "runtime com outro argumento não pertence ao jogo");
            platform.Active.Add(new(45, runtime.ProcessPath, "Game.dll"));
            await service.MoveToTv(runtime.Id);
            assert(platform.Moved.Last().Pid == 45, "runtime seleciona apenas o PID e argumento exatos");
        } finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
