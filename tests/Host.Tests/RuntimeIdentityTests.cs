using LazyGaming;
using System.Diagnostics;

static class RuntimeIdentityTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        var executable = ProcessImage.PathOf(Environment.ProcessId)!;
        var content = Path.Combine(Path.GetTempPath(), "lg-runtime-" + Guid.NewGuid().ToString("N"), "game content", "main");
        foreach (var server in new[] { false, true }) {
            var launch = new ProcessStartInfo(executable) { UseShellExecute = false };
            launch.ArgumentList.Add("--runtime-identity-fixture");
            launch.ArgumentList.Add(content);
            if (server) launch.ArgumentList.Add("-server");
            using var child = Process.Start(launch)!;
            try {
                Check(WindowsGames.RuntimeMatches(child.Id, content) == !server, "WMI identifica argumento exato do processo real de teste e recusa servidor");
                Check(!WindowsGames.RuntimeMatches(child.Id, content + "-other"), "outro conteúdo não herda identidade do processo");
            } finally {
                if (!child.HasExited) child.Kill();
                child.WaitForExit();
            }
            Check(!WindowsGames.RuntimeMatches(child.Id, content), "processo já encerrado não é identificado como jogo");
        }
        Console.WriteLine("PASS: consulta WMI real de processos próprios, conteúdo exato, servidor/outro conteúdo/saída recusados; nenhum jogo executado.");
    }
}
