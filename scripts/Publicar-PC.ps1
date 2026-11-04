param([string]$OutputDirectory, [switch]$SelfContained, [string]$RuntimeIdentifier = 'win-x64')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'outputs/pc-package' }
$target = [IO.Path]::GetFullPath($OutputDirectory)
$installed = [IO.Path]::GetFullPath((Join-Path $projectRoot 'dist/LazyGaming'))
if ($target.TrimEnd('\','/') -eq $installed.TrimEnd('\','/')) { throw 'Use uma pasta de preparação; substituir a instalação exige conferir sessão e processo separadamente.' }
foreach ($required in @('moonlight-wasm.js','moonlight-wasm.js.mem','moonlight-wasm.wasm','moonlight-wasm.worker.js')) {
    if (!(Test-Path -LiteralPath (Join-Path $projectRoot "tv/stream-engine/$required"))) { throw 'Prepare o motor com scripts/Obter-Motor.py antes de publicar.' }
}
Push-Location $projectRoot
try {
    $publishArgs = @('publish', 'src/LazyGaming.Host', '-c', 'Release', '--self-contained', $SelfContained.IsPresent.ToString().ToLowerInvariant(), '-o', $target, '-m:1', '-p:UseSharedCompilation=false')
    if ($SelfContained) { $publishArgs += @('-r', $RuntimeIdentifier) }
    dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar o PC.' }
    Copy-Item -LiteralPath LICENSE,THIRD_PARTY.md -Destination $target
    Set-Content -LiteralPath (Join-Path $target 'Abrir-LazyGaming.cmd') -Encoding Ascii -Value '@echo off', 'set "LAZYGAMING_APP=%~dp0LazyGaming.Host.exe"', 'powershell -NoProfile -Command "Start-Process -FilePath $env:LAZYGAMING_APP -ArgumentList ''--open-launcher'' -WindowStyle Hidden"'
    Set-Content -LiteralPath (Join-Path $target 'Central-LazyGaming.cmd') -Encoding Ascii -Value '@echo off', 'set "LAZYGAMING_APP=%~dp0LazyGaming.Host.exe"', 'powershell -NoProfile -Command "Start-Process -FilePath $env:LAZYGAMING_APP -ArgumentList ''--open-hub'' -WindowStyle Hidden"'
    Write-Output "Executável preparado: $target"
} finally { Pop-Location }
