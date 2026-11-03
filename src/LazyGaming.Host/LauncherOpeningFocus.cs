namespace LazyGaming;

public enum LauncherFocusAction { Wait, Repair, Finish, Cancel, Fail }

// Confirma foco estável e observa somente a abertura. Nunca disputa uma nova
// entrada de teclado/mouse nem tenta ativar sobre outro aplicativo.
public sealed class LauncherOpeningFocus(long started, uint inputStamp)
{
    long? focusedSince;
    long nextRepair;
    int repairs;
    bool confirmed;
    public bool Ready { get; private set; }
    public int Repairs => repairs;
    public bool InputChanged { get; private set; }
    public LauncherFocusAction Update(long now, bool focused, uint currentInput, bool relatedForeground)
    {
        Ready = false;
        InputChanged = currentInput != inputStamp;
        if (InputChanged) return LauncherFocusAction.Cancel;
        if (confirmed && !focused && !relatedForeground) return LauncherFocusAction.Cancel;
        if (focused) {
            focusedSince ??= now;
            Ready = now - focusedSince >= 200;
            confirmed |= Ready;
        } else focusedSince = null;
        if (now - started >= 5000) return Ready ? LauncherFocusAction.Finish : LauncherFocusAction.Fail;
        if (confirmed && inputStamp == uint.MaxValue) return focused ? LauncherFocusAction.Finish : LauncherFocusAction.Cancel;
        if (focused || now < nextRepair) return LauncherFocusAction.Wait;
        if (repairs >= 5) return LauncherFocusAction.Fail;
        repairs++; nextRepair = now + 500;
        return LauncherFocusAction.Repair;
    }
}
