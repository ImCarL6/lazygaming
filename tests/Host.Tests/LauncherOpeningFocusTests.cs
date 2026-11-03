using LazyGaming;

static class LauncherOpeningFocusTests
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run()
    {
        var guard = new LauncherOpeningFocus(0, 42);
        Check(guard.Update(0, true, 42, true) == LauncherFocusAction.Wait && !guard.Ready, "um instante em foreground não confirma abertura");
        guard.Update(200, true, 42, true);
        Check(guard.Ready, "foco de entrada estável libera abertura");
        Check(guard.Update(1200, false, 42, true) == LauncherFocusAction.Repair, "jogo retomando foco após abertura recebe reparo sem reabrir painel");
        guard.Update(1250, true, 42, true); guard.Update(1450, true, 42, true);
        Check(guard.Ready && guard.Repairs == 1, "reparo confirma estabilidade novamente");
        Check(guard.Update(5000, true, 42, true) == LauncherFocusAction.Finish, "conferência termina no prazo");
        var before = guard.Repairs;
        Check(guard.Update(5100, false, 42, true) == LauncherFocusAction.Fail && guard.Repairs == before, "prazo vencido nunca faz nova ativação");

        var manual = new LauncherOpeningFocus(0, 10);
        manual.Update(0, true, 10, true); manual.Update(200, true, 10, true);
        Check(manual.Update(250, false, 11, true) == LauncherFocusAction.Cancel && manual.Repairs == 0, "clique/AltTab cancela antes de recuperar foco do jogo");
        var other = new LauncherOpeningFocus(0, 10);
        other.Update(0, true, 10, true); other.Update(200, true, 10, true);
        Check(other.Update(250, false, 10, false) == LauncherFocusAction.Cancel, "após abertura outro aplicativo não recebe disputa de foco");
        var failed = new LauncherOpeningFocus(0, 10);
        for (int i = 0; i < 5; i++) Check(failed.Update(i * 500, false, 10, true) == LauncherFocusAction.Repair, "tentativa limitada");
        Check(failed.Update(2500, false, 10, true) == LauncherFocusAction.Fail && failed.Repairs == 5, "recusa persistente encerra tentativas");
        var unknown = new LauncherOpeningFocus(0, uint.MaxValue);
        unknown.Update(0, true, uint.MaxValue, true);
        Check(unknown.Update(200, true, uint.MaxValue, true) == LauncherFocusAction.Finish, "sem amostra confiável não observar/disputar foco depois da abertura");

        var processor = new ControllerProcessor();
        var neutral = new ControllerState(ControllerButtons.None, true, false, null, false, false);
        var right = neutral with { Buttons = ControllerButtons.Right, Neutral = false };
        processor.Process(neutral, 0, true, true); processor.Process(neutral, 80, true, true);
        Check(processor.NavigationArmed, "menu confirma soltura inicial");
        processor.Process(right, 100, true, false);
        Check(!processor.NavigationArmed, "perda de foco desarma navegação");
        Check(processor.Process(right, 200, true, true).Navigation.Count == 0, "restaurar foco não reutiliza tecla mantida");
        processor.Process(neutral, 300, true, true); processor.Process(neutral, 380, true, true);
        Check(processor.Process(right, 400, true, true).Navigation.Single().Key == 39, "retoma controle na mesma abertura após foco e soltura");
        processor.ResetNavigation(410);
        Check(!processor.NavigationArmed && processor.Process(right, 420, true, true).Navigation.Count == 0,
            "reparo muito rápido também exige soltura, mesmo sem relatório HID observar a perda de foco");
        processor.Process(neutral, 500, true, true); processor.Process(neutral, 580, true, true);
        Check(processor.Process(right, 600, true, true).Navigation.Single().Key == 39, "reset explícito rearma após soltura sem reiniciar leitor HID");
        Console.WriteLine("PASS: foco tardio reparado na mesma abertura, estabilidade, limite/cancelamento por entrada manual e controle rearmado após soltura.");
    }
}
