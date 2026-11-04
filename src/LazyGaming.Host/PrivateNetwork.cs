using System.Net;
using System.Net.Sockets;

namespace LazyGaming;

public static class PrivateNetwork
{
    public static bool IsLocal(IPAddress address) {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168;
    }
}
