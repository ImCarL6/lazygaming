using System.Text.Json;
namespace LazyGaming;

public sealed record StreamPreferences(string Resolution = "auto", int Fps = 60, int Bitrate = 0, string Codec = "HEVC", bool Adaptive = true);
public sealed class StreamControls(string directory)
{
    readonly object gate = new();
    readonly string file = Path.Combine(directory, "stream-preferences.json");
    StreamPreferences settings = Load(Path.Combine(directory, "stream-preferences.json"));
    long revision = File.Exists(Path.Combine(directory, "stream-preferences.json")) ? 1 : 0, sequence;
    object? command;
    bool active; string profile = "", notice = ""; DateTime seen;
    public bool Playing { get { lock (gate) return active && DateTime.UtcNow - seen < TimeSpan.FromSeconds(15); } }
    public bool Online { get { lock (gate) return DateTime.UtcNow - seen < TimeSpan.FromSeconds(15); } }
    static StreamPreferences Load(string path) {
        try { var value = JsonSerializer.Deserialize<StreamPreferences>(File.ReadAllText(path)); Validate(value!); return value!; }
        catch { return new(); }
    }
    public static void Validate(StreamPreferences value) {
        if (value == null || !new[] { "auto", "1920x1080", "2560x1440", "3840x2160" }.Contains(value.Resolution) || value.Fps is not (30 or 60) || value.Bitrate != 0 && value.Bitrate is < 5 or > 80 || value.Codec is not ("H264" or "HEVC"))
            throw new InvalidOperationException("Qualidade de transmissão inválida.");
    }
    public object Snapshot() { lock (gate) return new { revision, settings, command, notice, active = active && DateTime.UtcNow - seen < TimeSpan.FromSeconds(15), profile, online = DateTime.UtcNow - seen < TimeSpan.FromSeconds(15) }; }
    public object Change(StreamPreferences value, string? action) { lock (gate) {
        Validate(value);
        if (action != null && action is not ("apply" or "start" or "stop")) throw new InvalidOperationException("Ação de transmissão inválida.");
        if (command != null) throw new InvalidOperationException("Aguarde a TV concluir o ajuste anterior.");
        File.WriteAllText(file, JsonSerializer.Serialize(value)); settings = value; revision++;
        if (action != null) command = new { id = ++sequence, action };
        return Snapshot();
    } }
    public object Report(long completed, bool playing, string label, string message = "") { lock (gate) {
        if (completed == sequence) command = null;
        active = playing; profile = label.Length <= 160 ? label : ""; notice = message.Length <= 200 ? message : ""; seen = DateTime.UtcNow; return Snapshot();
    } }
}
