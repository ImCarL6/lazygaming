using LazyGaming;

static class TaskbarOverflowTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        bool open = false;
        int toggles = 0;
        Action toggle = () => { toggles++; open = !open; };
        Check(!TaskbarOverflow.Collapse(() => open, toggle, () => true) && toggles == 0, "menu fechado nunca deve ser aberto pelo auxiliar");
        open = true;
        Check(TaskbarOverflow.Collapse(() => open, toggle, () => true) && !open && toggles == 1, "menu aberto pode ser fechado sem input no jogo");
        open = true;
        Check(!TaskbarOverflow.Collapse(() => open, toggle, () => false) && open && toggles == 1, "perda de autorização/foco do fluxo não altera o menu");
        int checks = 0;
        Check(!TaskbarOverflow.Collapse(() => open, toggle, () => ++checks == 1) && toggles == 1, "intervenção do usuário durante a consulta cancela a ação");
        int reads = 0;
        Check(!TaskbarOverflow.Collapse(() => ++reads == 1, toggle, () => true) && toggles == 1, "mudança para fechado durante a consulta não deve reabrir");
        Console.WriteLine("PASS: menu de estouro só fecha quando aberto/autorizado; estado fechado e intervenção manual não recebem ações.");
    }
}
