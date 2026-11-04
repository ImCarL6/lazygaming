using System.Diagnostics;
using System.Net.NetworkInformation;

namespace LazyGaming;

public static class HostStartup
{
    // Windows/firewall can time out a loopback connection even with no listener.
    // Only skip the network probe when local OS evidence proves absence.
    public static bool CanStart(int port, Func<bool>? hasListener = null, Func<bool>? anotherHelper = null) {
        try {
            return !(hasListener ?? (() => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p => p.Port == port)))()
                && !(anotherHelper ?? AnotherHelper)();
        } catch (Exception e) when (e is NetworkInformationException or System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }
    static bool AnotherHelper() {
        var processes = Process.GetProcessesByName("LazyGaming.Host");
        try { return processes.Any(p => p.Id != Environment.ProcessId); }
        finally { foreach(var process in processes) process.Dispose(); }
    }
}

public sealed class HostInstanceLease : IDisposable
{
    readonly FileStream handle;
    HostInstanceLease(FileStream handle) { this.handle=handle; }
    public static HostInstanceLease Acquire(string directory) {
        Directory.CreateDirectory(directory);
        try { return new(new FileStream(Path.Combine(directory,"host.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)); }
        catch(IOException e) { throw new InvalidOperationException("O perfil do LazyGaming está em uso ou temporariamente indisponível. Aguarde e tente novamente.",e); }
    }
    public void Dispose() => handle.Dispose();
}
