using LazyGaming;

static class PcSetupTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run() {
        string root = Path.Combine(Path.GetTempPath(), "lg-pc-setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var ip in new[]{"10.1.2.3","172.16.1.2","172.31.255.1","192.168.1.2"}) Check(PrivateNetwork.IsLocal(System.Net.IPAddress.Parse(ip)),"LAN privada aceita");
        foreach (var ip in new[]{"127.0.0.1","172.32.1.2","8.8.8.8","169.254.1.2","::1"}) Check(!PrivateNetwork.IsLocal(System.Net.IPAddress.Parse(ip)),"não expor em rede pública/loopback IPv6");
        try {
            var store = new PcPreferenceStore(root);
            Check(!store.Current.RestoreTvState,"setup novo mantém tela de jogo disponível");
            Check(!store.Current.SetupCompleted,"primeira instalação solicita configuração básica");
            File.WriteAllText(Path.Combine(root,"display-profile.json"),"{}");
            Check(new PcPreferenceStore(root).Current.RestoreTvState,"setup existente mantém restauração anterior");
            Check(new PcPreferenceStore(root).Current.SetupCompleted,"instalação existente não repete primeiro uso");
            var saved = new PcPreferences("physical-monitor-7", false,"xinput",new(Sensitivity:1700),"audio-example");
            store.Save(saved); Check(new PcPreferenceStore(root).Current == saved,"preferências persistem separadas da biblioteca");
            store.SetEnabled(false);
            Check(!new PcPreferenceStore(root).Current.Enabled && store.Current.DisplayPath == saved.DisplayPath,"pausa persiste sem apagar tela/controle");
            store.SetEnabled(true);
            File.WriteAllText(Path.Combine(root,"pc-settings.json"),"{");
            Check(new PcPreferenceStore(root).Current == saved,"reserva recupera configuração corrompida");
            var monitor = new Displays.Screen("Monitor","one",10,true,true,1920,1080,144);
            var samsung = monitor with { Name="Tela",DevicePath=@"\\?\DISPLAY#SAM735C#instance" };
            Check(PcSetup.IsSamsung(samsung) && !PcSetup.IsSamsung(monitor),"marca por identidade do Windows sem afirmar Tizen");
            Check(PcSetup.SuggestSamsung([samsung],new(),false),"sugestão Samsung opcional");
            Check(!PcSetup.SuggestSamsung([samsung],new(SamsungHintDismissed:true),false) && !PcSetup.SuggestSamsung([samsung],new(),true),"dispensa/vínculo existente não incomodam");
            Check(PcSetup.Controller("dualsense",true,true) is { Touchpad:true, Microphone:true, Connected:true },"capacidades DualSense detectadas");
            Check(PcSetup.Controller("dualsense") is { Touchpad:false, Microphone:false, Connected:true },"básico não inventa capacidades");
            var defaults=PcSetup.Defaults(PcSetup.Controller("dualsense",true,true));defaults.Validate();
            Check(defaults.LauncherBinding is{Buttons:PadButtons.Microphone,Gesture:"double-tap"} && defaults.MouseBinding is{Buttons:PadButtons.Microphone,Gesture:"hold",HoldMs:1500},"DualSense defaults use microphone for menu/mouse");
            Check(PcSetup.Defaults(PcSetup.Controller("dualsense")).LauncherBinding.Buttons!=PadButtons.Microphone,"basic Bluetooth has a usable fallback");
            var micSaved=new PcPreferences(Controller:new(Launcher:new(PadButtons.Microphone,"double-tap"),Mouse:new(PadButtons.Microphone,"hold",1500)));
            var basicEffective=PcSetup.Effective(micSaved,PcSetup.Controller("dualsense"));
            Check(basicEffective.LauncherBinding.Buttons==(PadButtons.Select|PadButtons.Start) && basicEffective.MouseBinding.Buttons==(PadButtons.LeftStick|PadButtons.RightStick),"unavailable mic gets working runtime fallbacks");
            Check(micSaved.Controls.LauncherBinding.Buttons==PadButtons.Microphone && PcSetup.Effective(micSaved,PcSetup.Controller("dualsense",true,true)).LauncherBinding.Buttons==PadButtons.Microphone,"fallback never erases saved mic shortcut and full reports restore it");
            Check(PcSetup.Effective(new(Controller:new(Launcher:new(PadButtons.Microphone,"double-tap"))),PcSetup.Controller("dualsense",true,true)).MouseBinding is{Buttons:PadButtons.Microphone,HoldMs:1500},"unspecified mouse binding follows detected DualSense defaults");
            Check(PcSetup.Effective(new(),PcSetup.Controller("switch-pro")).SwapConfirmBack && PcSetup.Effective(new(ControllerSource:"switch-pro"),PcSetup.Controller(null)).SwapConfirmBack,"Nintendo detected or selected uses A confirm automatically");
            Check(!PcSetup.Effective(new(Controller:new(SwapConfirmBack:true)),PcSetup.Controller("dualsense",true,true)).SwapConfirmBack,"non-Nintendo default confirms with lower button");
            Check(!PcSetup.Effective(new(ConfirmBackOverride:false),PcSetup.Controller("switch-pro")).SwapConfirmBack,"advanced override is explicit");
            Check(DesktopReopen.Command(@"C:\Apps\LazyGaming\LazyGaming.Host.exe")=="\"C:\\Apps\\LazyGaming\\LazyGaming.Host.exe\" --open-hub","reopen protocol never passes URI input or arbitrary commands");
            var configured = PcSetup.Complete(new(),new("one","auto","keep"),[monitor],[]);
            Check(configured.SetupCompleted && configured.Controls.LegacyMicrophone,"configuração habilita recursos automáticos e grava destino");
            var shortcut = new ShortcutBinding(PadButtons.Select,"double-tap");
            Check(PcSetup.Complete(new(),new("one",Launcher:shortcut),[monitor],[]).Controls.Launcher == shortcut,"primeiro uso grava o atalho escolhido");
            bool refused=false;try{PcSetup.Complete(new(),new("missing"),[monitor],[]);}catch(InvalidOperationException){refused=true;}
            Check(refused,"primeiro uso recusa destino inválido");
            File.WriteAllText(Path.Combine(root,"pc-settings.json"),"{\"ControllerSource\":\"xinput\"}");
            Check(new PcPreferenceStore(root).Current.SetupCompleted,"migração0.18 mantém configuração existente");
            Check(Displays.SelectTarget([monitor]) == monitor && Presentation.Cable([monitor]).Available == true,"monitor único sem HDMI funciona");
            var screens = Enumerable.Range(0,8).Select(i=>monitor with{DevicePath="screen-"+i}).ToArray();
            Check(Displays.SelectTarget(screens) == null,"não adivinhar destino entre N monitores");
            Check(Displays.SelectTarget(screens,"screen-6") == screens[6],"qualquer monitor assume papel de TV");
            Check(Displays.SelectTarget(screens,"missing") == null,"não substituir tela escolhida silenciosamente");
            Check(Presentation.Cable(screens,"screen-6").Available == true,"Cabo aceita monitor escolhido sem HDMI");
            Check(Displays.SelectTarget([monitor,monitor with{DevicePath="hdmi",Technology=5}])!.DevicePath == "hdmi","único HDMI automático");
            Check(Displays.SelectTarget([monitor with{Available=false}]) == null,"desconectada nunca é destino");
        } finally { Directory.Delete(root,true); }
        Console.WriteLine("PASS: 1–N telas, destino físico de qualquer tipo, ambiguidade, defaults/migração e reserva de configuração PC.");
    }
}
