using System.Text.RegularExpressions;

namespace LazyGaming;

public sealed record ControllerDetection(string? Kind, string Name, bool Connected, bool Touchpad, bool Microphone, string Note);
public sealed record InitialPcSetup(string DisplayPath, string ControllerSource = "auto", string? AudioDeviceId = null, bool OnlyGameDisplay = false,
    ShortcutBinding? Launcher = null, ControllerOptions? Controller = null);

public static class PcSetup
{
    public static ControllerOptions Defaults(ControllerDetection controller, string source = "auto") => controller is { Kind:"dualsense", Microphone:true }
        ? new(Launcher:new(PadButtons.Microphone,"double-tap"),Mouse:new(PadButtons.Microphone,"hold",1500))
        : new(SwapConfirmBack:source == "switch-pro" || source == "auto" && controller.Kind == "switch-pro");
    public static ControllerOptions Effective(PcPreferences preferences,ControllerDetection controller) {
        var defaults=Defaults(controller,preferences.ControllerSource);
        var options=preferences.Controller ?? defaults;
        options=options with{Launcher=options.Launcher ?? defaults.Launcher,Mouse=options.Mouse ?? defaults.Mouse};
        // Keep saved microphone shortcuts, but use available buttons in basic Bluetooth.
        if(controller is { Kind:"dualsense", Connected:true, Microphone:false }) {
            if((options.LauncherBinding.Buttons & PadButtons.Microphone)!=0)options=options with{Launcher=defaults.LauncherBinding};
            if((options.MouseBinding.Buttons & PadButtons.Microphone)!=0)options=options with{Mouse=defaults.MouseBinding};
        }
        return options with{SwapConfirmBack=preferences.ConfirmBackOverride ?? defaults.SwapConfirmBack};
    }
    public static ControllerDetection Controller(string? adapter, bool touch = false, bool mic = false, string? brand = null) => adapter switch {
        "dualsense" => new(adapter,"DualSense",true,touch,mic, touch && mic
            ? "Touchpad, clique e atalhos do microfone disponíveis automaticamente."
            : "Nesta conexão, use Create + Options para abrir. Microfone e movimento pelo touchpad precisam de USB ou Bluetooth completo."),
        "xinput" => new(adapter,brand == "8BitDo" ? "8BitDo" : brand == "Xbox" ? "Controle Xbox" : "Xbox / controle compatível",true,false,false,"Navegação e mouse pelo analógico disponíveis automaticamente."),
        "switch-pro" => new(adapter,"Switch Pro",true,false,false,"Navegação e analógico disponíveis. Suporte experimental."),
        _ => new(null,"Nenhum controle detectado ainda",false,false,false,"Conecte o controle por USB ou Bluetooth. Você também pode escolher o tipo e conectá-lo depois.")
    };
    public static bool IsSamsung(Displays.Screen screen) => screen.Available &&
        (screen.Name.Contains("Samsung",StringComparison.OrdinalIgnoreCase) ||
        Regex.IsMatch(screen.DevicePath,@"DISPLAY[#\\]SAM[0-9A-F]{4}(?:[#\\]|$)",RegexOptions.IgnoreCase));
    // Qualquer tela Samsung conectada (ativa ou não) é só um gatilho: marca/EDID não prova TV Tizen, IP ou acesso pela rede.
    public static string[] SamsungScreens(IEnumerable<Displays.Screen> screens) => screens.Where(IsSamsung).Select(s=>s.Name).Distinct().ToArray();
    public static bool SuggestSamsung(IEnumerable<Displays.Screen> screens, PcPreferences preferences, bool paired) =>
        SamsungScreens(screens).Length > 0 && !preferences.SamsungHintDismissed && !preferences.TvIntegrationEnabled && !paired;
    // Última etapa opcional do primeiro uso. Com vínculo existente, mostra o estado em vez de pedir instalação/pareamento.
    public static object SamsungOffer(IEnumerable<Displays.Screen> screens, PcPreferences preferences, bool paired, bool lanEnabled, string[]? addresses = null) {
        var names = SamsungScreens(screens);
        return new { show = names.Length > 0 && (paired || !preferences.SamsungHintDismissed), screens = names, linked = paired,
            integrationEnabled = preferences.TvIntegrationEnabled, lanEnabled, dismissed = preferences.SamsungHintDismissed, addresses = addresses ?? [] };
    }
    public static PcPreferences Complete(PcPreferences current, InitialPcSetup setup, IEnumerable<Displays.Screen> screens, IEnumerable<AudioDevice> audio) {
        if (string.IsNullOrWhiteSpace(setup.DisplayPath) || !screens.Any(s=>s.Available && s.DevicePath==setup.DisplayPath))
            throw new InvalidOperationException("Escolha a tela em que você vai jogar.");
        if (!string.IsNullOrEmpty(setup.AudioDeviceId) && setup.AudioDeviceId != "keep" && !audio.Any(d=>d.Id==setup.AudioDeviceId))
            throw new InvalidOperationException("Escolha uma saída de áudio disponível.");
        var next = current with { DisplayPath=setup.DisplayPath, ControllerSource=setup.ControllerSource, AudioDeviceId=setup.AudioDeviceId, SetupCompleted=true,
            ConfirmBackOverride=setup.ControllerSource==current.ControllerSource?current.ConfirmBackOverride:null,
            Controller = setup.Controller ?? current.Controls with { Launcher = setup.Launcher ?? current.Controls.Launcher } };
        next.Validate(); return next;
    }
}
