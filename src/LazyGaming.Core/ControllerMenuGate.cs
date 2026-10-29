namespace LazyGaming;

// Abertura espera soltura inicial; depois permite navegação normal, inclusive hold.
public sealed class MenuNavigationGate
{
    bool armed;
    public bool Update(bool focusedPanel, bool released) {
        if (!focusedPanel) { armed = false; return false; }
        if (!armed && released) armed = true;
        return armed;
    }
}
public sealed class ControllerNeutralState
{
    readonly object gate = new();
    bool neutral;
    long since, observed;
    public void Observe(bool released, long now) { lock (gate) {
        if (!released || !neutral || now - observed > 2000) since = now;
        neutral = released; observed = now;
    } }
    public bool Ready(long now) { lock (gate) return neutral && now - observed <= 2000 && now - since >= 80; }
}
