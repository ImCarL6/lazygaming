using LazyGaming;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

static class RuntimeDiagnosticsTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void InterruptedFixture(string data) {
        using var diagnostics = new RuntimeDiagnostics(data); diagnostics.Attach();
        diagnostics.Begin("POST /api/games/example/start");
        Environment.Exit(17); // Encerramento sem executar Dispose; não gera crash/diálogo do Windows.
    }
    public static async Task Run() {
        var root = Path.Combine(Path.GetTempPath(), "lg-diagnostics-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try {
            string folder, report;
            using (var diagnostics = new RuntimeDiagnostics(root)) {
                diagnostics.Attach(); folder = diagnostics.RunDirectory;
                var operation = diagnostics.Begin("POST /api/games/example/start");
                RuntimeDiagnostics.Report("game", "launch-failed", new InvalidOperationException("token=DontPublishThisToken123 cookie=DoNotPublishCookie456 C:\\Users\\PrivateUser\\game\\a.exe"),
                    new { password = "DoNotPublishPassword789", commandLine = "private command", email = "private@example.com", url = "http://pc.example/?invite=HiddenInviteValue", mode = "cable", width = 3840 });
                diagnostics.End(operation, false, 409);
                for (int i = 0; i < 100; i++) RuntimeDiagnostics.Report("audio", "unavailable", new IOException("Falha de teste"));
                Check(diagnostics.IssueCount >= 102 && File.ReadAllLines(Path.Combine(folder, "events.jsonl")).Length < 15, "repetições são contadas sem inundar o histórico");
                File.WriteAllText(Path.Combine(folder, "pairing.json"), "DoNotExportPrivateFile");
                diagnostics.Context("display", new { width = 3840, height = 2160 });
                diagnostics.Complete(); report = diagnostics.Export()!;
                Check(File.Exists(report), "ZIP de relatório criado automaticamente/pronto para suporte");
            }
            using (var zip = ZipFile.OpenRead(report)) {
                Check(!zip.Entries.Any(e => e.Name == "pairing.json") && zip.Entries.Any(e => e.Name == "summary.json"), "exporta somente arquivos de diagnóstico conhecidos");
                string all = string.Join("\n", zip.Entries.Select(e => { using var read = new StreamReader(e.Open()); return read.ReadToEnd(); }));
                foreach (var secret in new[] { "DontPublishThisToken123", "DoNotPublishCookie456", "DoNotPublishPassword789", "private command", "PrivateUser", "private@example.com", "HiddenInviteValue", "DoNotExportPrivateFile" })
                    Check(!all.Contains(secret), "conteúdo privado omitido do relatório: " + secret[..5]);
                Check(all.Contains("3840") && all.Contains("launch-failed") && all.Contains("0x"), "contexto de vídeo, código de falha e HResult preservados");
            }
            using (var next = new RuntimeDiagnostics(root)) { Check(next.IssueCount == 0, "saída normal não é classificada como crash"); next.Complete(); }
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(typeof(RuntimeDiagnosticsTests).Assembly.Location);
            info.ArgumentList.Add("--diagnostics-interrupted-fixture"); info.ArgumentList.Add(root);
            using (var process = Process.Start(info)!) { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); Check(process.ExitCode == 17, "processo isolado terminou sem fechamento normal"); }
            using (var recovered = new RuntimeDiagnostics(root)) {
                Check(recovered.IssueCount > 0 && File.ReadAllText(Path.Combine(recovered.RunDirectory, "events.jsonl")).Contains("previous-run-interrupted"), "próxima abertura detecta execução interrompida");
                var previous = Directory.GetFiles(Path.Combine(recovered.Root, "reports"), "*.zip").Any(p => {
                    using var zip = ZipFile.OpenRead(p); var summary = zip.GetEntry("summary.json"); if (summary == null) return false;
                    using var read = new StreamReader(summary.Open()); var text = read.ReadToEnd(); return text.Contains("interrupted") && text.Contains("POST /api/games/example/start");
                }); Check(previous, "relatório anterior conserva operação que não foi concluída"); recovered.Complete();
            }
            var xml = """<Event><System><EventID>1000</EventID><EventRecordID>42</EventRecordID></System><EventData><Data Name="AppName">LazyGaming.Host.exe</Data><Data Name="AppVersion">0.28.4</Data><Data Name="ModuleName">driver.dll</Data><Data Name="ExceptionCode">c0000005</Data><Data Name="AppPath">C:\Users\SecretUser\LazyGaming.Host.exe</Data></EventData></Event><Event><System><EventID>1000</EventID></System><EventData><Data Name="AppName">Outro.exe</Data></EventData></Event>""";
            var crashes = RuntimeDiagnostics.ParseWindowsCrashes(xml); var events = JsonSerializer.Serialize(crashes);
            Check(crashes.Length == 1 && events.Contains("c0000005") && events.Contains("driver.dll") && !events.Contains("SecretUser") && !events.Contains("Outro.exe"), "eventos Windows filtram só o app, código/módulo, sem caminhos pessoais ou outros apps");
            var retention = Path.Combine(root, "retention"); Directory.CreateDirectory(retention);
            for (int i = 0; i < 8; i++) using (var run = new RuntimeDiagnostics(retention, 3)) { run.Record("test", "error", true); run.Complete(); }
            Check(Directory.GetDirectories(Path.Combine(retention, "diagnostics"), "run-*").Length <= 3 && Directory.GetFiles(Path.Combine(retention, "diagnostics", "reports"), "*.zip").Length is > 0 and <= 3, "retenção limita execuções e pacotes automáticos de relatório");
            using (var live = new RuntimeDiagnostics(Path.Combine(root, "live"))) {
                live.Record("test", "live-error", true);
                for (int i = 0; i < 40 && !Directory.Exists(Path.Combine(live.Root, "reports")); i++) await Task.Delay(100);
                Check(Directory.Exists(Path.Combine(live.Root, "reports")) && Directory.GetFiles(Path.Combine(live.Root, "reports"), "*.zip").Length == 1, "falha gera ZIP automaticamente com o app ainda aberto");
                for (int i = 0; i < 700; i++) live.Record("test", "case-" + i, true, new InvalidOperationException(new string('x', 1800)));
                Check(File.Exists(Path.Combine(live.RunDirectory, "events.previous.jsonl")) && Directory.GetFiles(live.RunDirectory, "events*.jsonl").All(f => new FileInfo(f).Length < 1_081_344), "log rotaciona e mantém limite em rajada de erros distintos");
                live.Complete();
            }
            var blocked = Path.Combine(root, "blocked"); File.WriteAllText(blocked, "arquivo, não diretório");
            using (var unavailable = new RuntimeDiagnostics(blocked)) { unavailable.Record("test", "unwritable", true); unavailable.Complete(); Check(!unavailable.Available, "falha de armazenamento não interrompe o aplicativo"); }
        } finally { var full = Path.GetFullPath(root); Check(full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("lg-diagnostics-"), "limpeza limitada ao teste isolado"); Directory.Delete(full, true); }
        Console.WriteLine("PASS: diagnósticos locais, privacidade/ZIP, repetição limitada, crash/interrupção real em processo isolado, eventos Windows e retenção.");
    }
}
