using LazyGaming;

static class ControllerMenuTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run() {
        foreach (var format in new[] { "usb", "bluetooth", "basic" }) {
            var report = new byte[format == "usb" ? 64 : 78]; report[0] = format == "bluetooth" ? (byte)0x31 : (byte)1;
            int stick = format == "bluetooth" ? 2 : 1, buttons = format == "bluetooth" ? 9 : format == "usb" ? 8 : 5, triggers = format == "bluetooth" ? 6 : format == "usb" ? 5 : 8;
            for (int i = 0; i < 4; i++) report[stick + i] = 128;
            report[buttons] = 8;
            Check(DualSenseReport.IsNeutral(report), "neutralidade em " + format);
            report[buttons] = 0x48; Check(!DualSenseReport.IsNeutral(report), "círculo mantido não pode voltar ao jogo em " + format); report[buttons] = 8;
            report[buttons + 1] = 0x30; Check(!DualSenseReport.IsNeutral(report), "atalho de abrir precisa ser solto em " + format); report[buttons + 1] = 0;
            report[triggers] = 128; Check(!DualSenseReport.IsNeutral(report), "gatilho impede retorno com entrada mantida em " + format); report[triggers] = 0;
            report[stick + 2] = 220; Check(!DualSenseReport.IsNeutral(report), "analógico direito mantido impede retorno em " + format); report[stick + 2] = 128;
            report[buttons + 2] = 2; Check(!DualSenseReport.IsNeutral(report), "clique físico do touchpad também precisa ser solto em " + format);
        }
        Check(!DualSenseReport.IsNeutral(new byte[8]), "relatório desconhecido não confirma soltura");
        var neutral = new ControllerNeutralState();
        Check(!neutral.Ready(0), "sem amostra não comprova soltura");
        neutral.Observe(true, 100); Check(!neutral.Ready(179) && neutral.Ready(180), "exigir período estável de soltura");
        neutral.Observe(false, 200); neutral.Observe(true, 220); Check(!neutral.Ready(280) && neutral.Ready(300), "novo comando reinicia período de soltura");
        Check(!neutral.Ready(2300), "amostra antiga não comprova que o dispositivo continua solto");
        neutral.Observe(true, 2400); Check(!neutral.Ready(2400), "retorno após silêncio exige período neutro novo");
        neutral.Observe(true, 2480); Check(neutral.Ready(2480), "retoma com amostras atuais estáveis");
        var gate = new MenuNavigationGate();
        Check(!gate.Update(false, true), "painel sem foco não consome navegação");
        Check(!gate.Update(true, false), "botão anterior mantido na abertura não confirma opção");
        Check(gate.Update(true, true) && gate.Update(true, false), "após soltura inicial permite navegação/hold normal");
        Check(!gate.Update(false, false) && !gate.Update(true, false), "perder foco/fechar painel desarma consumo e exige nova soltura");
        Check(gate.Update(true, true), "retoma somente após soltar");
        Console.WriteLine("PASS: menu exige foco e soltura inicial; retorno aguarda estado neutro USB/BT/básico e evita botão mantido.");
    }
}
