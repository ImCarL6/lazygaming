$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $env:TEMP ('lg-installer-hash-' + [guid]::NewGuid().ToString('N'))
$content = Join-Path $testRoot 'payload/LazyGaming'
New-Item -ItemType Directory -Path $content -Force | Out-Null
try {
    $exe = Join-Path $content 'LazyGaming.Host.exe'
    [IO.File]::WriteAllText($exe, 'fixture never executed')
    $sha = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    @{ version = '0.25.1'; files = @(@{ path = 'LazyGaming.Host.exe'; sha256 = $sha }) } | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath (Join-Path $content 'release.json') -Encoding UTF8
    $archive = Join-Path $testRoot 'payload.zip'
    Compress-Archive -LiteralPath $content -DestinationPath $archive
    $wrapper = Join-Path $testRoot 'verify.ps1'
    @'
param($Installer, $Package, $Root)
function Get-FileHash { throw 'Get-FileHash indisponivel neste ambiente de instalacao' }
& $Installer -Package $Package -Root $Root -SkipIntegration -Quiet
exit $LASTEXITCODE
'@ | Set-Content -LiteralPath $wrapper -Encoding UTF8
    $installer = Join-Path $projectRoot 'scripts/Instalar-LazyGaming.ps1'
    $installRoot = Join-Path $testRoot 'installed'
    & "$env:WINDIR/System32/WindowsPowerShell/v1.0/powershell.exe" -NoProfile -File $wrapper -Installer $installer -Package $archive -Root $installRoot
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath (Join-Path $installRoot 'LazyGaming/LazyGaming.Host.exe'))) { throw 'Instalacao sem Get-FileHash falhou.' }
    # Corrupção deve continuar recusada antes de trocar a instalação existente.
    [IO.File]::WriteAllText($exe, 'corrupted fixture')
    Remove-Item -LiteralPath $archive
    Compress-Archive -LiteralPath $content -DestinationPath $archive
    & "$env:WINDIR/System32/WindowsPowerShell/v1.0/powershell.exe" -NoProfile -File $wrapper -Installer $installer -Package $archive -Root $installRoot
    if ($LASTEXITCODE -ne 1) { throw 'Pacote corrompido foi aceito.' }
    if ((Get-FileHash -LiteralPath (Join-Path $installRoot 'LazyGaming/LazyGaming.Host.exe') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sha) { throw 'Instalacao anterior foi modificada.' }
    Write-Output 'PASS: instalador funciona sem Get-FileHash e recusa corrupção antes de alterar a instalação.'
} finally {
    $absolute = [IO.Path]::GetFullPath($testRoot)
    if (!$absolute.StartsWith([IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($absolute) -notlike 'lg-installer-hash-*') { throw 'Pasta de teste inesperada.' }
    Remove-Item -LiteralPath $absolute -Recurse -Force
}
