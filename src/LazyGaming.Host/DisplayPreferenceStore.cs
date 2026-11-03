using System.Text.Json;

namespace LazyGaming;

// A preferência do usuário não é o perfil temporário de recuperação da sessão.
public sealed class DisplayPreferenceStore(string directory)
{
    readonly string file = Path.Combine(directory, "display-profile.json");
    public bool Load() => Read(file) ?? Read(file + ".bak") ?? true;
    static bool? Read(string path)
    {
        try {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            return json.RootElement.ValueKind == JsonValueKind.Object &&
                json.RootElement.TryGetProperty("MonitorsEnabled", out var enabled) &&
                enabled.ValueKind is JsonValueKind.True or JsonValueKind.False ? enabled.GetBoolean() : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
    public void Save(bool enabled)
    {
        var value = new TvSession.DisplayPreference(enabled);
        // A cópia reserva também contém a última escolha confirmada. Cada destino
        // só é substituído depois de gravar e descarregar o arquivo completo.
        DurableJson.Write(file, value);
        DurableJson.Write(file + ".bak", value);
    }
}

internal static class DurableJson
{
    public static void Write<T>(string file, T value, JsonSerializerOptions? options = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.WriteThrough)) {
                JsonSerializer.Serialize(stream, value, options);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(file)) File.Replace(temporary, file, null);
            else File.Move(temporary, file);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
