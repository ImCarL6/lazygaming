using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LazyGaming;

// DWM mantém a superfície do jogo na GPU; o navegador desenha somente o menu
// transparente por cima. Nenhuma captura/JPEG/HTTP/decodificação por quadro.
public sealed class NativeDesktop(string directory, Uri origin, Func<nint> gameWindow) : IDisposable
{
    readonly TaskCompletionSource<nint> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    GlassWindow? form;
    Action? focusContent;
    Action? announceShown;
    // Fechar toca no host: a página oculta pode ter tarefas adiadas pelo navegador.
    readonly System.Media.SoundPlayer? closeSound = LoadSound("launcher-close.wav");
    volatile bool live;
    object dimensions = new { };
    object scale = new { };
    string? issue;
    public object Status => new { engine = "webview2-dwm", live, dimensions, scale, issue };
    public Task<nint> Open(string url) {
        var ui = new Thread(() => {
            try {
                using var dpi = WindowFocus.PhysicalCoordinates();
                using var window = new GlassWindow(); form = window;
                using var surface = new GameSurface();
                using var web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Transparent };
                focusContent = () => { if (WindowFocus.IsForeground(window.Handle)) web.Focus(); };
                // A página toca o som no primeiro quadro desenhado depois de visível (requestAnimationFrame).
                announceShown = () => { try { web.CoreWebView2?.PostWebMessageAsJson("{\"type\":\"launcher-shown\"}"); } catch (InvalidOperationException) { } };
                window.Controls.Add(web); window.Owner = surface;
                window.Activated += (_, _) => web.Focus();
                using var timer = new System.Windows.Forms.Timer { Interval = 500 };
                bool desired = false;
                bool readingScale = false;
                string lastScale = "";
                async void RefreshScale() {
                    if (web.CoreWebView2 == null || readingScale) return;
                    var key = $"{window.Bounds}:{window.DeviceDpi}:{web.ClientSize}:{web.ZoomFactor}";
                    if (key == lastScale) return;
                    readingScale = true;
                    try {
                        var browser = await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({width:innerWidth,height:innerHeight,dpr:devicePixelRatio,viewportScale:visualViewport?visualViewport.scale:1})");
                        var decoded = JsonSerializer.Deserialize<string>(browser);
                        scale = new { dpi = WindowFocus.WindowDpi(window.Handle), managedDpi = window.DeviceDpi, awareness = WindowFocus.DpiAwareness(window.Handle),
                            formWidth = window.Width, formHeight = window.Height, webWidth = web.Width, webHeight = web.Height,
                            zoom = web.ZoomFactor, browser = decoded == null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(decoded) };
                        lastScale = key;
                    } catch (Exception e) when (e is InvalidOperationException or JsonException or COMException) { }
                    finally { readingScale = false; }
                }
                void RefreshSurface() {
                    bool active = false;
                    try { active = desired && window.Visible && window.WindowState != FormWindowState.Minimized && surface.Present(gameWindow(), window.Bounds); }
                    catch (Exception e) { issue = e.GetType().Name + ":" + e.HResult; }
                    if (!active) surface.Clear(); else dimensions = surface.Dimensions;
                    if (live == active) return;
                    live = active;
                    web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "background", active }));
                }
                // Escala independente do display: área lógica de ~1080 de altura (limitada pela largura)
                // em 1080p, 1440p, 4K e ultrawide, qualquer DPI do Windows; recalcula ao mover/redimensionar.
                void ApplyZoom() {
                    if (web.CoreWebView2 == null) return;
                    var zoom = LauncherZoom(web.ClientSize.Width, web.ClientSize.Height, window.DeviceDpi);
                    if (zoom > 0 && Math.Abs(web.ZoomFactor - zoom) > 0.005) web.ZoomFactor = zoom;
                }
                web.SizeChanged += (_, _) => ApplyZoom();
                window.DpiChanged += (_, _) => ApplyZoom();
                window.VisibleChanged += (_, _) => { ApplyZoom(); RefreshSurface(); };
                window.LocationChanged += (_, _) => RefreshSurface();
                window.SizeChanged += (_, _) => RefreshSurface();
                window.FormClosed += (_, _) => Application.ExitThread();
                timer.Tick += (_, _) => { RefreshSurface(); RefreshScale(); };
                _ = window.Handle;
                window.BeginInvoke(async () => {
                    try {
                        var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
                        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(directory, "desktop-webview"), options);
                        await web.EnsureCoreWebView2Async(env);
                        var core = web.CoreWebView2;
                        core.Settings.AreDevToolsEnabled = false;
                        core.Settings.AreDefaultContextMenusEnabled = false;
                        core.Settings.AreHostObjectsAllowed = false;
                        core.Settings.IsStatusBarEnabled = false;
                        core.Settings.IsZoomControlEnabled = false;
                        core.NewWindowRequested += (_, e) => e.Handled = true;
                        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
                        core.NavigationStarting += (_, e) => { if (!SameOrigin(e.Uri, origin)) e.Cancel = true; };
                        core.WebMessageReceived += (_, e) => {
                            if (!SameOrigin(e.Source, origin)) return;
                            try {
                                using var message = JsonDocument.Parse(e.WebMessageAsJson);
                                if (message.RootElement.GetProperty("type").GetString() != "background") return;
                                desired = message.RootElement.GetProperty("enabled").GetBoolean(); RefreshSurface();
                                // Confirmar mesmo se o estado não mudou (página reiniciada).
                                core.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "background", active = live }));
                            } catch (Exception e2) when (e2 is JsonException or InvalidOperationException or KeyNotFoundException) { }
                        };
                        await core.AddScriptToExecuteOnDocumentCreatedAsync("if(window===window.top&&location.origin===" + JsonSerializer.Serialize(origin.GetLeftPart(UriPartial.Authority)) + "){Object.defineProperty(window,'LGNativeDesktop',{value:Object.freeze({background:function(enabled){chrome.webview.postMessage({type:'background',enabled:!!enabled});}})});}");
                        core.NavigationCompleted += (_, e) => {
                            ApplyZoom();
                            if (e.IsSuccess) { timer.Start(); ready.TrySetResult(window.Handle); }
                            else ready.TrySetException(new InvalidOperationException("Não foi possível carregar o painel do PC."));
                        };
                        core.Navigate(url);
                    } catch (Exception e) { ready.TrySetException(new InvalidOperationException("Não foi possível preparar o painel nativo do PC.", e)); window.Close(); }
                });
                Application.Run();
            } catch (Exception e) { ready.TrySetException(e); }
            finally { live = false; focusContent = null; announceShown = null; form = null; }
        }) { IsBackground = true, Name = "LazyGaming desktop" };
        ui.SetApartmentState(ApartmentState.STA); ui.Start();
        return ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
    // Fator de zoom do WebView2 para a área lógica de referência (1080 de altura; 1440 de largura mínima).
    public static double LauncherZoom(int width, int height, int dpi) {
        if (width <= 0 || height <= 0 || dpi <= 0) return 0;
        double scale = dpi / 96.0, zoom = Math.Min(height / 1080.0, width / 1440.0) / scale;
        return Math.Clamp(Math.Round(zoom, 3), 0.25, 5);
    }
    public static bool SameOrigin(string supplied, Uri expected) => Uri.TryCreate(supplied, UriKind.Absolute, out var uri) &&
        uri.Scheme == expected.Scheme && uri.Host == expected.Host && uri.Port == expected.Port && uri.UserInfo.Length == 0;
    public Task Show(Rectangle? bounds = null) => Dispatch(() => { if (bounds.HasValue) form!.Bounds = bounds.Value; form!.WindowState = FormWindowState.Normal; bool hidden = !form.Visible; form.Show(); form.BringToFront(); focusContent?.Invoke(); if (hidden) announceShown?.Invoke(); });
    public Task FocusInput() => Dispatch(() => focusContent?.Invoke());
    public Task Align(Rectangle bounds) => Dispatch(() => { form!.WindowState = FormWindowState.Normal; form.Bounds = bounds; });
    public Task Hide() => Dispatch(() => {
        bool visible = form!.Visible; form.Hide();
        // Nunca antes de sumir: aguarda a composição DWM que já não contém a janela.
        if (visible && closeSound != null) _ = Task.Run(() => { try { DwmFlush(); closeSound.Play(); } catch { } });
    });
    static System.Media.SoundPlayer? LoadSound(string name) {
        try { var player = new System.Media.SoundPlayer(Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "audio", name)); player.Load(); return player; }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException) { return null; }
    }
    [DllImport("dwmapi.dll")] static extern int DwmFlush();
    Task Dispatch(Action action) {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = form;
        if (window == null || window.IsDisposed) return Task.FromException(new InvalidOperationException("O painel do PC foi fechado."));
        try { window.BeginInvoke(() => { try { action(); done.TrySetResult(); } catch (Exception e) { done.TrySetException(e); } }); }
        catch (InvalidOperationException e) { done.TrySetException(e); }
        return done.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    public void Dispose() { closeSound?.Dispose(); var window = form; try { if (window is { IsDisposed: false }) window.BeginInvoke(window.Close); } catch (InvalidOperationException) { } }

    sealed class GlassWindow : Form {
        public GlassWindow() { Text = "LazyGaming — PC"; FormBorderStyle = FormBorderStyle.None; BackColor = Color.Black; StartPosition = FormStartPosition.Manual; TopMost = true; AutoScaleMode = AutoScaleMode.None; }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 }; Marshal.ThrowExceptionForHR(DwmExtendFrameIntoClientArea(Handle, ref margins)); }
        [StructLayout(LayoutKind.Sequential)] struct Margins { public int Left, Right, Top, Bottom; }
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(nint window, ref Margins margins);
    }
    sealed class GameSurface : Form {
        nint thumbnail, source;
        public object Dimensions { get; private set; } = new { };
        public GameSurface() { Text = "LazyGaming game surface"; FormBorderStyle = FormBorderStyle.None; BackColor = Color.Black; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true; Enabled = false; AutoScaleMode = AutoScaleMode.None; }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000080; return p; } }
        public bool Present(nint target, Rectangle bounds) {
            if (target == 0 || !IsWindow(target) || IsIconic(target)) return false;
            if (source != target) { Clear(); Marshal.ThrowExceptionForHR(DwmRegisterThumbnail(Handle, target, out thumbnail)); source = target; }
            Bounds = bounds;
            if (!GetClientRect(target, out var rect)) return false;
            var fit = Fit(rect.Right, rect.Bottom, bounds.Width, bounds.Height);
            Dimensions = new { sourceWidth = rect.Right, sourceHeight = rect.Bottom, renderedWidth = fit.Width, renderedHeight = fit.Height };
            if (fit.Width == 0 || fit.Height == 0) return false;
            var properties = new ThumbnailProperties { Flags = 1 | 4 | 8 | 16, Destination = new() { Left = fit.X, Top = fit.Y, Right = fit.Right, Bottom = fit.Bottom }, Opacity = 255, Visible = true, ClientOnly = true };
            Marshal.ThrowExceptionForHR(DwmUpdateThumbnailProperties(thumbnail, ref properties));
            if (!Visible) Show();
            return true;
        }
        public void Clear() { if (Visible) Hide(); if (thumbnail != 0) DwmUnregisterThumbnail(thumbnail); thumbnail = source = 0; }
        protected override void Dispose(bool disposing) { Clear(); base.Dispose(disposing); }
        [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct ThumbnailProperties { public uint Flags; public Rect Destination, Source; public byte Opacity; [MarshalAs(UnmanagedType.Bool)] public bool Visible; [MarshalAs(UnmanagedType.Bool)] public bool ClientOnly; }
        [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
        [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref ThumbnailProperties properties);
        [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(nint thumbnail);
        [DllImport("user32.dll")] static extern bool GetClientRect(nint window, out Rect rect);
        [DllImport("user32.dll")] static extern bool IsWindow(nint window);
        [DllImport("user32.dll")] static extern bool IsIconic(nint window);
    }
    public static Rectangle Fit(int width, int height, int viewportWidth, int viewportHeight) {
        if (width <= 0 || height <= 0 || viewportWidth <= 0 || viewportHeight <= 0) return Rectangle.Empty;
        double scale = Math.Min(viewportWidth / (double)width, viewportHeight / (double)height);
        int w = (int)Math.Round(width * scale), h = (int)Math.Round(height * scale);
        return new((viewportWidth - w) / 2, (viewportHeight - h) / 2, w, h);
    }
}
