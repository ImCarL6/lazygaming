using System.Text.Json;

namespace LazyGaming;

public sealed record PcPreferences(string? DisplayPath = null, bool RestoreTvState = false,
    string ControllerSource = "auto", ControllerOptions? Controller = null, string? AudioDeviceId = null, bool Enabled = true,
    bool TvIntegrationEnabled = false, bool SetupCompleted = false, bool SamsungHintDismissed = false, bool? ConfirmBackOverride = null,
    bool DualSenseBluetoothFeatures = true)
{
    public ControllerOptions Controls => Controller ?? new();
    public void Validate() {
        if (ControllerSource is not ("auto" or "dualsense" or "xinput" or "switch-pro") ||
            DisplayPath is { Length: > 512 } || AudioDeviceId is { Length: > 512 })
            throw new InvalidOperationException("Configuração do PC inválida.");
        Controls.Validate();
    }
}

public sealed class PcPreferenceStore
{
    readonly string file;
    readonly object gate = new();
    PcPreferences current;
    public PcPreferences Current => Volatile.Read(ref current);
    public PcPreferenceStore(string directory) {
        file = Path.Combine(directory, "pc-settings.json");
        // Existing installations keep the previous exact restoration behavior.
        bool existing = File.Exists(Path.Combine(directory, "display-profile.json")) || File.Exists(Path.Combine(directory, "pairing.json"));
        current = Read(file) ?? Read(file + ".bak") ?? new(RestoreTvState: existing, SetupCompleted: existing);
    }
    static PcPreferences? Read(string path) {
        try {
            var text = File.ReadAllText(path);
            var p = JsonSerializer.Deserialize<PcPreferences>(text, LibraryJson.Options); p?.Validate();
            using var json = JsonDocument.Parse(text);
            // Upgrades of configured installations do not reopen first-run setup.
            return p != null && !json.RootElement.EnumerateObject().Any(v => v.Name.Equals("SetupCompleted",StringComparison.OrdinalIgnoreCase))
                ? p with { SetupCompleted = true } : p;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return null; }
    }
    public void Save(PcPreferences value) {
        lock (gate) SaveCore(value);
    }
    public void SetEnabled(bool enabled) { lock (gate) SaveCore(Current with { Enabled = enabled }); }
    void SaveCore(PcPreferences value) {
        value.Validate();
        using var held = new LibraryFileLock(file);
        LibraryJson.Write(file, value);
        Volatile.Write(ref current, value);
    }
}
