$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskDirectory = Join-Path $taskRoot 'dist\LazyGaming'
$taskExecutable = Join-Path $taskDirectory 'LazyGaming.Host.exe'
if (!(Test-Path -LiteralPath $taskExecutable)) { throw 'Auxiliar publicado não encontrado.' }

# A tarefa só inicia o auxiliar; nunca abre painel, jogo ou uma segunda cópia.
function Test-TaskHelperRunning {
    return @(Get-CimInstance Win32_Process -Filter "Name='LazyGaming.Host.exe'" |
        Where-Object { $_.ExecutablePath -eq $taskExecutable }).Count -gt 0
}
if (Test-TaskHelperRunning) { exit 0 }

# O auxiliar fixa os endereços ao iniciar. Aguardar a rede física no logon.
$taskNetworkReady = $false
for ($taskAttempt = 0; $taskAttempt -lt 30; $taskAttempt++) {
    $taskNetworkReady = @([Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() | Where-Object {
        if ($_.OperationalStatus -ne [Net.NetworkInformation.OperationalStatus]::Up -or
            $_.NetworkInterfaceType -eq [Net.NetworkInformation.NetworkInterfaceType]::Loopback) { return $false }
        $taskProperties = $_.GetIPProperties()
        $taskLan = @($taskProperties.UnicastAddresses | Where-Object {
            $_.Address.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork -and $_.Address.ToString().StartsWith('192.168.')
        }).Count -gt 0
        $taskGateway = @($taskProperties.GatewayAddresses | Where-Object {
            $_.Address.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork -and $_.Address.ToString() -ne '0.0.0.0'
        }).Count -gt 0
        $taskLan -and $taskGateway
    }).Count -gt 0
    if ($taskNetworkReady) { break }
    Start-Sleep -Seconds 2
}
if (!$taskNetworkReady) { throw 'Rede local ainda indisponível. A tarefa tentará novamente.' }
if (Test-TaskHelperRunning) { exit 0 }

[Diagnostics.Process]::GetCurrentProcess().PriorityClass = [Diagnostics.ProcessPriorityClass]::Normal
$taskProcess = Start-Process -FilePath $taskExecutable -ArgumentList '--lan' -WorkingDirectory $taskDirectory -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $taskRoot 'outputs\autostart-host.log') `
    -RedirectStandardError (Join-Path $taskRoot 'outputs\autostart-host-error.log')
$null = $taskProcess.Handle
$taskProcess.WaitForExit()
exit $taskProcess.ExitCode
