namespace LazyGaming;

// Conferência limitada ao início; após receber entrada do usuário, não disputa foco.
public sealed class GameWindowTarget(string path, DateTime deadline, Func<uint>? inputStamp = null)
{
    int stable;
    volatile bool done;
    DateTime? stableSince;
    uint? inputAtFocus;
    bool confirmed;
    public string Phase => done ? (Error == null ? "finished" : "failed") : confirmed ? "watching" : "preparing";
    public string? Error { get; private set; }
    public bool CanAutomate(DateTime now) => !done && now < deadline &&
        (inputAtFocus == null || inputStamp?.Invoke() == inputAtFocus);
    public void Cancel(string processPath) {
        if (!string.Equals(processPath, path, StringComparison.OrdinalIgnoreCase)) return;
        done = true; Error = null;
    }
    public void Update(IReadOnlyList<Running> processes, DateTime now, Func<Running, bool> place)
    {
        if (done) return;
        if (inputAtFocus is uint stamp && inputStamp?.Invoke() is uint current && current != stamp) { done = true; Error = null; return; }
        if (now >= deadline) { done = true; if (!confirmed || stable == 0) Error = "Não foi possível confirmar a posição e o foco do jogo. Use Continuar para tentar novamente."; return; }
        var matches = processes.Where(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) { stable = 0; stableSince = null; return; }
        try { stable = place(matches[0]) ? stable + 1 : 0; }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { stable = 0; }
        if (stable == 0) stableSince = null;
        else {
            stableSince ??= now;
            if (inputAtFocus == null && inputStamp?.Invoke() is uint stampAtFocus && stampAtFocus != uint.MaxValue) inputAtFocus = stampAtFocus;
        }
        if (stable >= 5 && now - stableSince >= TimeSpan.FromSeconds(12)) {
            confirmed = true;
            // Sem amostra confiável de entrada, manter o comportamento curto.
            // Com ela, observar o restante do prazo de abertura; um clique/tecla
            // do usuário encerra a conferência antes de qualquer nova ativação.
            if (inputAtFocus == null) done = true;
        }
    }
}
