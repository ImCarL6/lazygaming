using LazyGaming;
using System.Threading.Channels;

static class ControllerPipelineTests
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static byte[] Basic(int buttons=0) {
        var r=new byte[78];Convert.FromHexString("01807C7F7A08000C00000000").CopyTo(r,0);r[6]=(byte)buttons;return r;
    }
    static byte[] Full(bool mic=false) {
        var r=new byte[78];r[0]=0x31;for(int i=2;i<6;i++)r[i]=128;r[9]=8;r[11]=(byte)(mic?4:0);r[34]=r[38]=128;return r;
    }
    static async Task Until(Func<bool> condition) {
        using var deadline=new CancellationTokenSource(3000);
        while(!condition()){deadline.Token.ThrowIfCancellationRequested();await Task.Delay(10,deadline.Token);}
    }
    public static async Task Run() {
        foreach(bool features in new[]{true,false}) {
            var connection=new Input();var source=new Source(connection);
            var preferences=new PcPreferences(Controller:new(Launcher:new(PadButtons.Microphone,"double-tap")),DualSenseBluetoothFeatures:features);
            int launched=0;var reader=new TouchpadMouse(()=>false,launcher:()=>Interlocked.Increment(ref launched),source:source,preferences:()=>preferences);
            using var cancellation=new CancellationTokenSource();var running=reader.Run(cancellation.Token);
            try {
                connection.Send(Basic());await Until(()=>reader.Detection.Connected);
                Check(!reader.Detection.Microphone && !reader.Detection.Touchpad,"basic report never announces unavailable features");
                Check(connection.Requests==(features?1:0),"feature negotiation respects opt-out");
                connection.Send(Basic());await Task.Delay(100);connection.Send(Basic());await Until(()=>reader.Released);
                Check(connection.Requests==(features?1:0),"basic reports do not cause negotiation loops");
                reader.Capture.Begin(Environment.TickCount64);connection.Send(Basic());await Task.Delay(100);connection.Send(Basic());await Task.Delay(25);
                connection.Send(Basic(0x10));await Task.Delay(30);connection.Send(Basic());
                await Until(()=>reader.Capture.State(Environment.TickCount64).Phase=="done");
                Check(reader.Capture.State(Environment.TickCount64).Binding?.Buttons==PadButtons.Select && launched==0,"actual read pipeline records a short press without opening launcher");
                connection.Send(Basic());await Task.Delay(25);connection.Send(Basic(0x30));await Task.Delay(1050);connection.Send(Basic(0x30));
                await Until(()=>launched==1);Check(!reader.Detection.Microphone,"saved mic shortcut has an operational basic fallback");
                connection.Send(Full());await Until(()=>reader.Detection.Microphone && reader.Detection.Touchpad);
                connection.Send(Full(true));await Task.Delay(30);connection.Send(Full());await Task.Delay(30);connection.Send(Full(true));await Task.Delay(30);connection.Send(Full());
                await Until(()=>launched==2);
                Check(source.Opens==1,"mode transition requires neither second reader nor virtual device");
            } finally {cancellation.Cancel();await running;}
        }
        Console.WriteLine("PASS: shared controller read pipeline, captured Bluetooth counter, optional feature handshake without loops, capture, fallback launcher and live full-report transition.");
    }
    sealed class Source(Input input):IControllerSource {
        public int Opens;
        public IControllerConnection Open(){Opens++;return input;}
    }
    sealed class Input:Stream,IControllerConnection,IControllerFeatures {
        readonly Channel<byte[]> reports=Channel.CreateUnbounded<byte[]>();
        public int Requests;
        public void Send(byte[] value)=>reports.Writer.TryWrite(value);
        public Task<bool> EnableFullReports(CancellationToken cancellation){Requests++;return Task.FromResult(true);}
        Stream IControllerConnection.Input=>this;public int ReportLength=>78;public IControllerAdapter Adapter{get;}=new DualSenseAdapter();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellation=default){var data=await reports.Reader.ReadAsync(cancellation);data.CopyTo(buffer);return data.Length;}
        public override bool CanRead=>true;public override bool CanWrite=>false;public override bool CanSeek=>false;public override long Length=>throw new NotSupportedException();
        public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override void Flush(){}
        public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
    }
}
