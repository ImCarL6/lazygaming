using LazyGaming;
using System.Drawing;

static class NativeDesktopTests {
    public static void Run() {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        Check(NativeDesktop.Fit(3840, 2160, 3840, 2160) == new Rectangle(0, 0, 3840, 2160), "4K nativo preserva pixels e quadro completo");
        Check(NativeDesktop.Fit(2560, 1440, 2560, 1440) == new Rectangle(0, 0, 2560, 1440), "1440p não sofre redução artificial");
        Check(NativeDesktop.Fit(1920, 1080, 1024, 1024) == new Rectangle(0, 224, 1024, 576), "outra proporção contém quadro inteiro, sem cortar/esticar");
        Check(NativeDesktop.Fit(0, 1080, 3840, 2160) == Rectangle.Empty, "fonte inválida não é exibida");
        var origin = new Uri("http://127.0.0.1:47831");
        Check(NativeDesktop.SameOrigin("http://127.0.0.1:47831/#desktop=test", origin), "origem local exata aceita");
        foreach (var url in new[] { "http://127.0.0.1:47832/", "https://127.0.0.1:47831/", "http://localhost:47831/", "http://127.0.0.1.evil:47831/", "http://user@127.0.0.1:47831/", "file:///tmp/test", "invalid" })
            Check(!NativeDesktop.SameOrigin(url, origin), "navegação/mensagem externa recusada: " + url);
        Console.WriteLine("PASS: composição nativa mantém resolução/proporção e bridge limitado à origem loopback exata.");
    }
}
