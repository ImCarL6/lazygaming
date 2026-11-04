using System.Text.Json;

namespace LazyGaming;

public interface IGameEnvironment
{
    bool Active { get; }
    object Status { get; }
    void Prepare(string mode = "cable");
    void Tick();
    void Restore();
    void ManualAudio(string id);
    DisplayControls DisplayControls { get; }
    void SetMonitors(bool enabled);
    void ReconfirmDesktop() { }
}
public sealed record DisplayControls(bool SessionActive, bool MonitorsEnabled, bool Available = true, string? Note = null);
public interface ISessionPlatform
{
    void ValidateTv();
    Displays.Snapshot CaptureDisplays();
    void PrepareTv();
    bool TvReady();
    bool DesktopTvReady() => TvReady();
    void PrepareDesktop(Displays.Snapshot original) => ConfigureMonitors(original, true);
    void ConfigureMonitors(Displays.Snapshot original, bool enabled);
    void RestoreDisplays(Displays.Snapshot snapshot);
    void RestoreSessionDisplays(Displays.Snapshot snapshot) => RestoreDisplays(snapshot);
    void ReconfirmDesktop() { }
    List<AudioDevice> ListAudio();
    bool KeepAudio => false;
    AudioDevice? SessionAudio(List<AudioDevice> devices) => devices.SingleOrDefault(d => d.Name.StartsWith("SAMSUNG (NVIDIA", StringComparison.OrdinalIgnoreCase));
    string[] CaptureAudio();
    void SelectAudio(string id);
    void RestoreAudio(string[] ids);
}
public sealed class NativeSessionPlatform(Func<nint>? gameWindow = null, Action? alignLauncher = null) : ISessionPlatform
{
    public bool KeepAudio => Displays.Preferences().AudioDeviceId == "keep";
    public AudioDevice? SessionAudio(List<AudioDevice> devices) {
        var id = Displays.Preferences().AudioDeviceId;
        if (!string.IsNullOrEmpty(id)) return devices.SingleOrDefault(d => d.Id == id);
        var name = Displays.Tv().Name;
        var matches = devices.Where(d => d.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    DesktopWindows? initialWindows;
    public void ValidateTv() => _ = Displays.Tv();
    public Displays.Snapshot CaptureDisplays() => Displays.Capture();
    public void PrepareTv() => Displays.PrepareTv();
    public bool TvReady() => Displays.TvReady();
    public bool DesktopTvReady() => Displays.TvReady();
    public void PrepareDesktop(Displays.Snapshot original) {
        using var focus = WindowFocus.KeepAcrossDisplayChange();
        initialWindows ??= DesktopWindows.Capture(gameWindow?.Invoke() ?? 0);
        Displays.PrepareDesktop(original, alignLauncher);
    }
    public void ReconfirmDesktop() => initialWindows?.Restore();
    public void ConfigureMonitors(Displays.Snapshot original, bool enabled) {
        using var focus = WindowFocus.KeepAcrossDisplayChange();
        using var placement = WindowFocus.KeepGamePlacement(gameWindow);
        Displays.ConfigureMonitors(original, enabled, () => { placement.Restore(); alignLauncher?.Invoke(); });
    }
    public void RestoreDisplays(Displays.Snapshot snapshot) {
        using var focus = WindowFocus.KeepAcrossDisplayChange();
        using var windows = DesktopWindows.Capture();
        Displays.Restore(snapshot);
        windows.Restore();
        initialWindows = null;
    }
    public void RestoreSessionDisplays(Displays.Snapshot snapshot) => RestoreDisplays(Displays.RestorePlan(snapshot, Displays.Preferences().RestoreTvState));
    public List<AudioDevice> ListAudio() => Audio.List();
    public string[] CaptureAudio() => Audio.CaptureDefaults();
    public void SelectAudio(string id) => Audio.Select(id);
    public void RestoreAudio(string[] ids) => Audio.RestoreDefaults(ids);
}

public sealed class TvSession : IGameEnvironment
{
    readonly string file;
    readonly string directory;
    readonly ISessionPlatform platform;
    readonly DisplayPreferenceStore preferences;
    Saved? saved;
    bool manualAudio, restoreFailed, monitorsEnabled;
    bool preferredMonitorsEnabled;
    string? note;
    string? audioId;
    readonly object gate = new();
    public bool Active => saved != null;
    bool Ready() => monitorsEnabled ? platform.DesktopTvReady() : platform.TvReady();
    public DisplayControls DisplayControls { get { lock (gate) {
        bool available = true;
        if (Active) { try { available = Ready(); } catch { available = false; } }
        return new(Active, monitorsEnabled, available, available ? null : "A TV perdeu o destino de vídeo. Use Continuar na TV para recuperar somente a TV antes de alterar os monitores.");
    } } }
    public object Status { get { lock (gate) return new { active = Active, audioPending = Active && !saved!.Stream && !manualAudio && !platform.KeepAudio && audioId == null, note, restoreFailed }; } }
    public TvSession(string directory, ISessionPlatform? platform = null)
    {
        this.directory = directory;
        this.platform = platform ?? new NativeSessionPlatform();
        file = Path.Combine(directory, "session.json");
        preferences = new(directory);
        monitorsEnabled = preferredMonitorsEnabled = preferences.Load();
        if (File.Exists(file)) {
            saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(file), JsonOptions);
            monitorsEnabled = saved?.MonitorsEnabled ?? monitorsEnabled;
            manualAudio = saved?.ManualAudio ?? false;
        }
    }
    static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true };
    public void Prepare(string mode = "cable")
    {
        Presentation.Validate(mode, true);
        lock (gate)
        {
            // Recuperação explícita via Continuar: não repetir automaticamente o perfil que perdeu a TV.
            if (saved != null && !Ready() && monitorsEnabled) {
                monitorsEnabled = false; saved = saved with { MonitorsEnabled = false };
                DurableJson.Write(file, saved, JsonOptions);
            }
            if (saved == null)
            {
                platform.ValidateTv(); // Identificar a TV antes de alterar qualquer coisa.
                saved = new(platform.CaptureDisplays(), platform.CaptureAudio(), monitorsEnabled, Stream: mode == "stream");
                DurableJson.Write(file, saved, JsonOptions);
                manualAudio = false; audioId = null;
            }
            restoreFailed = false;
            try {
                if (monitorsEnabled) platform.PrepareDesktop(saved.Displays);
                else {
                    if (!platform.TvReady()) platform.PrepareTv();
                    platform.ConfigureMonitors(saved.Displays, false);
                }
            }
            catch { RestoreCore(true); throw; }
            Tick();
        }
    }
    public void Tick()
    {
        lock (gate)
        {
            if (!Active || saved!.Stream || manualAudio || platform.KeepAudio || restoreFailed) return;
            try
            {
                var devices = platform.ListAudio();
                var target = platform.SessionAudio(devices);
                if (target == null) { audioId = null; note = "Escolha a saída de áudio na Central do PC ou aguarde o HDMI…"; RuntimeDiagnostics.State("audio", note); return; }
                if (!target.Current) platform.SelectAudio(target.Id);
                audioId = target.Id; note = null; RuntimeDiagnostics.State("audio", null);
            }
            catch (Exception e) { RuntimeDiagnostics.Report("audio", "prepare-failed", e); audioId = null; note = "Aguardando o áudio da TV. O auxiliar continuará tentando."; }
        }
    }
    public void ReconfirmDesktop() {
        lock (gate) { if (Active && monitorsEnabled && !restoreFailed && Ready()) platform.ReconfirmDesktop(); }
    }
    public void ManualAudio(string id)
    {
        lock (gate) {
            platform.SelectAudio(id); manualAudio = true; note = null; audioId = null;
            if (saved != null) { saved = saved with { ManualAudio = true }; DurableJson.Write(file, saved, JsonOptions); }
        }
    }
    public void SetMonitors(bool enabled)
    {
        lock (gate) {
            if (saved != null && (restoreFailed || !Ready())) throw new InvalidOperationException("A TV não está no destino esperado. Use Continuar na TV antes de alterar os monitores.");
            if (saved != null && monitorsEnabled != enabled) {
                if (enabled) platform.PrepareDesktop(saved.Displays);
                else {
                    platform.PrepareTv();
                    platform.ConfigureMonitors(saved.Displays, false);
                }
                saved = saved with { MonitorsEnabled = enabled };
                DurableJson.Write(file, saved, JsonOptions);
            }
            // Sem sessão: guardar escolha, sem mudar telas ou áudio do usuário.
            preferences.Save(enabled);
            monitorsEnabled = preferredMonitorsEnabled = enabled;
        }
    }
    public void Restore() => RestoreCore(false);
    void RestoreCore(bool exact)
    {
        lock (gate)
        {
            if (saved == null || restoreFailed) return;
            try
            {
                if (exact) platform.RestoreDisplays(saved.Displays);
                else platform.RestoreSessionDisplays(saved.Displays);
                platform.RestoreAudio(saved.Audio);
                GameDisplaySettings.Restore(directory);
                saved = null; manualAudio = false; audioId = null; note = null;
                File.Delete(file);
                monitorsEnabled = preferredMonitorsEnabled;
            }
            catch (Exception e) { RuntimeDiagnostics.Report("session", "restore-failed", e); restoreFailed = true; note = "Não foi possível restaurar toda a configuração anterior. Reinicie o auxiliar após conferir os cabos."; }
        }
    }
    public sealed record Saved(Displays.Snapshot Displays, string[] Audio, bool MonitorsEnabled = false, bool ManualAudio = false, bool Stream = false);
    public sealed record DisplayPreference(bool MonitorsEnabled);
}

public sealed class SimulatedEnvironment : IGameEnvironment
{
    public bool Active { get; private set; }
    public int Preparations, Restorations;
    public int DesktopReconfirmations;
    public void ReconfirmDesktop() => DesktopReconfirmations++;
    public object Status => new { active = Active, Preparations, Restorations, mode = Mode };
    public string Mode { get; private set; } = "cable";
    public void Prepare(string mode = "cable") { if (!Active) Preparations++; Active = true; Mode = mode; }
    public void Tick() { }
    public void Restore() { if (Active) Restorations++; Active = false; }
    public void ManualAudio(string id) { }
    bool monitorsEnabled = true;
    public DisplayControls DisplayControls => new(Active, monitorsEnabled);
    public void SetMonitors(bool enabled) => monitorsEnabled = enabled;
}
