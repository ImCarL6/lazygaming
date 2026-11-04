namespace LazyGaming;

// Observa handles dos jogos já verificados. Não fecha processos, não lê memória
// e não usa PID reutilizado para atribuir uma saída ao jogo anterior.
public sealed class GameExitDiagnostics(Func<Running, IProcessLifetime> track, Action<GameExitDiagnostics.Exit> report) : IDisposable
{
    public sealed record Exit(string Game, int Pid, uint? Code, bool Forced, double Seconds);
    sealed record Tracked(string Game, DateTime Started, IProcessLifetime Lifetime) { public bool Forced; }
    readonly object gate = new();
    readonly Dictionary<int, Tracked> active = [];
    readonly Dictionary<int, DateTime> retry = [];
    public void Observe(IReadOnlyList<Running> processes, Func<Running, string> identify) {
        lock (gate) {
            foreach (var pair in active.ToArray()) {
                try {
                    if (!pair.Value.Lifetime.Exited) continue;
                    report(new(pair.Value.Game, pair.Key, pair.Value.Lifetime.ExitCode, pair.Value.Forced, (DateTime.UtcNow - pair.Value.Started).TotalSeconds));
                    pair.Value.Lifetime.Dispose(); active.Remove(pair.Key);
                } catch (Exception e) { RuntimeDiagnostics.Report("game-exit", "observe-failed", e); pair.Value.Lifetime.Dispose(); active.Remove(pair.Key); }
            }
            foreach (var process in processes) {
                if (active.ContainsKey(process.Pid) || active.Count >= 8 || retry.TryGetValue(process.Pid, out var next) && DateTime.UtcNow < next) continue;
                try { var game = identify(process); var lifetime = track(process); active[process.Pid] = new(game, DateTime.UtcNow, lifetime); retry.Remove(process.Pid); }
                catch (Exception e) {
                    if (retry.Count >= 32) retry.Remove(retry.MinBy(p => p.Value).Key);
                    retry[process.Pid] = DateTime.UtcNow.AddSeconds(10); RuntimeDiagnostics.Report("game-exit", "tracking-unavailable", e);
                }
            }
        }
    }
    public void Forced(int pid, bool forced = true) { lock (gate) if (active.TryGetValue(pid, out var value)) value.Forced = forced; }
    public void Dispose() { lock (gate) { foreach (var entry in active.Values) entry.Lifetime.Dispose(); active.Clear(); } }
}
