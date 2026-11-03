using LazyGaming;
using System.Diagnostics;

static class DisplayPreferenceTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static async Task Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lg-display-preference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "display-profile.json");
        try {
            var platform = new FakeOutput();
            var session = new TvSession(directory, platform);
            Check(session.DisplayControls.MonitorsEnabled, "sem histórico os monitores ficam ligados por padrão");
            Check(!platform.Prepared && platform.MonitorChanges == 0, "carregar padrão não altera telas");
            // O processo confirma a gravação e é terminado sem executar shutdown/finally.
            foreach (bool enabled in new[] { true, false, true, false }) {
                using var child = Process.Start(new ProcessStartInfo(ProcessImage.PathOf(Environment.ProcessId)!) {
                    UseShellExecute = false, RedirectStandardOutput = true,
                    ArgumentList = { "--monitor-preference-fixture", directory, enabled.ToString() }
                })!;
                try {
                    Check(await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) == "saved", "fixture confirmou gravação durável");
                }
                finally { if (!child.HasExited) child.Kill(); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                Check(new TvSession(directory, new FakeOutput()).DisplayControls.MonitorsEnabled == enabled,
                    "última escolha sobrevive ao encerramento abrupto e nova instância");
            }
            foreach (var invalid in new[] { "{", "{}", "null", "{\"MonitorsEnabled\":\"false\"}" }) {
                File.WriteAllText(file, invalid);
                Check(!new TvSession(directory, new FakeOutput()).DisplayControls.MonitorsEnabled, "reserva recupera última escolha quando principal inválido");
            }
            File.Delete(file);
            Check(!new TvSession(directory, new FakeOutput()).DisplayControls.MonitorsEnabled, "principal ausente recupera reserva");
            File.WriteAllText(file + ".bak", "{");
            Check(new TvSession(directory, new FakeOutput()).DisplayControls.MonitorsEnabled, "sem cópia válida usa padrão ligado sem falhar inicialização");
            File.WriteAllText(file, "{\"MonitorsEnabled\":false}");
            Check(!new TvSession(directory, new FakeOutput()).DisplayControls.MonitorsEnabled, "formato anterior preserva escolha desligado");

            session.SetMonitors(true); // Mesmo igual ao padrão: persistir escolha explícita.
            Check(new TvSession(directory, new FakeOutput()).DisplayControls.MonitorsEnabled, "escolher ligado mesmo no padrão grava preferência");
            session.Prepare(); platform.Prepared = false;
            session.Prepare();
            Check(!session.DisplayControls.MonitorsEnabled, "recuperação usa somente TV temporariamente");
            Check(new DisplayPreferenceStore(directory).Load(), "recuperação não apaga última escolha do usuário");
            var recovered = new TvSession(directory, platform);
            Check(recovered.Active && !recovered.DisplayControls.MonitorsEnabled, "reinício com sessão mantém perfil temporário e snapshot");
            recovered.Restore();
            Check(recovered.DisplayControls.MonitorsEnabled && !recovered.Active, "fim da sessão volta à preferência original");
            recovered.Prepare();
            Check(recovered.DisplayControls.MonitorsEnabled, "próxima abertura respeita preferência, não fallback anterior");
            recovered.Restore();
            Check(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "gravações concluídas não deixam arquivos temporários");
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("PASS: padrão ligado, preferência durável após término abrupto, reserva/corrupção, legado e recuperação sem sobrescrever escolha.");
    }
}
