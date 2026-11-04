using LazyGaming;
using System.Threading.Channels;

// Reproduz o uso real: DualSense completo enviando relatórios contínuos (~250 Hz),
// mic segurado liga/desliga o mouse, touchpad arrasta e toque duplo abre o launcher.
static class MouseTogglePipelineTests
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    sealed class Pad { public volatile bool Mic, Press, Finger; public volatile int X = 500; }
    static byte[] Report(Pad pad) {
        var r = new byte[78]; r[0] = 0x31; for (int i = 2; i < 6; i++) r[i] = 128; r[9] = 8;
        r[11] = (byte)((pad.Mic ? 4 : 0) | (pad.Press ? 2 : 0));
        if (pad.Finger) { r[34] = 1; r[35] = (byte)(pad.X & 255); r[36] = (byte)((pad.X >> 8) & 15 | (300 & 15) << 4); r[37] = 300 >> 4; } else r[34] = 128;
        r[38] = 128; return r;
    }
    sealed class Mouse : IMouseOutput { public readonly List<string> Actions = []; public void Send(MouseAction action) { lock (Actions) Actions.Add(action.Kind); } }
    public static async Task Run() {
        var pad = new Pad(); var input = new Input(); var mouse = new Mouse();
        var preferences = new PcPreferences(Controller: new(Launcher: new(PadButtons.Microphone, "double-tap"), Mouse: new(PadButtons.Microphone, "hold", 1500)));
        int launched = 0; var changes = new List<bool>();
        var reader = new TouchpadMouse(() => true, value => { lock (changes) changes.Add(value); }, () => Interlocked.Increment(ref launched), source: new Source(input), mouse: mouse, preferences: () => preferences);
        using var cancellation = new CancellationTokenSource(); var running = reader.Run(cancellation.Token);
        var pump = Task.Run(async () => { while (!cancellation.IsCancellationRequested) { input.Send(Report(pad)); await Task.Delay(4); } });
        async Task Hold(int ms) { pad.Mic = true; await Task.Delay(ms); pad.Mic = false; await Task.Delay(150); }
        async Task Tap() { pad.Mic = true; await Task.Delay(70); pad.Mic = false; await Task.Delay(90); }
        try {
            await Task.Delay(300);
            await Hold(1700); Check(changes.SequenceEqual([true]) && reader.Enabled, "segurar liga o mouse");
            pad.Finger = true; await Task.Delay(40); pad.Press = true; await Task.Delay(40);
            for (int i = 0; i < 20; i++) { pad.X += 8; await Task.Delay(8); }
            pad.Press = false; await Task.Delay(40); pad.Finger = false; await Task.Delay(60);
            lock (mouse.Actions) Check(mouse.Actions.Contains("left-down") && mouse.Actions.LastIndexOf("left-up") > mouse.Actions.IndexOf("left-down"), "arrastar solta o botão");
            await Hold(1700); Check(changes.SequenceEqual([true, false]) && !reader.Enabled, "segurar de novo desliga o mouse");
            await Tap(); await Tap(); await Task.Delay(200);
            Check(launched == 1, $"toque duplo abre o launcher depois de ligar/desligar (aberturas: {launched})");
            await Hold(1700); Check(changes.SequenceEqual([true, false, true]), "ciclo continua respondendo");
            await Tap(); await Tap(); await Task.Delay(200);
            Check(launched == 2, $"atalho segue funcionando (aberturas: {launched})");
            Check(!running.IsCompleted, "leitura do controle continua viva");
        } finally { cancellation.Cancel(); try { await running; } catch (OperationCanceledException) { } try { await pump; } catch (OperationCanceledException) { } }
        Console.WriteLine("PASS: mic segurar liga/desliga o mouse repetidamente, arrasto solta o botão e toque duplo continua abrindo o launcher.");
        foreach (var failure in new[] { "throws", "hangs" }) {
            var good = new Input(); var faults = new List<string>(); int opened = 0, launches = 0;
            var flaky = new SequenceSource(() => opened++ == 0 ? new Broken(failure) : good);
            var recovering = new TouchpadMouse(() => true, launcher: () => Interlocked.Increment(ref launches), source: flaky, mouse: new Mouse(),
                preferences: () => preferences, diagnostic: (kind, _) => { lock (faults) faults.Add(kind); });
            using var stop = new CancellationTokenSource(); var task = recovering.Run(stop.Token);
            var state = new Pad();
            var feed = Task.Run(async () => { while (!stop.IsCancellationRequested) { good.Send(Report(state)); await Task.Delay(4); } });
            try {
                using var deadline = new CancellationTokenSource(9000);
                while (opened < 2) await Task.Delay(20, deadline.Token);
                await Task.Delay(300);
                state.Mic = true; await Task.Delay(70); state.Mic = false; await Task.Delay(90); state.Mic = true; await Task.Delay(70); state.Mic = false; await Task.Delay(250);
                Check(launches == 1 && !task.IsCompleted, $"leitura se recupera de falha '{failure}' e o atalho volta a funcionar");
                lock (faults) Check(faults.Contains(failure == "throws" ? "read-loop-fault" : "read-timeout"), $"falha '{failure}' fica registrada no diagnóstico");
            } finally { stop.Cancel(); try { await task; } catch (OperationCanceledException) { } try { await feed; } catch (OperationCanceledException) { } }
        }
        Console.WriteLine("PASS: leitura do controle sobrevive a exceção inesperada e a leitura que ignora cancelamento, com diagnóstico.");
    }
    sealed class SequenceSource(Func<IControllerConnection> next) : IControllerSource { public IControllerConnection Open() => next(); }
    sealed class Broken(string mode) : Stream, IControllerConnection {
        Stream IControllerConnection.Input => this; public int ReportLength => 78; public IControllerAdapter Adapter { get; } = new DualSenseAdapter();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellation = default) =>
            mode == "throws" ? throw new InvalidOperationException("estado inesperado do dispositivo") : new(new TaskCompletionSource<int>().Task);
        public override bool CanRead => true; public override bool CanWrite => false; public override bool CanSeek => false; public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException(); public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
    sealed class Source(Input input) : IControllerSource { public IControllerConnection Open() => input; }
    sealed class Input : Stream, IControllerConnection {
        readonly Channel<byte[]> reports = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
        public void Send(byte[] value) => reports.Writer.TryWrite(value);
        Stream IControllerConnection.Input => this; public int ReportLength => 78; public IControllerAdapter Adapter { get; } = new DualSenseAdapter();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellation = default) { var data = await reports.Reader.ReadAsync(cancellation); data.CopyTo(buffer); return data.Length; }
        public override bool CanRead => true; public override bool CanWrite => false; public override bool CanSeek => false; public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException(); public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
}
