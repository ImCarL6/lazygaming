param(
    [string]$TizenCli = $env:LAZYGAMING_TIZEN_CLI,
    [string]$SigningProfile = $env:LAZYGAMING_TIZEN_PROFILE,
    [string]$OutputDirectory,
    [switch]$StageOnly
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputDir = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'outputs' }
$stage = Join-Path $outputDir ('staging-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
if (!$StageOnly) {
    if (!$TizenCli) { $command = Get-Command tizen,tizen.bat -ErrorAction SilentlyContinue | Select-Object -First 1; if ($command) { $TizenCli = $command.Source } }
    if (!$TizenCli -or !(Test-Path -LiteralPath $TizenCli)) { throw 'Informe -TizenCli ou coloque a CLI Tizen no PATH.' }
    if (!$SigningProfile) { throw 'Informe -SigningProfile com um perfil local existente. Não copie certificados para o projeto.' }
}
foreach ($required in @('moonlight-wasm.js','moonlight-wasm.js.mem','moonlight-wasm.wasm','moonlight-wasm.worker.js')) {
    if (!(Test-Path -LiteralPath (Join-Path $projectRoot "tv/stream-engine/$required"))) { throw 'Prepare o motor com scripts/Obter-Motor.py antes de empacotar.' }
}
[IO.Directory]::CreateDirectory($stage) | Out-Null
foreach ($name in @('index.html','style.css','app.js','platform.js','presentation.js','crypto.js','config.xml','stream.js','stream-policy.js')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "tv/$name") -Destination $stage
}
foreach ($folder in @('stream-engine','assets')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "tv/$folder") -Destination $stage -Recurse
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE'),(Join-Path $projectRoot 'THIRD_PARTY.md') -Destination $stage
if ($StageOnly) { Write-Output "Staging sem assinatura: $stage"; return }
& $TizenCli package -t wgt -s $SigningProfile -o $outputDir -- $stage
if ($LASTEXITCODE -ne 0) { throw 'Falha ao empacotar/assinar.' }
Get-FileHash -LiteralPath (Join-Path $outputDir 'LazyGaming.wgt') -Algorithm SHA256
