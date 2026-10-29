using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LazyGaming;

public static class ProcessImage
{
    public sealed record Observation(string? Image, bool Exited, int Error);
    public static Observation ResolveObservation(Func<Observation> observe, Func<bool> stillListed, Action pause) {
        var result = observe();
        if (result.Image == null && !result.Exited) {
            pause(); result = observe();
            if (result.Image == null && !result.Exited && !stillListed()) return result with { Exited = true };
        }
        return result;
    }
    // A imagem e o estado vêm do mesmo objeto de processo, sem reabrir o PID
    // com permissões diferentes durante a saída.
    public static Observation Observe(int pid) {
        using var handle = OpenProcess(0x1000, false, (uint)pid);
        if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); return new(null, error == 87, error); }
        if (GetExitCodeProcess(handle, out var code) && code != 259) return new(null, true, 0);
        var path = PathOf(handle); int imageError = path == null ? Marshal.GetLastWin32Error() : 0;
        bool exited = GetExitCodeProcess(handle, out code) && code != 259;
        return new(path, exited, imageError);
    }
    public static IProcessLifetime Track(int pid, string expectedImage) {
        var handle = OpenProcess(0x101000, false, (uint)pid); bool synchronized = !handle.IsInvalid;
        if (!synchronized) { handle.Dispose(); handle = OpenProcess(0x1000, false, (uint)pid); }
        try {
            if (handle.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var image = PathOf(handle);
            if (image == null) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            if (!string.Equals(image, expectedImage, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("O processo mudou; atualize o estado.");
            return new Lifetime(handle, synchronized);
        } catch { handle.Dispose(); throw; }
    }
    sealed class Lifetime(SafeProcessHandle handle, bool synchronized) : IProcessLifetime {
        public bool Exited => synchronized ? WaitForSingleObject(handle, 0) == 0 : GetExitCodeProcess(handle, out var code) && code != 259;
        public void Dispose() => handle.Dispose();
    }
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
    // Consulta de imagem com acesso limitado; não solicita leitura da memória do jogo.
    public static string? PathOf(int pid)
    {
        using var handle = OpenProcess(0x1000, false, (uint)pid);
        if (handle.IsInvalid) return null;
        return PathOf(handle);
    }
    static string? PathOf(SafeProcessHandle handle) {
        uint size = 32768; var text = new StringBuilder((int)size);
        return QueryFullProcessImageName(handle, 0, text, ref size) ? text.ToString() : null;
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref uint size);
}
