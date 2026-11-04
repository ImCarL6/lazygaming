using System.Drawing;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LazyGaming;

// Somente preferências de vídeo do formato serializado do Balatro. Nunca executa Lua,
// altera o executável ou toca nos arquivos de campanha/perfil.
public static class BalatroDisplayProfile
{
    const string Journal = "balatro-display-before.json";
    static readonly string[] Paths = ["WINDOW.screenmode", "WINDOW.selected_display", "screen_res", "QUEUED_CHANGE.screenres", "QUEUED_CHANGE.selected_display", "QUEUED_CHANGE.screenmode"];
    public sealed record Backup(string File, Dictionary<string, string?> Values);
    sealed record Field(string Key, int Start, int Value, int End, int Next);
    static List<Field> Fields(string table) {
        if (!table.StartsWith('{') || !table.EndsWith('}')) throw new InvalidDataException("Formato de preferências desconhecido.");
        var fields = new List<Field>(); int at = 1;
        while (at < table.Length - 1) {
            while (at < table.Length - 1 && char.IsWhiteSpace(table[at])) at++;
            if (at == table.Length - 1) break;
            var match = Regex.Match(table[at..], "^\\[\"(?<key>[A-Za-z_][A-Za-z_0-9]*)\"\\]\\s*=\\s*");
            if (!match.Success) throw new InvalidDataException("Campo de preferências desconhecido.");
            string key = match.Groups["key"].Value;
            if (fields.Any(f => f.Key == key)) throw new InvalidDataException("Campo de preferências duplicado.");
            int value = at + match.Length, end = value, depth = 0; char quote = '\0';
            for (; end < table.Length; end++) {
                char c = table[end];
                if (quote != '\0') { if (c == '\\') end++; else if (c == quote) quote = '\0'; continue; }
                if (c is '\"' or '\'') quote = c;
                else if (c == '{') depth++;
                else if (c == '}' && depth > 0) depth--;
                else if (depth == 0 && c is ',' or '}') break;
            }
            if (quote != '\0' || depth != 0 || end >= table.Length) throw new InvalidDataException("Preferências incompletas.");
            int next = table[end] == ',' ? end + 1 : end;
            fields.Add(new(key, at, value, end, next)); at = next;
        }
        return fields;
    }
    static string? Read(string table, string path) {
        var parts = path.Split('.', 2); var field = Fields(table).SingleOrDefault(f => f.Key == parts[0]);
        if (field == null) return null;
        var value = table[field.Value..field.End].Trim();
        return parts.Length == 1 ? value : Read(value, parts[1]);
    }
    static string Set(string table, string path, string? value) {
        var parts = path.Split('.', 2); var fields = Fields(table); var field = fields.SingleOrDefault(f => f.Key == parts[0]);
        if (parts.Length > 1) {
            var child = field == null ? "{}" : table[field.Value..field.End].Trim();
            value = Set(child, parts[1], value);
        }
        if (field != null) return value == null ? table.Remove(field.Start, field.Next - field.Start) : table[..field.Value] + value + table[field.End..];
        if (value == null) return table;
        var prefix = table[..^1].TrimEnd();
        return prefix + (fields.Count > 0 && !prefix.EndsWith(',') ? "," : "") + "[\"" + parts[0] + "\"]=" + value + ",}";
    }
    static string Table(string text) {
        if (!text.StartsWith("return ", StringComparison.Ordinal)) throw new InvalidDataException("Preferências não reconhecidas.");
        var table = text[7..].Trim(); _ = Fields(table); return table;
    }
    public static string? Configure(string text, int monitor, Rectangle bounds) {
        if (monitor < 1 || bounds.Width < 1 || bounds.Height < 1) return null;
        try {
            var table = Table(text);
            if (Read(table, "WINDOW.screenmode") is not ("\"Windowed\"" or "\"Borderless\"" or "\"Fullscreen\"") ||
                !int.TryParse(Read(table, "WINDOW.selected_display"), out _) || Read(table, "QUEUED_CHANGE") is not string queue || !queue.StartsWith('{')) return null;
            // Fullscreen usa a resolução explícita; Borderless do jogo exibe o maior modo
            // anunciado pelo monitor mesmo quando o desktop está em outra resolução.
            var resolution = "{[\"w\"]=" + bounds.Width + ",[\"h\"]=" + bounds.Height + ",}";
            foreach (var path in Paths) table = Set(table, path, path.EndsWith("screenmode") ? "\"Fullscreen\"" : path.EndsWith("selected_display") ? monitor.ToString() : resolution);
            return "return " + table;
        } catch (InvalidDataException) { return null; }
    }
    public static string Decode(byte[] bytes) {
        if (bytes.Length > 1_000_000) throw new InvalidDataException("Preferências grandes demais.");
        using var input = new MemoryStream(bytes); using var zip = new DeflateStream(input, CompressionMode.Decompress); using var output = new MemoryStream();
        var buffer = new byte[4096]; int count;
        while ((count = zip.Read(buffer)) > 0) { if (output.Length + count > 1_000_000) throw new InvalidDataException("Preferências grandes demais."); output.Write(buffer, 0, count); }
        return new UTF8Encoding(false, true).GetString(output.ToArray());
    }
    public static byte[] Encode(string text) {
        using var output = new MemoryStream();
        using (var zip = new DeflateStream(output, CompressionLevel.Optimal, true)) zip.Write(Encoding.UTF8.GetBytes(text));
        return output.ToArray();
    }
    static void Write(string path, string text) { File.WriteAllBytes(path + ".lazygaming.tmp", Encode(text)); File.Move(path + ".lazygaming.tmp", path, true); }
    public static void Prepare(Game game, string directory, Func<LaunchDisplay.Target> target, string? settings = null) {
        if (!Path.GetFileName(game.ProcessPath).Equals("Balatro.exe", StringComparison.OrdinalIgnoreCase) || game.ProcessArgument != null ||
            !File.Exists(Path.Combine(Path.GetDirectoryName(game.ProcessPath)!, "love.dll"))) return;
        var path = settings ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Balatro", "settings.jkr");
        if (!File.Exists(path)) return;
        string original;
        try { original = Decode(File.ReadAllBytes(path)); } catch (Exception e) when (e is InvalidDataException or DecoderFallbackException) { return; }
        var display = target(); var configured = Configure(original, display.Monitor, display.Bounds);
        if (configured == null || configured == original) return;
        var journal = Path.Combine(directory, Journal);
        if (!File.Exists(journal)) {
            Directory.CreateDirectory(directory);
            DurableJson.Write(journal, new Backup(path, Paths.ToDictionary(p => p, p => Read(Table(original), p))));
        }
        Write(path, configured);
    }
    public static void Restore(string directory) {
        var journal = Path.Combine(directory, Journal); if (!File.Exists(journal)) return;
        var backup = JsonSerializer.Deserialize<Backup>(File.ReadAllText(journal))!;
        if (backup.Values.Count != Paths.Length || Paths.Any(p => !backup.Values.ContainsKey(p))) throw new InvalidDataException("Reserva de vídeo inválida.");
        if (!File.Exists(backup.File)) throw new InvalidOperationException("As preferências de vídeo do Balatro não estão disponíveis para restauração.");
        var table = Table(Decode(File.ReadAllBytes(backup.File)));
        foreach (var pair in backup.Values) table = Set(table, pair.Key, pair.Value);
        Write(backup.File, "return " + table); File.Delete(journal);
    }
}
