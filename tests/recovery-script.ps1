# Testa somente funções e pré-condições em dados artificiais; nunca inicia/encerra auxiliar.
$ErrorActionPreference = 'Stop'
$taskTestRoot = Split-Path -Parent $PSScriptRoot
$taskSource = Join-Path $taskTestRoot 'scripts\Recuperar-Conexao.ps1'
$taskSourceText = Get-Content -LiteralPath $taskSource -Raw -Encoding UTF8
$taskTokens = $null; $taskParseErrors = $null
$taskAst = [Management.Automation.Language.Parser]::ParseFile($taskSource, [ref]$taskTokens, [ref]$taskParseErrors)
if ($taskParseErrors.Count) { throw 'Sintaxe inválida.' }
foreach ($taskFunction in $taskAst.EndBlock.Statements | Where-Object {
    $_ -is [Management.Automation.Language.FunctionDefinitionAst] -and
    $_.Name -in @('Get-ConnectionSignature', 'Test-Connection')
}) { Invoke-Expression $taskFunction.Extent.Text }
$taskPreflight = [regex]::Match($taskSourceText, '(?s)    \$taskHashes = @\{\}.*?(?=    \$taskRestarted =)')
if (!$taskPreflight.Success) { throw 'Pré-condições não encontradas.' }
$taskData = Join-Path $taskTestRoot ('outputs\recovery-tests-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $taskData
$taskPair = @{ Identity = ('1' * 32); Key = ('2' * 64) }
$taskRecovery = @{ Id = ('3' * 32); Key = ('4' * 64) }
$taskPair | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskData 'pairing.json') -Encoding UTF8
$script:RejectStatus = $false; $script:WrongProof = $false; $script:WrongRecovery = $false
$script:StatusCalls = 0; $script:RecoveryCalls = 0
function Get-ConnectionStatus {
    $script:StatusCalls++
    if ($script:RejectStatus) { throw 'HTTP401 simulado' }
    return @{ state = @{ games = @(); environment = @{active = $false} } }
}
function Read-ConnectionJson([string]$Url) {
    $challenge = [regex]::Match($Url, 'challenge=([^&]+)').Groups[1].Value
    if ($Url.Contains('/api/recovery/discover')) {
        $script:RecoveryCalls++
        $proof = Get-ConnectionSignature "recovery-discover`n$challenge`n$($taskPair.Identity)`n$($taskRecovery.Id)" $taskRecovery.Key
        if ($script:WrongRecovery) { $proof = 'invalid' }
        return @{ available = $true; identity = $taskPair.Identity; proof = $proof }
    }
    $proof = Get-ConnectionSignature "discover`n$challenge`n$($taskPair.Identity)" $taskPair.Key
    if ($script:WrongProof) { $proof = 'invalid' }
    return @{ identity = $taskPair.Identity; proof = $proof }
}
Invoke-Expression $taskPreflight.Value
if ($taskLinkPresence['tv-recovery.json'] -or $taskHashes.Count -ne 1) { throw 'Reserva ausente não foi preservada como ausência.' }
if (!(Test-Connection @('192.168.0.10')) -or $script:StatusCalls -ne 1 -or $script:RecoveryCalls) { throw 'Pareamento principal válido sem reserva deveria passar.' }
$script:RejectStatus = $true
if (Test-Connection @('192.168.0.10')) { throw 'HTTP401 não pode ser aceito sem reserva.' }
$script:RejectStatus = $false; $script:WrongProof = $true
if (Test-Connection @('192.168.0.10')) { throw 'Prova principal incorreta não pode passar.' }
$script:WrongProof = $false
$taskRecovery | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskData 'tv-recovery.json') -Encoding UTF8
Invoke-Expression $taskPreflight.Value
if (!$taskLinkPresence['tv-recovery.json'] -or $taskHashes.Count -ne 2) { throw 'Reserva presente precisa ser protegida.' }
if (!(Test-Connection @('192.168.0.10'))) { throw 'Vínculos válidos deveriam passar.' }
$script:WrongRecovery = $true
if (Test-Connection @('192.168.0.10')) { throw 'Reserva existente com prova inválida não pode passar.' }
Set-Content -LiteralPath (Join-Path $taskData 'tv-recovery.json') -Value '{}' -Encoding UTF8
$taskRejected = $false
try { Invoke-Expression $taskPreflight.Value } catch { $taskRejected = $true }
if (!$taskRejected) { throw 'Reserva existente inválida deveria ser recusada antes de reiniciar.' }
$taskData = Join-Path $taskData 'empty'
$null = New-Item -ItemType Directory -Path $taskData
$taskRejected = $false
try { Invoke-Expression $taskPreflight.Value } catch { $taskRejected = $true }
if (!$taskRejected) { throw 'Vínculo principal ausente deveria ser recusado antes de reiniciar.' }
'PASS: reserva opcional; principal/HMAC/status obrigatórios; reserva existente protegida; credenciais ausentes/inválidas recusadas.'
