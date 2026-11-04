using LazyGaming;

static class ShortcutCaptureTests
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static void Run(){
        var neutral=new ControllerState(ControllerButtons.None,true,null,null,false,false,PadButtons.None);
        var mic=neutral with{Neutral=false,Microphone=true,RawButtons=PadButtons.Microphone};
        var capture=new ShortcutCapture();capture.Begin(0);
        capture.Observe(mic,10);Check(capture.State(20).Phase=="release","recording waits for initial release");
        capture.Observe(neutral,100);capture.Observe(neutral,180);capture.Observe(mic,200);capture.Observe(neutral,260);
        capture.Observe(mic,380);Check(capture.Observe(neutral,440),"completion report is consumed");
        Check(capture.State(440).Binding is{Buttons:PadButtons.Microphone,Gesture:"double-tap"},"double microphone detected");
        capture.Begin(1000);capture.Observe(neutral,1000);capture.Observe(neutral,1080);
        capture.Observe(mic with{RawButtons=PadButtons.Select},1100);capture.Observe(mic with{RawButtons=PadButtons.Select|PadButtons.Start},1120);capture.Observe(neutral,2120);
        Check(capture.State(2120).Binding is{Buttons:PadButtons.Select|PadButtons.Start,Gesture:"hold",HoldMs:1000},"combination and hold inferred");
        capture.Begin(3000);capture.Observe(null,3050);Check(capture.State(3060).Phase=="release","disconnect never invents a binding");
        Check(capture.State(18000).Phase=="timeout","capture expires without reports");capture.Cancel();Check(!capture.Active(19000),"cancel discards capture");
        capture.Begin(19000);capture.Observe(neutral,19000);capture.Observe(neutral,19080);capture.Observe(mic,19100);capture.Observe(neutral,19200);
        Check(capture.State(19601) is{Phase:"done",SinglePress:true,Binding.Buttons:PadButtons.Microphone},"one short press captures the button without demanding a double gesture");
        var options=new ControllerOptions(Launcher:new(PadButtons.Microphone,"double-tap"),Mouse:new(PadButtons.Microphone,"hold",1500));options.Validate();
        var processor=new ControllerProcessor();processor.Process(neutral,20000,true,false,options);processor.Process(mic,20100,true,false,options);processor.Process(neutral,20180,true,false,options);processor.Process(mic,20300,true,false,options);
        Check(processor.Process(neutral,20380,true,false,options).ToggleLauncher && !processor.Enabled,"double mic opens only the launcher");
        processor.Process(mic,21000,true,false,options);Check(processor.Process(mic,22500,true,false,options).MouseModeChanged==true,"hold mic toggles mouse using same physical button");
        Check(!processor.Process(neutral,22600,true,false,options).ToggleLauncher,"long hold does not become double tap");
        var pad=neutral with{TouchpadPressed=true,RawButtons=PadButtons.Touchpad,Touch=new(new(1,100,100),null)};
        Check(processor.Process(pad,22650,true,false,options).Mouse.Any(a=>a.Kind=="left-down"),"physical touchpad press holds left button");
        Console.WriteLine("PASS: input capture release/double/hold/chord/cancel/timeout and DualSense microphone menu/mouse separation.");
    }
}
