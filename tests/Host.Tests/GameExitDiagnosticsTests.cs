using LazyGaming;

static class GameExitDiagnosticsTests
{
    sealed class Lifetime(uint code) : IProcessLifetime { public bool Exited { get; set; } public uint? ExitCode => Exited ? code : null; public bool Disposed; public void Dispose() => Disposed = true; }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static void Run() {
        var first = new Lifetime(0xc0000005); var second = new Lifetime(0); var current = first; var reports = new List<GameExitDiagnostics.Exit>();
        using var observer = new GameExitDiagnostics(_ => current, reports.Add);
        observer.Observe([new(101,"verified-game.exe")], _ => "game-one"); first.Exited = true; current = second;
        observer.Observe([new(101,"other-verified-game.exe")], _ => "game-two");
        Check(reports.Count == 1 && reports[0].Game == "game-one" && reports[0].Code == 0xc0000005 && first.Disposed, "crash é atribuído ao handle original, mesmo com PID reutilizado");
        second.Exited = true; observer.Observe([], _ => "unused");
        Check(reports.Count == 2 && reports[1].Game == "game-two" && reports[1].Code == 0 && !reports[1].Forced, "saída normal é registrada separadamente");
        current = new Lifetime(1); observer.Observe([new(102,"verified-game.exe")], _ => "game-three"); observer.Forced(102); current.Exited = true; observer.Observe([], _ => "unused");
        Check(reports[^1].Forced && reports[^1].Code == 1, "encerramento forçado solicitado não é confundido com crash");
        current = new Lifetime(0); observer.Observe([new(103,"verified-game.exe")], _ => "game-four"); observer.Dispose();
        Check(current.Disposed && reports.Count == 3, "parar o observador libera handles sem inventar saída/crash do jogo");
        Console.WriteLine("PASS: códigos de saída dos jogos, normal/erro/forçado e PID reutilizado, sem encerrar processos.");
    }
}
