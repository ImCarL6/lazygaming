using LazyGaming;
using System.Buffers.Binary;

static class MultiControllerTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run() {
        var featureRequest=DualSenseBluetooth.FeatureRequest();
        Check(featureRequest.Length==78 && featureRequest[0]==0x31 && featureRequest[2]==0x10 && featureRequest.AsSpan(3,71).IndexOfAnyExcept((byte)0)<0,"feature handshake enables no rumble/LED/audio/trigger flags");
        Check(Convert.ToHexString(featureRequest.AsSpan(74))=="B5011523","feature handshake CRC matches independent zlib fixture");
        var adapter = new XInputAdapter();
        var report = new byte[12];
        Check(adapter.Decode(report) is { Neutral: true, Touch: null, Microphone: null }, "XInput neutro sem inventar toque");
        report[1] = 0x10;
        Check(adapter.Decode(report) is { Buttons: ControllerButtons.Confirm, RawButtons: PadButtons.South, Neutral: false }, "A XInput confirma");
        report[1] = 0x20;
        Check(adapter.Decode(report)!.Buttons == ControllerButtons.Back, "B XInput volta");
        Array.Clear(report); BinaryPrimitives.WriteInt16LittleEndian(report.AsSpan(8), short.MinValue);
        Check(adapter.Decode(report) is { Neutral: false, RightX: -1 }, "limite negativo do analógico não transborda");
        Array.Clear(report); report[2] = 100;
        Check(adapter.Decode(report) is { Neutral: false, RawButtons: PadButtons.LeftTrigger }, "gatilho conta para soltura");
        Check(adapter.Decode(new byte[11]) == null && adapter.Decode(new byte[13]) == null, "layout XInput exato");
        var sw = new SwitchProAdapter(); var pro = new byte[49]; pro[0] = 0x30;
        pro[7] = pro[10] = 8; pro[8] = pro[11] = 128;
        Check(sw.Decode(pro) is { Neutral: true }, "Switch Pro centro nominal"); pro[3] = 4;
        Check(sw.Decode(pro)!.Buttons == ControllerButtons.Confirm, "B inferior do Switch confirma por posição");
        pro[3] = 8; Check(sw.Decode(pro)!.Buttons == ControllerButtons.Back, "A direito volta por posição");
        pro[0] = 0x3f; Check(sw.Decode(pro) == null, "Switch básico não é interpretado como completo");

        var options = new ControllerOptions(LegacyMicrophone: false);
        var neutral = new ControllerState(ControllerButtons.None, true, null, null, false, false, PadButtons.None);
        var processor = new ControllerProcessor();
        var opening = neutral with { Neutral = false, RawButtons = PadButtons.Select | PadButtons.Start };
        processor.Process(opening,0,false,false,options);
        Check(!processor.Process(opening,2000,false,false,options).ToggleLauncher,"conexão segurando não abre");
        processor.Process(neutral,2100,false,false,options); processor.Process(opening,2200,false,false,options);
        Check(processor.Process(opening,3200,false,false,options).ToggleLauncher,"combinação abre após soltar e segurar");
        Check(!processor.Process(opening,4500,false,false,options).ToggleLauncher,"segurar não repete");
        processor.Process(neutral,4600,true,true,options);
        var mouse = neutral with { Neutral = false, RawButtons = PadButtons.LeftStick | PadButtons.RightStick };
        processor.Process(mouse,4700,true,true,options);
        Check(processor.Process(mouse,5700,true,true,options).MouseModeChanged == true,"atalho ativa mouse sem microfone");
        processor.Process(neutral,5800,true,true,options);
        var click = neutral with { Neutral = false, RawButtons = PadButtons.RightShoulder };
        Check(processor.Process(click,5820,true,true,options).Mouse.Single().Kind == "left-click","botão faz clique");
        Check(processor.Process(click,5840,true,true,options).Mouse.Count == 0,"clique mantido não repete");
        Check(processor.Process(click,5860,false,false,options).Mouse.Count == 0 && !processor.Enabled,"fim bloqueia mouse/clique");
        var custom = options with { Launcher = new(PadButtons.South,"double-tap") };
        processor.Process(neutral,6000,true,true,custom); processor.Process(neutral,6080,true,true,custom);
        var south = neutral with { Neutral = false, RawButtons = PadButtons.South, Buttons = ControllerButtons.Confirm };
        Check(processor.Process(south,6100,true,true,custom).Navigation.Count == 0,"botão reservado não confirma jogo");
        processor.Process(neutral,6180,true,true,custom); processor.Process(south,6300,true,true,custom);
        Check(processor.Process(neutral,6380,true,true,custom).ToggleLauncher,"duplo toque configurável");
        processor.Process(null,6400,true,true,custom);
        Check(!processor.Released(6500) && !processor.Enabled,"desconexão exige soltura nova");
        var pointer = new StickPointer(); pointer.Update(1,0,0,options);
        var fast = pointer.Update(1,0,20,options)!; pointer.Reset(); pointer.Update(.3,0,0,options);
        int slow = 0; for(int t=20;t<=200;t+=20) slow += pointer.Update(.3,0,t,options)?.X ?? 0;
        Check(fast.X == 22 && slow is > 0 and < 22,"curva precisa conserva frações para movimento lento");
        Check(pointer.Update(.05,0,220,options) == null,"zona morta elimina drift");
        int Distance(int interval) { var p=new StickPointer();p.Update(1,0,0,options); int x=0;for(int t=interval;t<=1000;t+=interval)x+=p.Update(1,0,t,options)?.X ?? 0;return x; }
        Check(Math.Abs(Distance(10)-Distance(20))<=1,"velocidade independente da frequência dos relatórios");
        bool refused = false;try{(options with{Sensitivity=double.NaN}).Validate();}catch(InvalidOperationException){refused=true;}
        Check(refused,"sensibilidade inválida recusada");
        Console.WriteLine("PASS: XInput/Switch sintéticos, atalhos configuráveis, soltura, cliques e curva do analógico sem hardware.");
    }
}
