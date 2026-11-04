namespace LazyGaming;

public interface IProcessLifetime : IDisposable { bool Exited { get; } uint? ExitCode => null; }

// Só recebe processos cuja imagem/runtime já foram verificados antes de fechar.
// O handle identifica o objeto original mesmo se o número do PID for reutilizado.
public sealed class ClosingGames : IDisposable
{
    readonly Dictionary<int, (Running Process, IProcessLifetime Lifetime)> entries = [];
    public void Add(Running process, IProcessLifetime lifetime) {
        if (entries.Remove(process.Pid, out var old)) old.Lifetime.Dispose();
        entries.Add(process.Pid, (process, lifetime));
    }
    public List<Running> Active(Func<Running, bool>? stillListed = null) {
        var result = new List<Running>();
        foreach (var pair in entries.ToArray()) {
            // O sinal de saída pode chegar antes de desaparecerem entradas
            // transitórias do jogo. Nessa janela a imagem pode recusar consulta.
            // Continuar bloqueando a troca até ambas as condições confirmarem;
            // nunca usar a identidade guardada para autorizar novo fechamento.
            if (pair.Value.Lifetime.Exited && stillListed?.Invoke(pair.Value.Process) != true) { entries.Remove(pair.Key); pair.Value.Lifetime.Dispose(); }
            else result.Add(pair.Value.Process);
        }
        return result;
    }
    public void Dispose() { foreach (var entry in entries.Values) entry.Lifetime.Dispose(); entries.Clear(); }
}
