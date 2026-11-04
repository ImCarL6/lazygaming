namespace LazyGaming;

public sealed record ShortcutBinding(PadButtons Buttons, string Gesture = "hold", int HoldMs = 1000);
public sealed record ControllerOptions(
    ShortcutBinding? Launcher = null, ShortcutBinding? Mouse = null,
    double Sensitivity = 1100, double Deadzone = .18, double Curve = 2,
    PadButtons LeftClick = PadButtons.RightShoulder, PadButtons RightClick = PadButtons.LeftShoulder,
    bool SwapConfirmBack = false, bool LegacyMicrophone = true)
{
    public ShortcutBinding LauncherBinding => Launcher ?? new(PadButtons.Select | PadButtons.Start);
    public ShortcutBinding MouseBinding => Mouse ?? new(PadButtons.LeftStick | PadButtons.RightStick);
    public void Validate() {
        static bool Known(PadButtons b) => b != PadButtons.None && ((int)b & ~16383) == 0;
        foreach (var binding in new[] { LauncherBinding, MouseBinding })
            if (!Known(binding.Buttons) || binding.Gesture is not ("hold" or "double-tap") || binding.HoldMs is < 500 or > 3000)
                throw new InvalidOperationException("Escolha botões e um gesto válido para o atalho.");
        if (LauncherBinding.Buttons == MouseBinding.Buttons && LauncherBinding.Gesture == MouseBinding.Gesture || !Known(LeftClick) || !Known(RightClick) || LeftClick == RightClick)
            throw new InvalidOperationException("Escolha atalhos e cliques diferentes.");
        if (!double.IsFinite(Sensitivity) || Sensitivity is < 100 or > 4000 || !double.IsFinite(Deadzone) || Deadzone is < .05 or > .4 || !double.IsFinite(Curve) || Curve is < 1 or > 3)
            throw new InvalidOperationException("Ajuste a sensibilidade, zona morta e curva dentro dos limites.");
    }
}

public sealed class ConfiguredShortcut
{
    readonly DoubleTap tap = new();
    bool holding, fired, armed; long began;
    public bool Holding => holding;
    public void Reset() { tap.Reset(); holding = fired = armed = false; }
    public bool Update(PadButtons buttons, ShortcutBinding binding, long now) {
        bool pressed = (buttons & binding.Buttons) == binding.Buttons;
        // A device connected with a held button must first release it.
        if (!pressed) armed = true;
        if (!armed) return false;
        if (binding.Gesture == "double-tap") return tap.Update(pressed, now);
        if (!pressed) { holding = fired = false; return false; }
        if (!holding) { holding = true; began = now; }
        if (!fired && now - began >= binding.HoldMs) { fired = true; return true; }
        return false;
    }
}

public sealed class StickPointer
{
    long? previous; double remainderX, remainderY;
    public void Reset() { previous = null; remainderX = remainderY = 0; }
    public MouseAction? Update(double x, double y, long now, ControllerOptions options) {
        double seconds = previous.HasValue ? Math.Clamp(now - previous.Value, 0, 50) / 1000d : 0;
        previous = now;
        double length = Math.Sqrt(x * x + y * y);
        if (length <= options.Deadzone || !double.IsFinite(length)) { remainderX = remainderY = 0; return null; }
        double speed = Math.Pow(Math.Clamp((length - options.Deadzone) / (1 - options.Deadzone), 0, 1), options.Curve) * options.Sensitivity * seconds;
        remainderX += x / length * speed; remainderY += y / length * speed;
        int dx = (int)remainderX, dy = (int)remainderY;
        remainderX -= dx; remainderY -= dy;
        return dx == 0 && dy == 0 ? null : new("move", dx, dy);
    }
}
