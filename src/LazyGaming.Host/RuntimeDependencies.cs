using System.Runtime.CompilerServices;
using Microsoft.Web.WebView2.Core;

namespace LazyGaming;

public sealed record DependencyStatus(bool WebView2Available, string DotNetVersion, string? Note, string HelpUrl);
public static class RuntimeDependencies
{
    public const string WebViewDownload = "https://developer.microsoft.com/microsoft-edge/webview2/";
    [MethodImpl(MethodImplOptions.NoInlining)]
    static string DetectWebView() => CoreWebView2Environment.GetAvailableBrowserVersionString();
    public static DependencyStatus Inspect(Func<string>? detect = null) {
        bool available;
        try { available = !string.IsNullOrWhiteSpace((detect ?? DetectWebView)()); }
        catch (Exception e) when (e is WebView2RuntimeNotFoundException or DllNotFoundException or IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException or System.Runtime.InteropServices.COMException or TypeLoadException) { available = false; }
        return new(available,Environment.Version.ToString(),available ? null :
            "Para abrir o launcher pelo controle, instale ou repare o Microsoft Edge WebView2 Runtime. A Central continua disponível no navegador.",WebViewDownload);
    }
}
