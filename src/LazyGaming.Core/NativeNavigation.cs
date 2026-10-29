namespace LazyGaming;

// Shared UI wire protocol. Repeat belongs to navigation, never Confirm or Back.
public sealed class NativeNavigation
{
    static readonly (ControllerButtons Button, int Key)[] mapping = [
        (ControllerButtons.Left, 37), (ControllerButtons.Right, 39),
        (ControllerButtons.Up, 38), (ControllerButtons.Down, 40),
        (ControllerButtons.Confirm, 13), (ControllerButtons.Back, 10009)];
    readonly Dictionary<int, long> held = new();
    public HashSet<int> Repeated { get; } = new();
    public List<int> Update(ReadOnlySpan<byte> report, long now, bool active) =>
        Update(new DualSenseAdapter().Decode(report), now, active);
    public List<int> Update(ControllerState? state, long now, bool active)
    {
        var result = new List<int>();
        Repeated.Clear();
        if (!active || state == null) { held.Clear(); return result; }
        foreach (var (button, key) in mapping) {
            if (!state.Buttons.HasFlag(button)) { held.Remove(key); continue; }
            bool first = !held.TryGetValue(key, out var next);
            if (first || (key is >= 37 and <= 40 && now >= next)) {
                result.Add(key); if (!first) Repeated.Add(key);
                held[key] = now + (first ? 300 : 140);
            }
        }
        return result;
    }
}
