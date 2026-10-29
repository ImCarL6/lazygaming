using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace LazyGaming;

// Captura só a janela verificada do jogo, sob demanda, exclusivamente em loopback.
public sealed class GameBackground(Func<nint> gameWindow, Func<bool> visible, int maxWidth = 960, float quality = .55f) : IDisposable
{
    readonly SemaphoreSlim gate = new(1, 1);
    IDirect3DDevice? device;
    Direct3D11CaptureFramePool? pool;
    GraphicsCaptureSession? session;
    GraphicsCaptureItem? item;
    nint window;
    Windows.Graphics.SizeInt32 poolSize;
    long demand, checkedAt;
    byte[]? image;
    long frames;
    double totalEncodeMs;
    string? error;
    public object Status => new { capturing = session != null, frames, averageEncodeMs = frames == 0 ? 0 : Math.Round(totalEncodeMs / frames, 1), error };

    public async Task<byte[]?> Frame()
    {
        await gate.WaitAsync();
        try {
            demand = Environment.TickCount64;
            if (!visible()) { StopCore(); return null; }
            if (window == 0 || demand - checkedAt > 1000) {
                checkedAt = demand;
                var target = gameWindow();
                if (target != window) { StopCore(); if (target != 0) Start(target); }
            }
            if (pool == null || item == null) return null;
            var frame = pool.TryGetNextFrame();
            if (frame == null) return image;
            // Descartar o frame antigo: o menu deve acompanhar o presente, sem fila.
            var newer = pool.TryGetNextFrame();
            if (newer != null) { frame.Dispose(); frame = newer; }
            var encodeWatch = System.Diagnostics.Stopwatch.StartNew();
            byte[] bytes; var size = frame.ContentSize;
            using (frame) {
            if (size.Width < 1 || size.Height < 1) return null;
            using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Ignore);
            using var stream = new InMemoryRandomAccessStream();
            var options = new BitmapPropertySet { ["ImageQuality"] = new BitmapTypedValue(quality, Windows.Foundation.PropertyType.Single) };
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream, options);
            encoder.SetSoftwareBitmap(bitmap);
            var scale = Math.Min(1d, maxWidth / (double)bitmap.PixelWidth);
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Linear;
            encoder.BitmapTransform.ScaledWidth = (uint)Math.Max(1, bitmap.PixelWidth * scale);
            encoder.BitmapTransform.ScaledHeight = (uint)Math.Max(1, bitmap.PixelHeight * scale);
            await encoder.FlushAsync();
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes);
            image = bytes; frames++; totalEncodeMs += encodeWatch.Elapsed.TotalMilliseconds; error = null;
            }
            if (size.Width != poolSize.Width || size.Height != poolSize.Height) {
                pool.Recreate(device!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size); poolSize = size;
            }
            return bytes;
        } catch (Exception e) { StopCore(); error = e.GetType().Name + " 0x" + e.HResult.ToString("X8"); return null; }
        finally { gate.Release(); }
    }
    public async Task Tick()
    {
        if (!await gate.WaitAsync(0)) return;
        try { if (session != null && (!visible() || Environment.TickCount64 - demand > 1500)) StopCore(); }
        finally { gate.Release(); }
    }
    void Start(nint target)
    {
        if (!GraphicsCaptureSession.IsSupported()) return;
        nint name = 0, factory = 0, nativeItem = 0;
        try {
            const string type = "Windows.Graphics.Capture.GraphicsCaptureItem";
            Marshal.ThrowExceptionForHR(WindowsCreateString(type, type.Length, out name));
            var factoryId = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, ref factoryId, out factory));
            var interop = (ICaptureItemInterop)Marshal.GetObjectForIUnknown(factory);
            try {
                var itemId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                interop.CreateForWindow(target, ref itemId, out nativeItem);
                item = WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(nativeItem);
            } finally { Marshal.ReleaseComObject(interop); }
            device = CreateDevice();
            poolSize = item.Size;
            pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, poolSize);
            session = pool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;
            session.StartCapture(); window = target;
        } finally {
            if (nativeItem != 0) Marshal.Release(nativeItem);
            if (factory != 0) Marshal.Release(factory);
            if (name != 0) WindowsDeleteString(name);
        }
    }
    static IDirect3DDevice CreateDevice()
    {
        nint d3d = 0, context = 0, dxgi = 0, inspectable = 0;
        try {
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out d3d, out _, out context));
            var iid = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3d, in iid, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        } finally {
            if (inspectable != 0) Marshal.Release(inspectable);
            if (dxgi != 0) Marshal.Release(dxgi);
            if (context != 0) Marshal.Release(context);
            if (d3d != 0) Marshal.Release(d3d);
        }
    }
    void StopCore()
    {
        session?.Dispose(); session = null; pool?.Dispose(); pool = null;
        device?.Dispose(); device = null; item = null; window = 0; image = null;
    }
    public void Dispose() { gate.Wait(); try { StopCore(); } finally { gate.Release(); } }
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICaptureItemInterop { void CreateForWindow(nint window, ref Guid iid, out nint result); void CreateForMonitor(nint monitor, ref Guid iid, out nint result); }
    [DllImport("combase.dll")] static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string value, int length, out nint result);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] static extern int RoGetActivationFactory(nint name, ref Guid iid, out nint factory);
    [DllImport("d3d11.dll")] static extern int D3D11CreateDevice(nint adapter, int driver, nint software, uint flags, nint levels, uint count, uint sdk, out nint device, out int level, out nint context);
    [DllImport("d3d11.dll")] static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgi, out nint device);
}
