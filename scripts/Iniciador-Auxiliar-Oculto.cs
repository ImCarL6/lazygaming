using System;
using System.Diagnostics;
using System.IO;

// Aplicativo Windows sem console: impede que o terminal padrão crie uma janela
// antes que o PowerShell consiga aplicar -WindowStyle Hidden.
internal static class IniciadorAuxiliarOculto
{
    private static int Main()
    {
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ".."));
        try
        {
            string script = Path.Combine(root, "scripts", "Iniciar-Auxiliar-Oculto.ps1");
            if (!File.Exists(script)) return 2;
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell", "v1.0", "powershell.exe"),
                Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -File \"" + script + "\"",
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (var child = Process.Start(start))
            {
                if (child == null) return 3;
                child.WaitForExit();
                return child.ExitCode;
            }
        }
        catch (Exception error)
        {
            try
            {
                File.AppendAllText(Path.Combine(root, "outputs", "autostart-launcher-error.log"),
                    DateTimeOffset.Now.ToString("o") + " " + error.GetType().Name + ": " + error.Message + Environment.NewLine);
            }
            catch { }
            return 1;
        }
    }
}
