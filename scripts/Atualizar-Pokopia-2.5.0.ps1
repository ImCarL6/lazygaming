param([switch]$Apply, [switch]$PresentationFix, [switch]$TaskbarFix)
$ErrorActionPreference = 'Stop'
if ($PresentationFix -and $TaskbarFix) { throw 'Selecione somente uma publicação para atualizar.' }
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskOutput = Join-Path $taskRoot 'outputs\pokopia-2.5.0'
$taskPackage = Join-Path $taskOutput $(if ($TaskbarFix) { 'pc-taskbar-fix' } elseif ($PresentationFix) { 'pc-presentation-fix' } else { 'pc' })
$taskExpectedPackageHash = if ($TaskbarFix) { '3BBA55DEA6297B10322FEDEB9CF7F663AF91BA48A9C169D6FAF28FF0F620A6FA' } elseif ($PresentationFix) { 'B24A12F8DF861B03217A92B1FA18887CAF1358B132B30C239011911E8F264D59' } else { 'F0D68D9BEA59EF9AEF574D90EB7872BAAD0D271FFEB3007BA8CA10786FFFB815' }
$taskTarget = Join-Path $taskRoot 'dist\LazyGaming'
$taskExe = Join-Path $taskTarget 'LazyGaming.Host.exe'
$taskData = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'LazyGaming'
$taskLibraryPath = Join-Path $taskData 'library'
$taskManifestPath = Join-Path $taskOutput 'pokopia-local.json'
$taskManifest = Get-Content -LiteralPath $taskManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$taskIdentity = 'local-140985860802'
Set-Location $taskRoot
. (Join-Path $PSScriptRoot 'Identificar-Processo.ps1')

function Get-PokopiaUpdateIdle {
    $state = & python (Join-Path $taskOutput 'validar.py') | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or !$state.state.games) { throw 'O auxiliar não confirmou o estado autenticado.' }
    if ($state.state.environment.active -or $state.state.environment.audioPending -or $state.state.environment.restoreFailed -or
        @($state.state.games | Where-Object { $_.running -or $_.starting }).Count -or
        (Test-Path -LiteralPath (Join-Path $taskData 'session.json')) -or
        (Test-Path -LiteralPath (Join-Path $taskData 'transport-session.json')) -or
        @(Get-CimInstance Win32_Process -Filter "Name='eglantine.exe'" -ErrorAction Stop).Count) {
        throw 'Feche o jogo normalmente e aguarde a restauração da sessão antes de atualizar.'
    }
    return $state
}

$taskShortcut = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $env:USERPROFILE 'pokopia\Jogar Pokopia 2.5.0 PT-BR.lnk'))
$taskExpectedArguments = @(('-g "' + $taskManifest.ContentPath + '"'), ('-f -g "' + $taskManifest.ContentPath + '"'))
if ($taskShortcut.TargetPath -ne $taskManifest.Executable -or $taskShortcut.WorkingDirectory -ne $taskManifest.WorkingDirectory -or
    $taskShortcut.Arguments -notin $taskExpectedArguments) {
    throw 'O atalho mudou desde a preparação; confira o destino antes de atualizar.'
}
if ((Get-FileHash -LiteralPath (Join-Path $taskPackage 'LazyGaming.Host.dll') -ErrorAction Stop).Hash -ne $taskExpectedPackageHash) { throw 'Pacote preparado diferente do testado.' }
$taskAllowedInstalledHashes = @('35E7341EAE5ABADED326A4EB2659A7B7935A231159B09691C0F4C76F256B1D47', 'F0D68D9BEA59EF9AEF574D90EB7872BAAD0D271FFEB3007BA8CA10786FFFB815', $taskExpectedPackageHash)
if ($TaskbarFix) { $taskAllowedInstalledHashes += 'B24A12F8DF861B03217A92B1FA18887CAF1358B132B30C239011911E8F264D59' }
if ((Get-FileHash -LiteralPath (Join-Path $taskTarget 'LazyGaming.Host.dll') -ErrorAction Stop).Hash -notin $taskAllowedInstalledHashes) { throw 'A instalação mudou; não substituir automaticamente.' }
$taskStatus = Get-PokopiaUpdateIdle
if (@($taskStatus.state.games | Where-Object id -eq $taskIdentity).Count -ne 1) { throw 'A entrada anterior do Pokopia não é única.' }
$taskFreshLibrary = & $taskExe --inspect-library | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or @($taskFreshLibrary.games | Where-Object Id -eq $taskIdentity).Count -ne 1) { throw 'A descoberta atual não confirmou a entrada anterior.' }
$taskProcess = Get-LazyGamingVerifiedHelper $taskExe
$taskStart = $taskProcess.StartTime
if (!$Apply) {
    @{diagnosticOnly=$true; installed=$false; games=@($taskStatus.state.games).Count; replacement=$taskIdentity; emulator=$taskManifest.Executable; content=$taskManifest.ContentPath} | ConvertTo-Json
    return
}

