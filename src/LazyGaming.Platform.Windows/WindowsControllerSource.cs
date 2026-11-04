namespace LazyGaming;

public sealed class WindowsControllerSource(Func<string>? preference = null) : IControllerSource
{
    public IControllerConnection? Open() => (preference?.Invoke() ?? "auto") switch {
        "dualsense" => DualSenseHid.Open(),
        "xinput" => XInputConnection.Open(),
        "switch-pro" => DualSenseHid.Open(true),
        _ => (IControllerConnection?)DualSenseHid.Open() ?? (IControllerConnection?)XInputConnection.Open() ?? DualSenseHid.Open(true)
    };
}
