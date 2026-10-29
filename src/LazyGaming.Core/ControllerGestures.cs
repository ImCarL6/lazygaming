namespace LazyGaming;

public record TouchPoint(int Id, int X, int Y);
public record TouchFrame(TouchPoint? First, TouchPoint? Second);
public record MouseAction(string Kind, int X = 0, int Y = 0);

public static class DualSenseReport
{
    public static bool IsNeutral(ReadOnlySpan<byte> report) {
        bool full = report.Length >= 78 && report[0] == 0x31;
        bool usb = report.Length == 64 && report[0] == 1;
        bool basic = report.Length >= 10 && report[0] == 1 && !usb;
        if (!full && !usb && !basic) return false;
        int stick = full ? 2 : 1, buttons = full ? 9 : usb ? 8 : 5, triggers = full ? 6 : usb ? 5 : 8;
        for (int i = stick; i < stick + 4; i++) if (Math.Abs(report[i] - 128) > 20) return false;
        return report[buttons] == 8 && report[buttons + 1] == 0 && (report[buttons + 2] & 7) == 0 && report[triggers] <= 12 && report[triggers + 1] <= 12;
    }
    public static bool? MicrophonePressed(ReadOnlySpan<byte> report)
    {
        if (report.Length == 64 && report[0] == 1) return (report[10] & 4) != 0;
        if (report.Length >= 78 && report[0] == 0x31) return (report[11] & 4) != 0;
        return null;
    }
    public static bool TouchpadPressed(ReadOnlySpan<byte> report) {
        int index = report.Length >= 78 && report[0] == 0x31 ? 11 : report.Length == 64 && report[0] == 1 ? 10 : report.Length >= 10 && report[0] == 1 ? 7 : -1;
        return index >= 0 && (report[index] & 2) != 0;
    }
    public static bool LauncherChord(ReadOnlySpan<byte> report)
    {
        // Create e Options são bits4/5 no segundo byte de botões em ambos os modos.
        int index = report.Length >= 78 && report[0] == 0x31 ? 10 : report.Length == 64 && report[0] == 1 ? 9 : report.Length >= 10 && report[0] == 1 ? 6 : -1;
        return index >= 0 && (report[index] & 0x30) == 0x30;
    }
    public static TouchFrame? Parse(ReadOnlySpan<byte> report)
    {
        int offset;
        if (report.Length == 64 && report[0] == 1) offset = 0;
        else if (report.Length >= 78 && report[0] == 0x31) offset = 1;
        else return null; // Bluetooth básico não tem coordenadas; não interpretar padding como toque.
        static TouchPoint? Point(ReadOnlySpan<byte> data, int start)
        {
            if ((data[start] & 0x80) != 0) return null;
            int x = data[start + 1] | ((data[start + 2] & 15) << 8);
            int y = (data[start + 2] >> 4) | (data[start + 3] << 4);
            return x < 1920 && y < 1080 ? new(data[start] & 127, x, y) : null;
        }
        return new(Point(report, 33 + offset), Point(report, 37 + offset));
    }
}
public sealed class HoldToggle
{
    long? began;
    bool fired;
    public bool Holding => began.HasValue;
    public void Reset() { began = null; fired = false; }
    public bool Update(bool pressed, long now)
    {
        if (!pressed) { Reset(); return false; }
        began ??= now;
        if (!fired && now - began.Value >= 1500) { fired = true; return true; }
        return false;
    }
}

public sealed class TouchpadGesture
{
    TouchPoint? previous;
    long began;
    int distance, fingers;
    bool touching;
    public bool Pending => touching;
    public void Reset() { previous = null; touching = false; distance = 0; fingers = 0; }
    public List<MouseAction> Update(TouchFrame frame, long time)
    {
        var actions = new List<MouseAction>();
        int count = (frame.First != null ? 1 : 0) + (frame.Second != null ? 1 : 0);
        var point = frame.First ?? frame.Second;
        if (count == 0)
        {
            // Toque leve não clica: clique físico é tratado separadamente.
            Reset(); return actions;
        }
        if (!touching) { began = time; touching = true; distance = 0; }
        fingers = Math.Max(fingers, count);
        if (previous != null && point != null && previous.Id == point.Id)
        {
            int dx = point.X - previous.X, dy = point.Y - previous.Y;
            distance += Math.Abs(dx) + Math.Abs(dy);
            if (count == 1 && fingers == 1 && Math.Abs(dx) < 250 && Math.Abs(dy) < 250 && (dx != 0 || dy != 0))
                actions.Add(new("move", (int)Math.Round(dx * 1.4), (int)Math.Round(dy * 1.4)));
        }
        else if (previous != null) distance = 1000; // Identificador mudou: não saltar nem clicar.
        previous = point; return actions;
    }
}
