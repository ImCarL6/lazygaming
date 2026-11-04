using LazyGaming;
using System.Runtime.InteropServices;

static class RuntimeDependencyTests
{
    static void Check(bool value,string message) { if(!value)throw new Exception(message); }
    public static void Run() {
        Check(RuntimeDependencies.Inspect(()=>"142.0.1.2") is { WebView2Available:true, Note:null },"healthy runtime keeps normal launcher path");
        Check(!RuntimeDependencies.Inspect(()=>"").WebView2Available,"empty detection is unavailable");
        foreach(var error in new Exception[]{new DllNotFoundException(),new FileNotFoundException(),new BadImageFormatException(),new UnauthorizedAccessException(),new InvalidOperationException(),new COMException()}) {
            var state=RuntimeDependencies.Inspect(()=>throw error);
            Check(!state.WebView2Available && state.Note!.Contains("Central") && state.HelpUrl.StartsWith("https://developer.microsoft.com/"),"dependency detection failure is an actionable browser fallback, without raw errors");
        }
        Check(RuntimeDependencies.Inspect(()=>"143.1").WebView2Available,"later installed runtime can recover after a failed detection");
        Console.WriteLine("PASS: isolated WebView2 dependency checks handle missing/locked/wrong architecture/runtime errors and later recovery.");
    }
}
