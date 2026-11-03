param([switch]$Silent)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskExe = Join-Path $taskRoot 'dist\LazyGaming\LazyGaming.Host.exe'
$taskData = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'LazyGaming'
$taskMutex = New-Object Threading.Mutex($false, 'Local\LazyGaming.ConnectionRepair')
$taskLocked = $false

function Get-ConnectionAddresses {
    @([Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() | Where-Object {
        $_.OperationalStatus -eq [Net.NetworkInformation.OperationalStatus]::Up -and
        $_.NetworkInterfaceType -ne [Net.NetworkInformation.NetworkInterfaceType]::Loopback -and
        @($_.GetIPProperties().GatewayAddresses | Where-Object { $_.Address.ToString() -ne '0.0.0.0' }).Count -gt 0
    } | ForEach-Object { $_.GetIPProperties().UnicastAddresses } | Where-Object {
        $_.Address.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork -and $_.Address.ToString().StartsWith('192.168.')
    } | ForEach-Object { $_.Address.ToString() } | Sort-Object -Unique)
}
function Get-ConnectionSignature([string]$Text, [string]$Key) {
    $bytes = New-Object byte[] ($Key.Length / 2)
    for ($i = 0; $i -lt $bytes.Length; $i++) { $bytes[$i] = [Convert]::ToByte($Key.Substring($i * 2, 2), 16) }
    $mac = New-Object Security.Cryptography.HMACSHA256
    $mac.Key = $bytes
    try { ([BitConverter]::ToString($mac.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)))).Replace('-', '').ToLowerInvariant() }
    finally { $mac.Dispose() }
}
function Read-ConnectionJson([string]$Url, [hashtable]$Headers = @{}) {
    # Sem proxy: somente o PC local nos endereços enumerados pelo Windows.
    $request = [Net.HttpWebRequest]::Create($Url)
    $request.Proxy = $null
    $request.Timeout = 2500
    foreach ($name in $Headers.Keys) { $request.Headers[$name] = $Headers[$name] }
    $response = $request.GetResponse()
    try {
        $reader = New-Object IO.StreamReader($response.GetResponseStream())
        try { $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    } finally { $response.Dispose() }
}
function Get-ConnectionStatus {
    $pair = Get-Content -LiteralPath (Join-Path $taskData 'pairing.json') -Raw | ConvertFrom-Json
    $time = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds().ToString()
    $nonce = [guid]::NewGuid().ToString('N')
    $signature = Get-ConnectionSignature "GET`n/api/status`n$time`n$nonce`n" $pair.Key
    Read-ConnectionJson 'http://127.0.0.1:47831/api/status' @{'X-LG-Time' = $time; 'X-LG-Nonce' = $nonce; 'X-LG-Signature' = $signature}
}
function Test-Connection([string[]]$Addresses) {
    try {
        $pair = Get-Content -LiteralPath (Join-Path $taskData 'pairing.json') -Raw | ConvertFrom-Json
        $recovery = $null
        if (Test-Path -LiteralPath (Join-Path $taskData 'tv-recovery.json') -PathType Leaf) {
            $recovery = Get-Content -LiteralPath (Join-Path $taskData 'tv-recovery.json') -Raw -ErrorAction Stop | ConvertFrom-Json
        }
        foreach ($address in $Addresses) {
            $challenge = [guid]::NewGuid().ToString('N')
            $base = 'http://' + $address + ':47831'
            $proof = Read-ConnectionJson ($base + '/api/discover?challenge=' + $challenge)
            if ($proof.identity -ne $pair.Identity -or $proof.proof -ne (Get-ConnectionSignature "discover`n$challenge`n$($pair.Identity)" $pair.Key)) { return $false }
            if ($recovery) {
                $proof = Read-ConnectionJson ($base + '/api/recovery/discover?id=' + $recovery.Id + '&challenge=' + $challenge)
                if (!$proof.available -or $proof.identity -ne $pair.Identity -or $proof.proof -ne (Get-ConnectionSignature "recovery-discover`n$challenge`n$($pair.Identity)`n$($recovery.Id)" $recovery.Key)) { return $false }
            }
        }
        $null = Get-ConnectionStatus
        return $true
    } catch { return $false }
}
function Assert-NoGameForConnectionRepair {
    foreach ($name in @('session.json', 'transport-session.json')) {
        if (Test-Path -LiteralPath (Join-Path $taskData $name)) { throw 'Existe uma sessão de jogo. Encerre-a pelo LazyGaming antes de recuperar a conexão.' }
    }
    try { $status = Get-ConnectionStatus } catch { $status = $null }
    if ($status -and ($status.state.environment.active -or @($status.state.games | Where-Object { $_.running -or $_.starting }).Count)) { throw 'Existe um jogo aberto. Encerre-o pelo LazyGaming antes de recuperar a conexão.' }
    $library = & $taskExe --inspect-library | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or !$library.games) { throw 'Não foi possível conferir os jogos; nenhuma alteração foi feita.' }
    $paths = @($library.games | ForEach-Object { $_.ProcessPath })
    if (@(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $paths -contains $_.ExecutablePath }).Count) { throw 'Existe um jogo aberto. Feche o jogo antes de recuperar a conexão.' }
}

