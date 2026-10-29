using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LazyGaming;

// Capacidade por formato reconhecido, não por ID/título. Não generalizar este
// schema a todo SDL: engines escolhem seu próprio formato de preferências.
public static class WindowedOptionsProfile
{
    static readonly string[] Keys = ["DisplayMode", "Resolution", "WindowPosition", "WindowMaximized"];
    const string Journal = "windowed-options-before.json";
    public sealed record Backup(string GamePath, Dictionary<string, string> Values);
    static string Config(string executable) => Path.Combine(Path.GetDirectoryName(executable)!, "options.ini");
    static bool HasSdl(string executable) => new[] { "SDL3.dll", "SDL2.dll" }.Any(name => File.Exists(Path.Combine(Path.GetDirectoryName(executable)!, name)));
    static Dictionary<string, string>? Read(string text) {
        var result = new Dictionary<string, string>();
        foreach (var key in Keys.Append("Monitor")) {
            var matches = Regex.Matches(text, @"(?m)^" + key + @"\s*=\s*([^\r\n]*)");
            if (matches.Count != 1) return null;
            result[key] = matches[0].Groups[1].Value.Trim();
        }
        if (result["DisplayMode"] is not ("Windowed" or "Borderless" or "Fullscreen") ||
            result["WindowMaximized"] is not ("true" or "false") || !int.TryParse(result["Monitor"], out _) ||
            !Regex.IsMatch(result["Resolution"], @"^\d+x\d+$")) return null;
        try {
            using var position = JsonDocument.Parse(result["WindowPosition"]);
            if (position.RootElement.ValueKind != JsonValueKind.Object ||
                !position.RootElement.TryGetProperty("x", out var x) || !x.TryGetInt32(out _) ||
                !position.RootElement.TryGetProperty("y", out var y) || !y.TryGetInt32(out _)) return null;
        } catch (JsonException) { return null; }
        return result;
    }
    static string Set(string text, IReadOnlyDictionary<string, string> values) {
        foreach (var pair in values) text = Regex.Replace(text, @"(?m)^(" + Regex.Escape(pair.Key) + @"\s*=\s*)[^\r\n]*", m => m.Groups[1].Value + pair.Value);
        return text;
    }
    public static string? Configure(string original, Rectangle bounds) {
        if (bounds.Width <= 0 || bounds.Height <= 0 || Read(original) == null) return null;
        return Set(original, new Dictionary<string, string> {
            ["DisplayMode"] = "Windowed", ["Resolution"] = $"{bounds.Width}x{bounds.Height}",
            ["WindowPosition"] = JsonSerializer.Serialize(new { x = bounds.X, y = bounds.Y }), ["WindowMaximized"] = "false"
        });
    }
    public static void Prepare(Game game, string directory, Func<Rectangle> target) {
        if (!HasSdl(game.ProcessPath) || !File.Exists(Config(game.ProcessPath))) return;
        var path = Config(game.ProcessPath); var original = File.ReadAllText(path);
        var values = Read(original); if (values == null) return;
        var configured = Configure(original, target()); if (configured == null || configured == original) return;
        var journal = Path.Combine(directory, Journal);
        var backups = File.Exists(journal) ? JsonSerializer.Deserialize<List<Backup>>(File.ReadAllText(journal))! : [];
        if (!backups.Any(b => string.Equals(b.GamePath, game.ProcessPath, StringComparison.OrdinalIgnoreCase))) {
            backups.Add(new(game.ProcessPath, Keys.ToDictionary(k => k, k => values[k])));
            File.WriteAllText(journal + ".tmp", JsonSerializer.Serialize(backups)); File.Move(journal + ".tmp", journal, true);
        }
        File.WriteAllText(path, configured, new UTF8Encoding(false));
    }
    public static void Restore(string directory) {
        var journal = Path.Combine(directory, Journal); if (!File.Exists(journal)) return;
        foreach (var backup in JsonSerializer.Deserialize<List<Backup>>(File.ReadAllText(journal))!) {
            if (!HasSdl(backup.GamePath) || !File.Exists(Config(backup.GamePath)) || backup.Values.Count != Keys.Length || backup.Values.Keys.Any(k => !Keys.Contains(k)))
                throw new InvalidOperationException("Não foi possível recuperar a apresentação anterior de um jogo.");
            var path = Config(backup.GamePath);
            File.WriteAllText(path, Set(File.ReadAllText(path), backup.Values), new UTF8Encoding(false));
        }
        File.Delete(journal);
    }
}
