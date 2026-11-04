using System.Runtime.InteropServices;

namespace LazyGaming;

public sealed class WindowsMouseOutput : IMouseOutput
{
    public void Send(MouseAction action)
    {
        Input[] inputs = action.Kind switch
        {
            "move" => [new() { Mouse = new() { X = action.X, Y = action.Y, Flags = 1 } }],
            "left-click" => [new() { Mouse = new() { Flags = 2 } }, new() { Mouse = new() { Flags = 4 } }],
            "left-down" => [new() { Mouse = new() { Flags = 2 } }],
            "left-up" => [new() { Mouse = new() { Flags = 4 } }],
            "right-click" => [new() { Mouse = new() { Flags = 8 } }, new() { Mouse = new() { Flags = 16 } }],
            _ => []
        };
        if (inputs.Length > 0) _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }
    [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public MouseInput Mouse; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, Input[] inputs, int size);
}
