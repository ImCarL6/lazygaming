using LazyGaming;
using System.Drawing;

static class GameResolutionTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run() {
        var root = Path.Combine(Path.GetTempPath(), "lg-video-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try {
            var path = Path.Combine(root, "settings.jkr");
            var original = "return {[\"WINDOW\"]={[\"screenmode\"]=\"Borderless\",[\"selected_display\"]=2,[\"vsync\"]=1,[\"DISPLAYS\"]={[1]={[\"w\"]=4096,},},},[\"QUEUED_CHANGE\"]={},[\"SOUND\"]={[\"volume\"]=50,},[\"language\"]=\"pt_BR\",[\"note\"]=\"brace } and \\\"quotes\\\"\",}";
            File.WriteAllBytes(path, BalatroDisplayProfile.Encode(original)); File.WriteAllText(Path.Combine(root, "love.dll"), "fixture");
            var game = new Game("any-id", "Any title", "unused", Path.Combine(root, "Balatro.exe"));
            BalatroDisplayProfile.Prepare(game, root, () => new(1, new(0, 0, 3840, 2160)), path);
            var configured = BalatroDisplayProfile.Decode(File.ReadAllBytes(path));
            Check(configured.Contains("[\"screenmode\"]=\"Fullscreen\"") && configured.Contains("[\"w\"]=3840,[\"h\"]=2160") && configured.Contains("[\"selected_display\"]=1"), "perfil usa dimensão e monitor ativos, não maior modo anunciado nem ID/título fixo");
            Check(configured.Contains("[\"vsync\"]=1") && configured.Contains("[\"language\"]=\"pt_BR\"") && configured.Contains("[\"DISPLAYS\"]={[1]={[\"w\"]=4096"), "outros campos e tabelas são preservados sem executar Lua");
            BalatroDisplayProfile.Prepare(game, root, () => new(2, new(-2560, 0, 2560, 1440)), path);
            configured = BalatroDisplayProfile.Decode(File.ReadAllBytes(path));
            Check(configured.Contains("[\"w\"]=2560,[\"h\"]=1440"), "outro PC/tela usa sua resolução, sem forçar 4K");
            File.WriteAllBytes(path, BalatroDisplayProfile.Encode(configured.Replace("[\"volume\"]=50", "[\"volume\"]=35").Replace("pt_BR", "en-us")));
            BalatroDisplayProfile.Restore(root);
            var restored = BalatroDisplayProfile.Decode(File.ReadAllBytes(path));
            Check(restored == original.Replace("[\"volume\"]=50", "[\"volume\"]=35").Replace("pt_BR", "en-us"), "restauração seletiva recupera vídeo inicial e preserva alterações feitas no jogo");
            BalatroDisplayProfile.Restore(root);
            Check(BalatroDisplayProfile.Configure("return {[\"WINDOW\"]={},}", 1, new(0, 0, 3840, 2160)) == null, "schema parcial não é adivinhado");
            Check(BalatroDisplayProfile.Configure(original.Replace("[\"selected_display\"]=2", "[\"selected_display\"]=2,[\"selected_display\"]=1"), 1, new(0, 0, 3840, 2160)) == null, "campos duplicados recusados");
            Exception? failure = null;
            var thread = new Thread(() => {
                try {
                    using var dpi = WindowFocus.PhysicalCoordinates();
                    using var form = new System.Windows.Forms.Form { StartPosition = System.Windows.Forms.FormStartPosition.Manual, Bounds = new(100, 100, 640, 480) };
                    var handle = form.Handle;
                    Check(!WindowFocus.MatchesBounds(handle, form.Bounds), "retângulo externo correto com bordas não comprova resolução útil");
                    form.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
                    Check(WindowFocus.MatchesBounds(form.Handle, form.Bounds), "janela real sem bordas confirma área útil e posição físicas");
                } catch (Exception e) { failure = e; }
            }); thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
            if (failure != null) throw failure;
            var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Balatro", "settings.jkr");
            if (File.Exists(real)) Console.WriteLine("INFO: preferências reais Balatro reconhecidas (somente leitura): " +
                (BalatroDisplayProfile.Configure(BalatroDisplayProfile.Decode(File.ReadAllBytes(real)), 1, new(0, 0, 3840, 2160)) != null));
        } finally { Directory.Delete(root, true); }
        Console.WriteLine("PASS: resolução ativa por tela, perfil Balatro reversível sem alterar campanha e área útil de janela Windows verificada.");
    }
}