try {
    $taskLocked = $taskMutex.WaitOne(0)
    if (!$taskLocked) { throw 'A recuperação já está em andamento.' }
    if (!(Test-Path -LiteralPath $taskExe)) { throw 'O auxiliar do LazyGaming não foi encontrado.' }
    $taskAddresses = @(Get-ConnectionAddresses)
    if (!$taskAddresses.Count) { throw 'O PC ainda não está conectado à rede local. Conecte a rede e tente novamente.' }
    $taskHashes = @{}
    $taskLinkPresence = @{}
    foreach ($taskName in @('pairing.json', 'tv-recovery.json')) {
        $taskLinkPath = Join-Path $taskData $taskName
        $taskLinkPresence[$taskName] = Test-Path -LiteralPath $taskLinkPath -PathType Leaf
        if (!$taskLinkPresence[$taskName]) {
            if ($taskName -eq 'pairing.json') { throw 'O vínculo principal está ausente; nenhuma tentativa de reinício foi feita.' }
            continue # Reserva opcional: não criar nem substituir vínculo por inferência.
        }
        $taskCredential = Get-Content -LiteralPath $taskLinkPath -Raw -ErrorAction Stop | ConvertFrom-Json
        $taskCredentialId = if ($taskName -eq 'pairing.json') { $taskCredential.Identity } else { $taskCredential.Id }
        if ($taskCredentialId -notmatch '^[a-fA-F0-9]{32}$' -or $taskCredential.Key -notmatch '^[a-fA-F0-9]{64}$') { throw 'Um vínculo salvo está inválido; nenhuma tentativa de reinício foi feita.' }
        $taskHashes[$taskName] = (Get-FileHash -LiteralPath $taskLinkPath -ErrorAction Stop).Hash
    }
    $taskRestarted = $false
    if (!(Test-Connection $taskAddresses)) {
        Assert-NoGameForConnectionRepair
        $taskHosts = @(Get-CimInstance Win32_Process -Filter "Name='LazyGaming.Host.exe'")
        if (@($taskHosts | Where-Object { $_.ExecutablePath -ne $taskExe }).Count -or $taskHosts.Count -gt 1) { throw 'Há outra instância do auxiliar. Nenhuma foi encerrada.' }
        $taskListeners = @(Get-NetTCPConnection -LocalPort 47831 -State Listen -ErrorAction SilentlyContinue)
        if (@($taskListeners | Where-Object { $_.OwningProcess -notin @($taskHosts.ProcessId) }).Count) { throw 'Outro aplicativo está usando a conexão do LazyGaming.' }
        if ($taskHosts.Count -eq 1) {
            $taskProcess = Get-Process -Id $taskHosts[0].ProcessId
            if ($taskProcess.Path -ne $taskExe) { throw 'O processo mudou; nenhuma alteração foi feita.' }
            $taskProcess.Kill()
            if (!$taskProcess.WaitForExit(10000)) { throw 'O auxiliar ainda não terminou. Aguarde antes de tentar novamente.' }
        }
        # Não instala arquivos, não refaz vínculos nem altera a tarefa de logon.
        if (@(Get-CimInstance Win32_Process -Filter "Name='LazyGaming.Host.exe'").Count -eq 0) {
            $null = Start-Process -FilePath $taskExe -ArgumentList '--lan' -WorkingDirectory (Split-Path $taskExe) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskRoot 'outputs\connection-host.log') -RedirectStandardError (Join-Path $taskRoot 'outputs\connection-host-error.log')
        }
        $taskRestarted = $true
        $taskHealthy = $false
        for ($taskAttempt = 0; $taskAttempt -lt 8; $taskAttempt++) {
            Start-Sleep -Seconds 1
            if (Test-Connection $taskAddresses) { $taskHealthy = $true; break }
        }
        if (!$taskHealthy) { throw 'O auxiliar não confirmou a conexão. Os vínculos foram preservados; consulte o diagnóstico do LazyGaming.' }
    }
    foreach ($taskName in $taskLinkPresence.Keys) {
        $taskLinkPath = Join-Path $taskData $taskName
        if ($taskLinkPresence[$taskName] -ne (Test-Path -LiteralPath $taskLinkPath -PathType Leaf)) { throw 'A presença de um vínculo mudou durante a conferência; confira a conexão na TV.' }
        if ($taskLinkPresence[$taskName] -and $taskHashes[$taskName] -ne (Get-FileHash -LiteralPath $taskLinkPath -ErrorAction Stop).Hash) { throw 'O vínculo mudou durante a conferência. Tente Recuperar conexão na própria TV.' }
    }
    $taskUrls = @($taskAddresses | ForEach-Object { 'http://' + $_ + ':47831' })
    $taskMessage = "Conexão do PC pronta.`n`nEndereço atual:`n" + ($taskUrls -join "`n") + "`n`nNa TV, use Buscar PC. Se necessário, informe este endereço e escolha Recuperar conexão."
    if (!$taskLinkPresence['tv-recovery.json']) {
        $taskMessage = "Conexão do PC pronta.`n`nEndereço atual:`n" + ($taskUrls -join "`n") + "`n`nPareamento principal local validado. O vínculo de reserva da TV está ausente neste perfil.`n`nSe Buscar PC e Recuperar conexão falharem na TV, use o endereço acima e conecte pelo campo Código. Gere o código abrindo http://127.0.0.1:47831 no PC. Este atalho verifica o auxiliar; não refaz o pareamento da TV."
    }
    @{ok = $true; restarted = $taskRestarted; recoveryPresent = $taskLinkPresence['tv-recovery.json']; urls = $taskUrls; checkedAt = [DateTimeOffset]::Now.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskData 'connection-repair.json') -Encoding UTF8
    if ($Silent) { Write-Output $taskMessage } else { $null = (New-Object -ComObject WScript.Shell).Popup($taskMessage, 0, 'LazyGaming - Conexão', 64) }
} catch {
    if ($Silent) { throw }
    $null = (New-Object -ComObject WScript.Shell).Popup($_.Exception.Message, 0, 'LazyGaming - Conexão', 48)
    exit 1
} finally {
    if ($taskLocked) { $taskMutex.ReleaseMutex() }
    $taskMutex.Dispose()
}
