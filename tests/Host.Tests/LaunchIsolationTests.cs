using LazyGaming;

static class LaunchIsolationTests
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "lg-launch-isolation-" + Guid.NewGuid().ToString("N"));
        var library = new GameLibrary(root, true);
        var local = library.Games.First() with { LaunchDirectory = root };
        var info = WindowsGames.LaunchInfo(local);
        Check(!info.UseShellExecute && info.FileName == local.Launch && info.WorkingDirectory == root, "EXE local usa CreateProcess sem extensões/DDE do Shell; diretório e wrapper preservados");
        var steam = local with { Steam = true, Launch = "steam://rungameid/123", ArtworkId = "123" };
        var steamInfo = WindowsGames.SteamLaunchInfo(steam, root);
        Check(!steamInfo.UseShellExecute && steamInfo.FileName == Path.Combine(root, "steam.exe") && steamInfo.ArgumentList.SequenceEqual(new[] { "-applaunch", "123" }), "Steam recebe appid instalado sem Shell ou execução direta do jogo");
        var unityPath = Path.Combine(root, "Example.exe");
        var unity = local with { Launch = unityPath, ProcessPath = unityPath, ProcessArgument = null };
        Check(!GameDisplaySettings.CanUseUnityDisplayArguments(unity), "não inferir Unity só pelo nome do jogo");
        File.WriteAllText(Path.Combine(root, "UnityPlayer.dll"), "fixture"); Directory.CreateDirectory(Path.Combine(root, "Example_Data"));
        Check(GameDisplaySettings.CanUseUnityDisplayArguments(unity), "Unity direto identificado por player e pasta de dados");
        Check(!GameDisplaySettings.CanUseUnityDisplayArguments(unity with { Launch = Path.Combine(root, "Wrapper.exe") }), "não presumir encaminhamento de argumentos por wrapper como o Cult");
        Check(!GameDisplaySettings.CanUseUnityDisplayArguments(unity with { ProcessArgument = "Shared.dll" }), "não aplicar parâmetros a runtime compartilhado");
        Check(GameDisplaySettings.UnityDisplayArguments(3,3840,2160).SequenceEqual(new[] { "-monitor","3","-screen-fullscreen","0","-popupwindow","-screen-width","3840","-screen-height","2160" }), "Unity usa monitor dinâmico e janela controlável sem alterar opções/saves");
        using var blocked = new BlockedLaunch();
        var service = new GameService(library, blocked, new SimulatedEnvironment());
        var opening = Task.Run(() => service.Start(local.Id));
        try {
            await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            using var canceled = new CancellationTokenSource(50);
            bool cancelObserved = false;
            try { await service.StatusAsync(canceled.Token); } catch (OperationCanceledException) { cancelObserved = true; }
            Check(cancelObserved, "consulta cancelada não retém trabalhador esperando abertura presa");
            bool bounded = false;
            try { await service.StatusAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (InvalidOperationException e) { bounded = e.Message.Contains("andamento"); }
            Check(bounded && !opening.IsCompleted, "consulta de estado informa operação em andamento em até2s sem destravar/duplicar lançamento pendente");
        } finally { blocked.Release.Set(); await opening; }
        await service.StatusAsync().WaitAsync(TimeSpan.FromSeconds(1));
        await service.Start(local.Id);
        Check(blocked.Count == 1, "liberar abertura permite consultas e não duplica tentativa ainda pendente");
        typeof(GameService).GetField("deadline", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(service, DateTime.UtcNow.AddSeconds(-1));
        var failed = System.Text.Json.JsonSerializer.Serialize(await service.StatusAsync());
        Check(failed.Contains("não abriu", StringComparison.OrdinalIgnoreCase) || failed.Contains("n\\u00E3o abriu", StringComparison.OrdinalIgnoreCase), "abertura expirada informa falha");
        await service.Start(local.Id);
        Check(blocked.Count == 2, "após falha expirada aceita nova tentativa explícita sem exigir reinício");
        Directory.Delete(root, true);
        Console.WriteLine("PASS: EXE local sem Shell, Steam preservado; consulta assíncrona cancelável/limitada durante lançamento bloqueado, sem duplicação.");
    }
    sealed class BlockedLaunch : IGamePlatform, IDisposable
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release = new(false);
        public int Count;
        public List<Running> Processes() => [];
        public void Launch(Game game, string mode = "cable") { Count++; Entered.TrySetResult(); Release.Wait(); }
        public bool RequestClose(Running process) => false;
        public void ForceClose(Running process) => throw new Exception("não fechar processos neste teste");
        public void Dispose() => Release.Dispose();
    }
}
