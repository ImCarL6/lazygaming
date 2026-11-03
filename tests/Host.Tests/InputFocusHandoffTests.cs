using LazyGaming;

static class InputFocusHandoffTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    sealed class Connection(Action detach) : IDisposable { public void Dispose() => detach(); }
    public static void Run()
    {
        Check(InputFocusHandoff.Ready(true, false, true, true), "janela restaurada/visível e foco completo confirma entrega");
        Check(!InputFocusHandoff.Ready(true, true, true, true), "janela minimizada não pode confirmar foco mesmo com estado de fila antigo");
        Check(!InputFocusHandoff.Ready(false, false, true, true), "janela oculta não confirma entrega");
        int activate = 0, connect = 0, assign = 0, detach = 0;
        bool ready = false;
        Func<IDisposable> queues = () => { connect++; return new Connection(() => detach++); };
        Check(InputFocusHandoff.Try(() => ready, () => { activate++; return false; }, queues,
            () => { assign++; ready = true; }), "recusa inicial deve tentar filas e conferir foco completo");
        Check(activate == 1 && connect == 1 && assign == 1 && detach == 1, "filas temporárias liberadas após entrega");
        Check(InputFocusHandoff.Try(() => ready, () => throw new Exception("não ativar de novo"),
            () => throw new Exception("não reinicializar teclas"), () => throw new Exception("não refocar")), "foco confirmado não interfere novamente no controle");
        ready = false;
        Check(!InputFocusHandoff.Try(() => ready, () => true, queues, () => { }), "foreground sem foco de entrada não confirma entrega");
        int beforeDetach = detach;
        try { InputFocusHandoff.Try(() => false, () => false, queues, () => throw new InvalidOperationException("fixture")); }
        catch (InvalidOperationException) { }
        Check(detach == beforeDetach + 1, "falha deve desconectar filas de entrada");
        Console.WriteLine("PASS: foco completo após recusa inicial, foreground isolado recusado, filas liberadas e controle preservado quando foco já entregue.");
    }
}