& python (Join-Path $taskOutput 'validar.py') --snapshot-only
if ($LASTEXITCODE -ne 0) { throw 'Não foi possível registrar os arquivos protegidos.' }
$taskBackup = Join-Path $taskOutput ('before-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$null = New-Item -ItemType Directory -Path $taskBackup
Copy-Item -LiteralPath $taskTarget -Destination (Join-Path $taskBackup 'host') -Recurse -ErrorAction Stop
Copy-Item -LiteralPath $taskLibraryPath -Destination (Join-Path $taskBackup 'library') -Recurse -ErrorAction Stop
$taskStatus | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $taskOutput 'baseline-status.json') -Encoding UTF8
$taskFreshLibrary | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $taskOutput 'baseline-library.json') -Encoding UTF8
$taskLinks = @{}
foreach ($taskName in @('pairing.json','tv-recovery.json')) {
    $taskPath = Join-Path $taskData $taskName
    if (Test-Path -LiteralPath $taskPath -PathType Leaf) {
        $taskLinks[$taskName] = (Get-FileHash -LiteralPath $taskPath -ErrorAction Stop).Hash
        Copy-Item -LiteralPath $taskPath -Destination $taskBackup -ErrorAction Stop
    } elseif ($taskName -eq 'pairing.json') { throw 'Vínculo principal ausente; auxiliar não encerrado.' }
}
$null = Get-PokopiaUpdateIdle
if ($taskProcess.HasExited -or (Get-LazyGamingProcessImage $taskProcess.Id) -ne $taskExe -or
    (Get-Process -Id $taskProcess.Id -ErrorAction Stop).StartTime -ne $taskStart) { throw 'O auxiliar mudou durante o backup; atualização cancelada.' }
foreach ($taskName in $taskLinks.Keys) {
    if ((Get-FileHash -LiteralPath (Join-Path $taskData $taskName) -ErrorAction Stop).Hash -ne $taskLinks[$taskName]) { throw 'Vínculo mudou durante o backup; atualização cancelada.' }
}
$taskStopped = $false
$taskNewProcess = $null
try {
    $taskProcess.Kill(); $taskStopped = $true
    if (!$taskProcess.WaitForExit(10000)) { throw 'O auxiliar ainda não terminou; arquivos não substituídos.' }
    Copy-Item -Path (Join-Path $taskPackage '*') -Destination $taskTarget -Recurse -Force -ErrorAction Stop
    & $taskExe --replace-local-game $taskIdentity $taskManifestPath
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao substituir a ficha local.' }
    foreach ($taskFile in Get-ChildItem -LiteralPath $taskPackage -Recurse -File) {
        $taskRelative = $taskFile.FullName.Substring($taskPackage.Length + 1)
        if ((Get-FileHash -LiteralPath $taskFile.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $taskTarget $taskRelative)).Hash) { throw 'Pacote instalado diferente da preparação.' }
    }
    foreach ($taskName in $taskLinks.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $taskData $taskName) -ErrorAction Stop).Hash -ne $taskLinks[$taskName]) { throw 'O vínculo mudou; confira antes de iniciar o auxiliar.' }
    }
    $taskNewProcess = Start-Process -FilePath $taskExe -ArgumentList '--lan' -WorkingDirectory $taskTarget -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskOutput 'host.log') -RedirectStandardError (Join-Path $taskOutput 'host-error.log')
    & python (Join-Path $taskOutput 'validar.py') --installed
    if ($LASTEXITCODE -ne 0) { throw 'A conferência da instalação falhou.' }
} catch {
    $taskFailure = $_
    if (!$taskStopped) { throw }
    if (@(Get-CimInstance Win32_Process -Filter "Name='eglantine.exe'" -ErrorAction Stop).Count -or
        (Test-Path -LiteralPath (Join-Path $taskData 'session.json')) -or (Test-Path -LiteralPath (Join-Path $taskData 'transport-session.json'))) { throw 'Há jogo ou sessão após a falha; restauração interrompida sem encerrar jogo.' }
    if ($taskNewProcess -and !$taskNewProcess.HasExited) {
        $null = Get-PokopiaUpdateIdle
        if ((Get-LazyGamingProcessImage $taskNewProcess.Id) -ne $taskExe) { throw 'Identidade mudou; restauração interrompida.' }
        $taskNewProcess.Kill()
        if (!$taskNewProcess.WaitForExit(10000)) { throw 'O auxiliar não terminou; restauração interrompida.' }
    }
    if (@(Get-CimInstance Win32_Process -Filter "Name='LazyGaming.Host.exe'" -ErrorAction Stop).Count) { throw 'Há auxiliar ativo; restauração interrompida.' }
    foreach ($taskPair in @(@($taskTarget,'host'),@($taskLibraryPath,'library'))) {
        $taskPath = [IO.Path]::GetFullPath($taskPair[0])
        $taskArchive = [IO.Path]::GetFullPath((Join-Path $taskBackup ($taskPair[1] + '-after-failure')))
        if ($taskPath -notin @([IO.Path]::GetFullPath($taskTarget),[IO.Path]::GetFullPath($taskLibraryPath)) -or
            !$taskArchive.StartsWith([IO.Path]::GetFullPath($taskBackup) + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Destino de restauração inválido.' }
        Move-Item -LiteralPath $taskPath -Destination $taskArchive -ErrorAction Stop
        Copy-Item -LiteralPath (Join-Path $taskBackup $taskPair[1]) -Destination $taskPath -Recurse -ErrorAction Stop
    }
    $null = Start-Process -FilePath $taskExe -ArgumentList '--lan' -WorkingDirectory $taskTarget -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskOutput 'rollback-host.log') -RedirectStandardError (Join-Path $taskOutput 'rollback-host-error.log')
    throw $taskFailure
}
if ($PresentationFix) { Write-Output 'Correção de restauração/foco instalada no auxiliar. Teste o Pokopia pela TV.' }
else { Write-Output 'Pokopia atualizado para Eglantine 2.5.0/NUP traduzido na biblioteca compartilhada. Nenhum jogo foi aberto.' }
Write-Output "Backup: $taskBackup"
