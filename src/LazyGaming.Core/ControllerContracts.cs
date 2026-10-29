namespace LazyGaming;

[Flags]
public enum ControllerButtons { None = 0, Left = 1, Right = 2, Up = 4, Down = 8, Confirm = 16, Back = 32 }

// null means the report does not supply that capability, never a guessed release.
public sealed record ControllerState(ControllerButtons Buttons, bool Neutral,
    bool? Microphone, TouchFrame? Touch, bool TouchpadPressed, bool LauncherChord);

public interface IControllerAdapter
{
    string Id { get; }
    ControllerState? Decode(ReadOnlySpan<byte> report);
}

public interface IControllerConnection : IDisposable
{
    Stream Input { get; }
    int ReportLength { get; }
    IControllerAdapter Adapter { get; }
}

public interface IControllerSource { IControllerConnection? Open(); }
public interface IMouseOutput { void Send(MouseAction action); }

public sealed class DualSenseAdapter : IControllerAdapter
{
    public string Id => "dualsense";
    public ControllerState? Decode(ReadOnlySpan<byte> report)
    {
        int buttons = report.Length >= 78 && report[0] == 0x31 ? 9
            : report.Length == 64 && report[0] == 1 ? 8
            : report.Length >= 10 && report[0] == 1 ? 5 : -1;
        if (buttons < 0) return null;
        int stick = report[0] == 0x31 ? 2 : 1, hat = report[buttons] & 15;
        var value = ControllerButtons.None;
        if (hat is 5 or 6 or 7 || report[stick] < 64) value |= ControllerButtons.Left;
        if (hat is 1 or 2 or 3 || report[stick] > 192) value |= ControllerButtons.Right;
        if (hat is 0 or 1 or 7 || report[stick + 1] < 64) value |= ControllerButtons.Up;
        if (hat is 3 or 4 or 5 || report[stick + 1] > 192) value |= ControllerButtons.Down;
        if ((report[buttons] & 0x20) != 0) value |= ControllerButtons.Confirm;
        if ((report[buttons] & 0x40) != 0) value |= ControllerButtons.Back;
        return new(value, DualSenseReport.IsNeutral(report), DualSenseReport.MicrophonePressed(report),
            DualSenseReport.Parse(report), DualSenseReport.TouchpadPressed(report), DualSenseReport.LauncherChord(report));
    }
}
