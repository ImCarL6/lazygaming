using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LazyGaming;

// One native chooser at a time, independent from library mutations.
public sealed class LocalFilePicker
{
    readonly SemaphoreSlim gate = new(1, 1);
    readonly Func<string, CancellationToken, Task<string?>> show;
    public LocalFilePicker(Func<string, CancellationToken, Task<string?>>? show = null) { this.show = show ?? Show; }

    public async Task<string?> Pick(string kind, CancellationToken cancellation = default)
    {
        if (!await gate.WaitAsync(0, cancellation)) throw new InvalidOperationException("Já existe uma seleção aberta. Conclua ou cancele essa seleção antes de tentar novamente.");
        try { return await show(kind, cancellation); }
        finally { gate.Release(); }
    }

    static Task<string?> Show(string kind, CancellationToken cancellation)
    {
        var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => {
            try {
                cancellation.ThrowIfCancellationRequested();
                // A visible owner gives the chooser a taskbar entry even if Windows refuses foreground activation.
                using var owner = new Form { Text = "LazyGaming · Escolher arquivo", TopMost = true, ShowInTaskbar = true,
                    StartPosition = FormStartPosition.CenterScreen, Size = new System.Drawing.Size(440, 130),
                    FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
                owner.Controls.Add(new Label { Text = "Conclua a seleção na janela do Windows ou cancele pela Central.", Dock = DockStyle.Fill, Padding = new Padding(16), AutoSize = false });
                owner.Show(); owner.Activate();
                using var timer = new System.Windows.Forms.Timer { Interval = 100 };
                timer.Tick += (_, _) => {
                    if (!cancellation.IsCancellationRequested) return;
                    // Only close the popup owned by this picker; never target another app's dialog.
                    var popup = GetLastActivePopup(owner.Handle);
                    if (popup != 0 && popup != owner.Handle) PostMessage(popup, 0x0010, 0, 0);
                };
                timer.Start();
                string? path;
                if (kind == "folder") {
                    using var dialog = new FolderBrowserDialog { Description = "Escolha a pasta dos jogos", UseDescriptionForTitle = true };
                    path = dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : null;
                } else {
                    using var dialog = new OpenFileDialog { Title = "LazyGaming · Escolher arquivo", CheckFileExists = true, Multiselect = false,
                        Filter = kind switch { "executable" => "Jogo (*.exe)|*.exe", "manifest" => "Ficha de jogo (*.json)|*.json", "image" => "Imagens|*.jpg;*.jpeg;*.png;*.webp", _ => "Conteúdo do jogo|*.nup;main" } };
                    path = dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
                }
                cancellation.ThrowIfCancellationRequested();
                result.TrySetResult(path);
            } catch (OperationCanceledException) { result.TrySetCanceled(cancellation); }
            catch (Exception error) { result.TrySetException(error); }
        }) { IsBackground = true, Name = "LazyGaming file picker" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return result.Task;
    }

    [DllImport("user32.dll")] static extern nint GetLastActivePopup(nint owner);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
