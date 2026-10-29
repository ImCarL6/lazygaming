namespace LazyGaming;

// Consulta sob demanda: jamais muda displays para descobrir disponibilidade.
public sealed record CableAvailability(bool? Available, bool Active, string? Note);
public static class Presentation
{
    public static CableAvailability Cable(IEnumerable<Displays.Screen> screens)
    {
        var matches = screens.Where(s => s.Technology == 5 && s.Name == "SAMSUNG" && s.Width == 3840 && s.Height == 2160 && s.Available).ToArray();
        return matches.Length switch {
            1 => new(true, matches[0].Active, null),
            0 => new(false, false, "A TV não foi identificada no HDMI do PC."),
            _ => new(null, false, "Há mais de uma TV compatível. Confira o destino antes de jogar.")
        };
    }
    public static CableAvailability InspectCable()
    {
        try { return Cable(Displays.List()); }
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
