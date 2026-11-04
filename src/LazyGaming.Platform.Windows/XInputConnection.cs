using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace LazyGaming;

public sealed class XInputConnection(uint index) : Stream, IControllerConnection
{
    bool disposed;
    public Stream Input => this;
    public int ReportLength => 12;
    public IControllerAdapter Adapter { get; } = new XInputAdapter();
    public static XInputConnection? Open() {
        for (uint i = 0; i < 4; i++) if (XInputGetState(i, out _) == 0) return new(i);
        return null;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (buffer.Length < 12) throw new ArgumentException("Relatório incompleto.");
        await Task.Delay(16, cancellationToken);
        if (XInputGetState(index, out var state) != 0) throw new IOException("Controle desconectado.");
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.Span, state.Buttons);
        buffer.Span[2] = state.LeftTrigger; buffer.Span[3] = state.RightTrigger;
        BinaryPrimitives.WriteInt16LittleEndian(buffer.Span[4..], state.LX);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.Span[6..], state.LY);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.Span[8..], state.RX);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.Span[10..], state.RY);
        return 12;
    }
    protected override void Dispose(bool disposing) { disposed = true; base.Dispose(disposing); }
    public override bool CanRead => !disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    [StructLayout(LayoutKind.Sequential)] struct State {
        public uint Packet; public ushort Buttons; public byte LeftTrigger, RightTrigger; public short LX, LY, RX, RY;
    }
    [DllImport("xinput1_4.dll")] static extern uint XInputGetState(uint index, out State state);
}
