namespace LazyGaming;

public sealed class WindowsControllerSource : IControllerSource
{
    public IControllerConnection? Open() => DualSenseHid.Open();
}
