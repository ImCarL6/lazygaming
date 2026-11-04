using System.Buffers.Binary;

namespace LazyGaming;

// XINPUT_GAMEPAD wire layout, copied by our polling source, never a HID guess.
public sealed class XInputAdapter : IControllerAdapter
{
    public string Id => "xinput";
    public ControllerState? Decode(ReadOnlySpan<byte> report) {
        if (report.Length != 12) return null;
        ushort b = BinaryPrimitives.ReadUInt16LittleEndian(report);
        return DecodeValues(b, report[2], report[3], BinaryPrimitives.ReadInt16LittleEndian(report[4..]),
            BinaryPrimitives.ReadInt16LittleEndian(report[6..]), BinaryPrimitives.ReadInt16LittleEndian(report[8..]), BinaryPrimitives.ReadInt16LittleEndian(report[10..]));
    }
    static ControllerState DecodeValues(ushort b, byte lt, byte rt, short lx, short ly, short rx, short ry) {
        var raw = PadButtons.None;
        (int Bit, PadButtons Button)[] mapping = [(0x1000, PadButtons.South), (0x2000, PadButtons.East), (0x4000, PadButtons.West), (0x8000, PadButtons.North),
            (0x100, PadButtons.LeftShoulder), (0x200, PadButtons.RightShoulder), (0x20, PadButtons.Select), (0x10, PadButtons.Start), (0x40, PadButtons.LeftStick), (0x80, PadButtons.RightStick)];
        foreach (var (bit, button) in mapping) if ((b & bit) != 0) raw |= button;
        if (lt > 30) raw |= PadButtons.LeftTrigger;
        if (rt > 30) raw |= PadButtons.RightTrigger;
        var nav = ControllerButtons.None;
        if ((b & 4) != 0 || lx < -16000) nav |= ControllerButtons.Left;
        if ((b & 8) != 0 || lx > 16000) nav |= ControllerButtons.Right;
        if ((b & 1) != 0 || ly > 16000) nav |= ControllerButtons.Up;
        if ((b & 2) != 0 || ly < -16000) nav |= ControllerButtons.Down;
        if (raw.HasFlag(PadButtons.South)) nav |= ControllerButtons.Confirm;
        if (raw.HasFlag(PadButtons.East)) nav |= ControllerButtons.Back;
        bool neutral = b == 0 && lt <= 12 && rt <= 12 && Math.Abs((int)lx) <= 5000 && Math.Abs((int)ly) <= 5000 && Math.Abs((int)rx) <= 5000 && Math.Abs((int)ry) <= 5000;
        return new(nav, neutral, null, null, false, false, raw, rx / 32768d, -ry / 32768d);
    }
}

// Nintendo Switch Pro full input report (0x30). USB initialization is deliberately
// not performed; accept only a real full report already supplied by the device.
public sealed class SwitchProAdapter : IControllerAdapter
{
    public string Id => "switch-pro";
    static double X(ReadOnlySpan<byte> r, int i) => ((r[i] | ((r[i + 1] & 15) << 8)) - 2048) / 2048d;
    static double Y(ReadOnlySpan<byte> r, int i) => -(((r[i + 1] >> 4) | (r[i + 2] << 4)) - 2048) / 2048d;
    public ControllerState? Decode(ReadOnlySpan<byte> r) {
        if (r.Length < 49 || r[0] != 0x30) return null;
        var raw = PadButtons.None;
        (int Index, int Bit, PadButtons Button)[] map = [(3,4,PadButtons.South),(3,8,PadButtons.East),(3,1,PadButtons.West),(3,2,PadButtons.North),
            (3,64,PadButtons.RightShoulder),(3,128,PadButtons.RightTrigger),(5,64,PadButtons.LeftShoulder),(5,128,PadButtons.LeftTrigger),
            (4,1,PadButtons.Select),(4,2,PadButtons.Start),(4,4,PadButtons.RightStick),(4,8,PadButtons.LeftStick)];
        foreach (var (index, bit, button) in map) if ((r[index] & bit) != 0) raw |= button;
        double lx = X(r,6), ly = Y(r,6), rx = X(r,9), ry = Y(r,9);
        var nav = ControllerButtons.None;
        if ((r[5] & 8) != 0 || lx < -.5) nav |= ControllerButtons.Left;
        if ((r[5] & 4) != 0 || lx > .5) nav |= ControllerButtons.Right;
        if ((r[5] & 2) != 0 || ly < -.5) nav |= ControllerButtons.Up;
        if ((r[5] & 1) != 0 || ly > .5) nav |= ControllerButtons.Down;
        if (raw.HasFlag(PadButtons.South)) nav |= ControllerButtons.Confirm;
        if (raw.HasFlag(PadButtons.East)) nav |= ControllerButtons.Back;
        return new(nav, r[3] == 0 && r[4] == 0 && r[5] == 0 && Math.Abs(lx) <= .15 && Math.Abs(ly) <= .15 && Math.Abs(rx) <= .15 && Math.Abs(ry) <= .15,
            null, null, false, false, raw, rx, ry);
    }
}
