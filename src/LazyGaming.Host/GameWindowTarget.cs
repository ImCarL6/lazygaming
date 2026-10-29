namespace LazyGaming;

// Conferência limitada ao início; não disputa futuras escolhas manuais durante a partida.
public sealed class GameWindowTarget(string path, DateTime deadline)
{
    int stable;
    bool done;
    DateTime? stableSince;
    public string? Error { get; private set; }
    public void Cancel(string processPath) {
        if (!string.Equals(processPath, path, StringComparison.OrdinalIgnoreCase)) return;
        done = true; Error = null;
    }
    public void Update(IReadOnlyList<Running> processes, DateTime now, Func<Running, bool> place)
    {
        if (done) return;
        if (now >= deadline) { done = true; Error = "Não foi possível confirmar a posição e o foco do jogo. Use Continuar para tentar novamente."; return; }
        var matches = processes.Where(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) { stable = 0; stableSince = null; return; }
        try { stable = place(matches[0]) ? stable + 1 : 0; }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { stable = 0; }
        if (stable == 0) stableSince = null;
        else stableSince ??= now;
        if (stable >= 5 && now - stableSince >= TimeSpan.FromSeconds(12)) done = true;
    }
}
