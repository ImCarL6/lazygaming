using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

internal static class RecuperarConexao
{
    [STAThread]
    private static int Main()
    {
        try
        {
            string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ".."));
            string script = Path.Combine(root, "scripts", "Recuperar-Conexao.ps1");
            if (!File.Exists(script)) throw new FileNotFoundException("A recuperação do LazyGaming não foi encontrada.");
            var info = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -File \"" + script + "\"",
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (var process = Process.Start(info))
            {
                if (process == null) return 2;
                process.WaitForExit();
                return process.ExitCode;
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "LazyGaming - Conexão", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }
}
