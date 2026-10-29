using LazyGaming;
using System.Diagnostics;

static class ProcessExitTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static async Task Run() {
        int attempts = 0;
        var exit = ProcessImage.ResolveObservation(() => ++attempts == 1 ? new(null, false, 5) : new(null, true, 0), () => throw new Exception("não reenumerar saída confirmada"), () => { });
        Check(exit.Exited && attempts == 2, "negação transitória durante saída não vira erro de administrador");
        var stale = ProcessImage.ResolveObservation(() => new(null, false, 5), () => false, () => { });
        Check(stale.Exited, "snapshot antigo não retém processo ausente na enumeração nova");
        var inaccessible = ProcessImage.ResolveObservation(() => new(null, false, 5), () => true, () => { });
        Check(!inaccessible.Exited && inaccessible.Image == null && inaccessible.Error == 5, "processo vivo inacessível continua bloqueando lançamento; não relaxar identidade");
        using var closing = new ClosingGames();
        var original = new Running(42, "DD2.exe"); var lifetime = new FakeLifetime();
        closing.Add(original, lifetime);
        Check(closing.Active().Single() == original && !lifetime.Disposed, "fechamento aceito guarda identidade do objeto verificado sem reconsultar imagem");
        lifetime.Exited = true;
        Check(closing.Active(_ => true).Single() == original && !lifetime.Disposed, "saída sinalizada mas PID ainda enumerado continua como fechando, sem reconsultar imagem que recusa acesso");
        Check(closing.Active(p => p.Path == "DD2.exe").Single().Pid == 42, "outra entrada transitória do mesmo jogo só mantém espera; nunca herda autorização para fechar seu PID");
        Check(closing.Active().Count == 0 && lifetime.Disposed, "saída confirmada libera troca e descarta referência");
        var reused = new FakeLifetime(); closing.Add(new(42, "different.exe"), reused);
        Check(closing.Active().Single().Path == "different.exe", "PID reutilizado não herda identidade anterior");

        var executable = ProcessImage.PathOf(Environment.ProcessId)!;
        using var child = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, ArgumentList = { "--process-lifetime-fixture" } })!;
        using var tracked = ProcessImage.Track(child.Id, executable);
        Check(!tracked.Exited, "handle nativo acompanha fixture viva com acesso limitado");
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(6));
        Check(child.ExitCode == 259 && tracked.Exited, "sinal do objeto confirma saída mesmo quando exit code é STILL_ACTIVE");
        bool listed = true;
        for (int i = 0; i < 20 && listed; i++) {
            listed = false;
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable))) using (process) { if (process.Id == child.Id) listed = true; }
            if (listed) await Task.Delay(25);
        }
        Check(!listed, "referência de consulta não impede o desaparecimento da fixture da lista de processos");
        bool mismatchRefused = false;
        try { using var invalid = ProcessImage.Track(Environment.ProcessId, "different.exe"); } catch (InvalidOperationException) { mismatchRefused = true; }
        Check(mismatchRefused, "rastreamento não aceita imagem diferente da verificada");
        Console.WriteLine("PASS: saída transitória, processo inacessível, PID reutilizado e handle nativo com exit code259.");
    }
    sealed class FakeLifetime : IProcessLifetime { public bool Exited { get; set; } public bool Disposed; public void Dispose() => Disposed = true; }
}
