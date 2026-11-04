namespace LazyGaming;

// Monotonic time is supplied so bursts/cooldowns can be tested without UI/sleeps.
public sealed class NoticePolicy
{
    readonly object gate = new();
    readonly Dictionary<string,long> shown = [];
    long last = long.MinValue;
    public bool Accept(string key, long now, int repeatMs = 30000, int spacingMs = 5000) {
        lock (gate) {
            if (shown.TryGetValue(key,out var previous) && now - previous < repeatMs) return false;
            if (last != long.MinValue && now - last < spacingMs) return false;
            if (shown.Count >= 32 && !shown.ContainsKey(key)) shown.Remove(shown.MinBy(p=>p.Value).Key);
            shown[key] = now; last = now; return true;
        }
    }
    public static int ReadingDuration(string text) => Math.Clamp(2000 + Math.Min(text.Length,150) * 30,3500,6500);
}
