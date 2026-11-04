using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace LazyGaming;

// Integração com o Windows somente do usuário atual: iniciar com o Windows (HKCU Run) e atalhos.
public static class PcIntegration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "LazyGaming";
    static string Exe => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "LazyGaming.Host.exe");
    static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "LazyGaming.lnk");
    static string MenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "LazyGaming", "LazyGaming.lnk");
    public static bool StartsWithWindows() {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Name) is string value && value.Contains(Exe, StringComparison.OrdinalIgnoreCase);
    }
    public static void SetStartWithWindows(bool enabled) {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(Name, $"\"{Exe}\" --startup");
        else if (key.GetValue(Name) != null) key.DeleteValue(Name, false);
    }
    public static bool HasDesktopShortcut() => File.Exists(DesktopLink);
    public static void SetDesktopShortcut(bool enabled) {
        if (enabled) CreateLink(DesktopLink, "--open-launcher", "Abrir o LazyGaming");
        else if (File.Exists(DesktopLink)) File.Delete(DesktopLink);
    }
    // Busca do Windows/menu Iniciar sempre encontra o app.
    public static void EnsureStartMenu() { if (!File.Exists(MenuLink)) CreateLink(MenuLink, "--open-launcher", "Abrir o LazyGaming"); }
    static void CreateLink(string path, string arguments, string description) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Não foi possível criar o atalho neste Windows.");
        dynamic shell = Activator.CreateInstance(type)!;
        try {
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = Exe; link.Arguments = arguments; link.WorkingDirectory = Path.GetDirectoryName(Exe); link.IconLocation = Exe + ",0"; link.Description = description; link.Save();
            Marshal.FinalReleaseComObject(link);
        } finally { Marshal.FinalReleaseComObject(shell); }
    }
}

// Impede que o botão PS/Xbox/Home abra o Steam Big Picture por cima do launcher ou do jogo.
// O Steam lê o controle diretamente; sem driver virtual, a proteção é: (1) opção do próprio Steam
// "botão Guide foca o Steam" desligada em todas as contas (só com o Steam fechado, com backup) e
// (2) fechar a janela do Big Picture assim que ela aparecer durante o launcher ou uma partida.
public sealed class SteamGuideGuard(Func<string> steamPath)
{
    long nextClose, nextConfig;
    public int Closed { get; private set; }
    public string? ConfigNote { get; private set; }
    public void Tick(bool enabled, bool active) {
        if (!enabled) return;
        long now = Environment.TickCount64;
        if (now >= nextConfig) { nextConfig = now + 30000; try { ApplySteamSetting(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ConfigNote = "Configuração do Steam indisponível no momento."; } }
        if (!active || now < nextClose) return;
        foreach (var window in BigPictureWindows()) { PostMessage(window, 0x0010, 0, 0); Closed++; nextClose = now + 1500; }
    }
    void ApplySteamSetting() {
        if (Process.GetProcessesByName("steam").Length > 0) { ConfigNote ??= "Será aplicado ao Steam quando ele estiver fechado; até lá, o Big Picture é fechado automaticamente."; return; }
        var userdata = Path.Combine(steamPath(), "userdata"); if (!Directory.Exists(userdata)) { ConfigNote = null; return; }
        int changed = 0;
        foreach (var file in Directory.EnumerateDirectories(userdata).Select(d => Path.Combine(d, "config", "localconfig.vdf")).Where(File.Exists)) {
            var text = File.ReadAllText(file);
            var next = DisableGuide(text);
            if (next == text) continue;
            var backup = file + ".lazygaming.bak"; if (!File.Exists(backup)) File.Copy(file, backup);
            File.WriteAllText(file, next, new UTF8Encoding(false)); changed++;
        }
        ConfigNote = "Opção do Steam aplicada" + (changed > 0 ? " agora." : ".");
    }
    public static string DisableGuide(string vdf) {
        var match = System.Text.RegularExpressions.Regex.Match(vdf, "\"Controller_CheckGuideButton\"\\s*\"(\\d)\"");
        if (match.Success) return match.Groups[1].Value == "0" ? vdf : vdf.Remove(match.Groups[1].Index, 1).Insert(match.Groups[1].Index, "0");
        var root = System.Text.RegularExpressions.Regex.Match(vdf, "\"UserLocalConfigStore\"\\s*\\{");
        return root.Success ? vdf.Insert(root.Index + root.Length, "\n\t\"Controller_CheckGuideButton\"\t\t\"0\"") : vdf;
    }
    static List<nint> BigPictureWindows() {
        var steam = new HashSet<uint>(Process.GetProcessesByName("steamwebhelper").Concat(Process.GetProcessesByName("steam")).Select(p => { using (p) return (uint)p.Id; }));
        var found = new List<nint>(); if (steam.Count == 0) return found;
        EnumWindows((window, _) => {
            if (!IsWindowVisible(window)) return true;
            GetWindowThreadProcessId(window, out var pid); if (!steam.Contains(pid)) return true;
            var title = new StringBuilder(256); GetWindowText(window, title, title.Capacity);
            if (title.ToString().Contains("Big Picture", StringComparison.OrdinalIgnoreCase)) found.Add(window);
            return true;
        }, 0);
        return found;
    }
    delegate bool EnumProc(nint window, nint parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, nint parameter);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(nint window, StringBuilder text, int length);
    [DllImport("user32.dll")] static extern bool PostMessage(nint window, uint message, nint w, nint l);
}
