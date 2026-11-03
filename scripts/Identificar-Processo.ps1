# Consulta somente a imagem do processo com PROCESS_QUERY_LIMITED_INFORMATION.
# Não solicita leitura de memória, término, elevação ou acesso total ao processo.
if (!('LazyGamingProcessQueryLimited' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
public static class LazyGamingProcessQueryLimited
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr handle, uint flags, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
    public static string Image(int processId)
    {
        if (processId <= 0) throw new ArgumentOutOfRangeException("processId");
        IntPtr handle = OpenProcess(0x1000, false, processId);
        if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            uint size = 32768;
            var path = new StringBuilder((int)size);
            if (!QueryFullProcessImageName(handle, 0, path, ref size))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return path.ToString();
        } finally { CloseHandle(handle); }
    }
}
'@ -ErrorAction Stop
}

function Get-LazyGamingProcessImage([int]$TargetProcessId) {
    [LazyGamingProcessQueryLimited]::Image($TargetProcessId)
}

function Get-LazyGamingVerifiedHelper([string]$ExpectedPath) {
    $candidates = @(Get-CimInstance Win32_Process -Filter "Name='LazyGaming.Host.exe'" -ErrorAction Stop)
    if ($candidates.Count -ne 1) { throw "Foram encontrados $($candidates.Count) auxiliares. A atualização exige exatamente um." }
    $process = Get-Process -Id $candidates[0].ProcessId -ErrorAction Stop
    try {
        if ((Get-LazyGamingProcessImage $process.Id) -ne $ExpectedPath) { throw 'O auxiliar pertence a outra instalação; nenhuma instância foi encerrada.' }
        return $process
    } catch { $process.Dispose(); throw }
}
