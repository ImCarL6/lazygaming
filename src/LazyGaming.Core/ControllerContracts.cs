namespace LazyGaming;

[Flags]
public enum ControllerButtons { None = 0, Left = 1, Right = 2, Up = 4, Down = 8, Confirm = 16, Back = 32, Volume = 64 }

// null means the report does not supply that capability, never a guessed release.
public sealed record ControllerState(ControllerButtons Buttons, bool Neutral,
    bool? Microphone, TouchFrame? Touch, bool TouchpadPressed, bool LauncherChord,
    PadButtons? RawButtons = null, double RightX = 0, double RightY = 0);

[Flags]
public enum PadButtons { None = 0, South = 1, East = 2, West = 4, North = 8,
    LeftShoulder = 16, RightShoulder = 32, Select = 64, Start = 128,
    LeftStick = 256, RightStick = 512, LeftTrigger = 1024, RightTrigger = 2048,
    Microphone = 4096, Touchpad = 8192 }

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
public interface IControllerFeatures { Task<bool> EnableFullReports(CancellationToken cancellation); }
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
        int b = report[buttons + 1];
        var raw = PadButtons.None;
        if ((report[buttons] & 0x20) != 0) raw |= PadButtons.South;
        if ((report[buttons] & 0x40) != 0) raw |= PadButtons.East;
        if ((report[buttons] & 0x10) != 0) raw |= PadButtons.West;
        if ((report[buttons] & 0x80) != 0) raw |= PadButtons.North;
        for (int i = 0; i < 2; i++) if ((b & (1 << i)) != 0) raw |= (PadButtons)(16 << i);
        if ((b & 0x10) != 0) raw |= PadButtons.Select;
        if ((b & 0x20) != 0) raw |= PadButtons.Start;
        if ((b & 0x40) != 0) raw |= PadButtons.LeftStick;
        if ((b & 0x80) != 0) raw |= PadButtons.RightStick;
        if ((b & 4) != 0) raw |= PadButtons.LeftTrigger;
        if ((b & 8) != 0) raw |= PadButtons.RightTrigger;
        if (DualSenseReport.MicrophonePressed(report) == true) raw |= PadButtons.Microphone;
        if (DualSenseReport.TouchpadPressed(report)) raw |= PadButtons.Touchpad;
        return new(value, DualSenseReport.IsNeutral(report), DualSenseReport.MicrophonePressed(report),
            DualSenseReport.Parse(report), DualSenseReport.TouchpadPressed(report), DualSenseReport.LauncherChord(report),
            raw, (report[stick + 2] - 128) / 128d, (report[stick + 3] - 128) / 128d);
    }
}
