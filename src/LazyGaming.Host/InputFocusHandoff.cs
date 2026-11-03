namespace LazyGaming;

// Foreground e foco de entrada precisam ser confirmados juntos. Uma recusa
// inicial não encerra a tentativa antes de conectar a fila do jogo verificado.
public static class InputFocusHandoff
{
    public static bool Ready(bool visible, bool minimized, bool foreground, bool inputFocused) =>
        visible && !minimized && foreground && inputFocused;
    public static bool Try(Func<bool> ready, Func<bool> activate, Func<IDisposable> connect, Action assign)
    {
        if (ready()) return true;
        activate();
        if (ready()) return true;
        using (connect()) { assign(); }
        return ready();
    }
}
