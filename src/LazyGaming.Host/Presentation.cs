namespace LazyGaming;

// Consulta sob demanda: jamais muda displays para descobrir disponibilidade.
public sealed record CableAvailability(bool? Available, bool Active, string? Note);
public static class Presentation
{
    public static CableAvailability Cable(IEnumerable<Displays.Screen> screens, string? selected = null)
    {
        var available = screens.Where(s => s.Available).ToArray();
        var target = Displays.SelectTarget(available, selected);
        if (target != null) return new(true, target.Active, null);
        return new(available.Length == 0 || !string.IsNullOrEmpty(selected) ? false : null, false, "Escolha a tela de jogo na Central do PC e confira a conexão.");
    }
    public static CableAvailability InspectCable()
    {
        try { return Cable(Displays.List(), Displays.Preferences().DisplayPath); }
        catch { return new(null, false, "Não foi possível conferir o HDMI. Confira o cabo antes de continuar."); }
    }
    public static void Validate(string mode, bool streamReady = false)
    {
        if (mode == "stream") {
            if (!streamReady) throw new InvalidOperationException("Stream ainda não está configurado. Sunshine e Moonlight precisam ser preparados e pareados.");
            return;
        }
        if (mode != "cable") throw new InvalidOperationException("Escolha Cabo ou Stream.");
    }
}
