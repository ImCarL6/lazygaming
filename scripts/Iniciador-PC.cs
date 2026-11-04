// Build with the .NET Framework compiler included in Windows; independent of .NET 10.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class LazyGamingStarter
{
    internal sealed class CheckResult {
        public bool Ready;
        public bool WebView2Available;
        public string Message;
        public string HelpUrl;
    }
    const string DotNetUrl = "https://dotnet.microsoft.com/download/dotnet/10.0";
    const string WebViewUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";
    static CheckResult Failure(string text, string url) { return new CheckResult { Message=text, HelpUrl=url }; }
    static bool SelfContained(string directory) {
        if (File.Exists(Path.Combine(directory,"coreclr.dll"))) return true;
        try {
            var json = new JavaScriptSerializer();
            var release = Path.Combine(directory,"release.json");
            if (File.Exists(release)) {
                var values = json.Deserialize<Dictionary<string,object>>(File.ReadAllText(release));
                object value;
                if (values.TryGetValue("selfContained",out value) && value is bool && (bool)value) return true;
            }
            var config=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(directory,"LazyGaming.Host.runtimeconfig.json")));
            var runtime=(Dictionary<string,object>)config["runtimeOptions"];
            return runtime.ContainsKey("includedFrameworks");
        } catch { return false; }
    }
    internal static CheckResult Check(string directory) {
        if (!Environment.Is64BitOperatingSystem) return Failure("Este pacote precisa do Windows de 64 bits.",null);
        foreach (string name in new[] { "LazyGaming.Host.exe","LazyGaming.Host.dll","LazyGaming.Host.runtimeconfig.json","LazyGaming.Host.deps.json",
            "Microsoft.Web.WebView2.Core.dll","Microsoft.Web.WebView2.WinForms.dll","wwwroot/index.html","wwwroot/hub.html" })
            if (!File.Exists(Path.Combine(directory,name))) return Failure("Alguns arquivos do LazyGaming estão ausentes. Extraia novamente o ZIP completo para uma pasta local.",null);
        bool complete=SelfContained(directory);
        if (complete) foreach(string name in new[]{"hostfxr.dll","hostpolicy.dll","coreclr.dll","System.Private.CoreLib.dll"})
            if(!File.Exists(Path.Combine(directory,name))) return Failure("O pacote que inclui o .NET está incompleto. Extraia novamente o ZIP completo.",null);
        try {
            var start=new ProcessStartInfo(Path.Combine(directory,"LazyGaming.Host.exe"),"--check-dependencies") {
                WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true
            };
            start.EnvironmentVariables["DOTNET_DISABLE_GUI_ERRORS"]="1"; // This child only, never machine/user settings.
            using(var process=Process.Start(start)) {
                Task<string> stdout=process.StandardOutput.ReadToEndAsync();
                Task<string> stderr=process.StandardError.ReadToEndAsync();
                if(!process.WaitForExit(10000)) {
                    // Only this exact read-only health child, never a running helper/game.
                    process.Kill();process.WaitForExit();
                    return Failure("A verificação demorou mais que o esperado. Aguarde os arquivos ficarem disponíveis e tente novamente.",null);
                }
                Task.WaitAll(stdout,stderr);
                if(process.ExitCode!=0) return Failure(complete
                    ? "Não foi possível carregar o pacote completo. Extraia-o novamente e confira se o Windows bloqueou algum arquivo."
                    : "Este pacote precisa dos componentes .NET 10 Desktop Runtime e ASP.NET Core Runtime para Windows x64. Você também pode usar o ZIP completo, que já inclui o .NET.",complete?null:DotNetUrl);
                var values=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(stdout.Result);
                object webview;
                if(values==null || !values.TryGetValue("webView2Available",out webview) || !(webview is bool))
                    return Failure("Os arquivos do pacote não correspondem a esta versão. Extraia o ZIP completo em outra pasta.",null);
                bool available=(bool)webview;
                return new CheckResult { Ready=true,WebView2Available=available,HelpUrl=available?null:WebViewUrl,
                    Message=available?"Tudo pronto.":"O launcher precisa do WebView2. A Central pode ser usada no navegador enquanto isso." };
            }
        } catch {
            return Failure("Não foi possível verificar os componentes do LazyGaming. Confira se o pacote foi extraído e se a pasta está disponível.",null);
        }
    }
    static void Explain(CheckResult result) {
        Application.EnableVisualStyles();
        using(var form=new Form { Text="LazyGaming",Width=510,Height=240,StartPosition=FormStartPosition.CenterScreen,
            FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false }) {
            form.Font=new Font("Segoe UI",10);
            var label=new Label { Text=result.Message,Left=20,Top=20,Width=450,Height=115 };
            var close=new Button { Text="Fechar",Left=365,Top=150,Width=100,DialogResult=DialogResult.Cancel };
            form.Controls.Add(label);form.Controls.Add(close);form.CancelButton=close;
            if(result.HelpUrl!=null) {
                var download=new Button { Text="Página oficial de instalação",Left=20,Top=150,Width=245 };
                download.Click+=delegate { try { Process.Start(new ProcessStartInfo(result.HelpUrl) {UseShellExecute=true}); } catch { } };
                form.Controls.Add(download);
            }
            form.ShowDialog();
        }
    }
    [STAThread]
    static int Main(string[] args) {
        string directory=AppDomain.CurrentDomain.BaseDirectory;
        var result=Check(directory);
        if(Array.IndexOf(args,"--check-only")>=0) {
            Console.WriteLine(new JavaScriptSerializer().Serialize(result));
            return result.Ready?0:1;
        }
        if(!result.Ready) { Explain(result);return 1; }
        string option=Array.IndexOf(args,"--hub")>=0 || !result.WebView2Available?"--open-hub":"--open-launcher";
        try {
            using(var process=Process.Start(new ProcessStartInfo(Path.Combine(directory,"LazyGaming.Host.exe"),option) {
                WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden
            })) {
                if(process.WaitForExit(1000) && process.ExitCode!=0) {
                    Explain(Failure("O auxiliar não conseguiu iniciar. Confira se a pasta está disponível e tente abrir a Central novamente.",null));return 1;
                }
            }
            return 0;
        } catch { Explain(Failure("Não foi possível abrir o LazyGaming. Confira os arquivos e as permissões da pasta.",null));return 1; }
    }
}
