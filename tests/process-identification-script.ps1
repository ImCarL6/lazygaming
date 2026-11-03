$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts\Identificar-Processo.ps1')
$taskSelf = [Diagnostics.Process]::GetCurrentProcess()
$taskImage = Get-LazyGamingProcessImage $taskSelf.Id
if ($taskImage -ne $taskSelf.MainModule.FileName) { throw 'Imagem do processo próprio incorreta.' }
$taskRejected = $false
try { Get-LazyGamingProcessImage 0 | Out-Null } catch { $taskRejected = $true }
if (!$taskRejected) { throw 'PID inválido deveria ser recusado.' }
# CIM pode enumerar um PID sem fornecer ExecutablePath; a imagem exata ainda é obrigatória.
function Get-CimInstance { @([pscustomobject]@{ProcessId=$taskSelf.Id; ExecutablePath=$null}) }
$taskVerified = Get-LazyGamingVerifiedHelper $taskImage
try { if ($taskVerified.Id -ne $taskSelf.Id) { throw 'Processo verificado incorreto.' } } finally { $taskVerified.Dispose() }
$taskRejected = $false
try { Get-LazyGamingVerifiedHelper ($taskImage + '.other') | Out-Null } catch { $taskRejected = $true }
if (!$taskRejected) { throw 'Outro executável não pode ser aceito.' }
function Get-CimInstance { @() }
$taskRejected = $false
try { Get-LazyGamingVerifiedHelper $taskImage | Out-Null } catch { $taskRejected = $true }
if (!$taskRejected) { throw 'Nenhum auxiliar deveria interromper a atualização.' }
function Get-CimInstance { @([pscustomobject]@{ProcessId=$taskSelf.Id}, [pscustomobject]@{ProcessId=$taskSelf.Id}) }
$taskRejected = $false
try { Get-LazyGamingVerifiedHelper $taskImage | Out-Null } catch { $taskRejected = $true }
if (!$taskRejected) { throw 'Mais de uma instância deveria interromper a atualização.' }
'PASS: consulta limitada real; CIM sem caminho; outro executável, PID inválido e zero/múltiplas instâncias recusados; nenhum processo encerrado.'
