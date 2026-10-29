namespace LazyGaming;

public sealed class DoubleTap
{
    bool wasPressed; long began, first = -10000;
    public bool Update(bool pressed, long now)
    {
        bool fire = false;
        if (pressed && !wasPressed) began = now;
        if (!pressed && wasPressed) {
            if (now - began <= 300) { fire = now - first <= 400; first = fire ? -10000 : now; }
            else first = -10000;
        }
        wasPressed = pressed; return fire;
    }
    public void Reset() { wasPressed = false; first = -10000; }
}

public sealed class LauncherHold
{
    long began; bool holding, fired;
    public bool Update(bool pressed, long now) {
        if (!pressed) { holding = fired = false; return false; }
        if (!holding) { holding = true; began = now; }
        if (!fired && now - began >= 1000) { fired = true; return true; } return false;
    }
}
