using System.Text;
using System.Text.RegularExpressions;

namespace LazyGaming;

public static class GameDisplaySettings
{
    public static bool CanUseUnityDisplayArguments(Game game) {
        var folder = Path.GetDirectoryName(game.ProcessPath)!;
        return game.ProcessArgument == null && (game.Steam || string.Equals(game.Launch, game.ProcessPath, StringComparison.OrdinalIgnoreCase))
            && File.Exists(Path.Combine(folder, "UnityPlayer.dll"))
            && Directory.Exists(Path.Combine(folder, Path.GetFileNameWithoutExtension(game.ProcessPath) + "_Data"));
    }
    public static string[] UnityDisplayArguments(int monitor, int width, int height) {
        if (monitor < 1 || width < 1 || height < 1) throw new InvalidOperationException("Destino de vídeo indisponível.");
        return ["-monitor", monitor.ToString(), "-screen-fullscreen", "0", "-popupwindow", "-screen-width", width.ToString(), "-screen-height", height.ToString()];
    }
    public static string[] RiskLaunchArguments(int monitor, int width, int height) {
        if (monitor < 1 || width < 1 || height < 1) throw new InvalidOperationException("Destino de vídeo indisponível.");
        return ["-applaunch", "632360", "-monitor", monitor.ToString(), "-screen-fullscreen", "0", "-popupwindow", "-screen-width", width.ToString(), "-screen-height", height.ToString()];
    }
    static readonly string[] Keys = ["DeviceInstanceName", "DisplayName", "NormalWindowResolution", "WindowMode", "FullScreenMode"];
    const string WildsConfig = @"C:\Program Files (x86)\Steam\steamapps\common\MonsterHunterWilds\config.ini";
    // Somente parâmetros de apresentação do Wilds; preserva as opções gráficas/saves.
    public static void Prepare(Game game, string directory)
    {
        WindowedOptionsProfile.Prepare(game, directory, Displays.TvBounds);
        if (game.Id != "wilds") return;
        var display = Displays.GameDisplay();
        var source = Displays.StreamSource(true);
        PrepareReframework(game, directory);
        var path = Path.Combine(Path.GetDirectoryName(game.ProcessPath)!, "config.ini");
        if (!File.Exists(path)) return;
        var original = File.ReadAllText(path);
        var backup = Path.Combine(directory, "wilds-display-before.ini");
        if (!File.Exists(backup)) File.WriteAllText(backup, original, new UTF8Encoding(false));
        var updated = ConfigureWilds(original, display, source.Width, source.Height, Displays.Tv().Name);
        File.WriteAllText(path, updated, new UTF8Encoding(false));
    }
    public static string ConfigureWilds(string original, string display, uint width, uint height, string displayName = "SAMSUNG")
    {
        if (string.IsNullOrWhiteSpace(display) || width == 0 || height == 0)
            throw new InvalidOperationException("O destino de vídeo da TV não está disponível.");
        var values = new Dictionary<string, string>
        {
            ["DeviceInstanceName"] = display, ["DisplayName"] = displayName,
            ["NormalWindowResolution"] = $"({width},{height})", ["WindowMode"] = "Borderless", ["FullScreenMode"] = "false"
        };
        return Regex.Replace(original, @"(?m)^(DeviceInstanceName|DisplayName|NormalWindowResolution|WindowMode|FullScreenMode)=([^\r\n]*)",
            m => m.Groups[1].Value + "=" + values[m.Groups[1].Value]);
    }
    public static void PrepareReframework(Game game, string directory)
    {
        if (game.Id != "wilds") return;
        var path = Path.Combine(Path.GetDirectoryName(game.ProcessPath)!, "re2_fw_config.txt");
        if (!File.Exists(path)) return;
        var original = File.ReadAllText(path);
        var updated = CloseReframeworkMenu(original);
        if (updated == original) return;
        var backupDirectory = Path.Combine(directory, "backups"); Directory.CreateDirectory(backupDirectory);
        var backup = Path.Combine(backupDirectory, "reframework-before-lazygaming.txt");
        if (!File.Exists(backup)) File.WriteAllText(backup, original, new UTF8Encoding(false));
        File.WriteAllText(path, updated, new UTF8Encoding(false));
    }
    public static string CloseReframeworkMenu(string config)
    {
        foreach (var entry in new[] { ("REFrameworkConfig_RememberMenuState", "true"), ("REFrameworkConfig_MenuOpen", "false") })
        {
            var pattern = @"(?m)^" + entry.Item1 + @"=[^\r\n]*";
            config = Regex.IsMatch(config, pattern) ? Regex.Replace(config, pattern, entry.Item1 + "=" + entry.Item2)
                : config + Environment.NewLine + entry.Item1 + "=" + entry.Item2 + Environment.NewLine;
        }
        return config;
    }
    public static void Restore(string directory)
    {
        WindowedOptionsProfile.Restore(directory);
        var backup = Path.Combine(directory, "wilds-display-before.ini");
        if (!File.Exists(backup) || !File.Exists(WildsConfig)) return;
        var previous = new Dictionary<(string, string), string>();
        string section = "";
        foreach (var line in File.ReadAllLines(backup))
        {
            if (line.StartsWith('[')) section = line;
            int split = line.IndexOf('=');
            if (split > 0 && Keys.Contains(line[..split])) previous[(section, line[..split])] = line[(split + 1)..];
        }
        var lines = File.ReadAllLines(WildsConfig); section = "";
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith('[')) section = lines[i];
            int split = lines[i].IndexOf('=');
            if (split > 0 && previous.TryGetValue((section, lines[i][..split]), out var value)) lines[i] = lines[i][..(split + 1)] + value;
        }
        File.WriteAllLines(WildsConfig, lines, new UTF8Encoding(false)); File.Delete(backup);
    }
}
