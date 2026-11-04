$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path $env:TEMP ('lg-install-diagnostics-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    $source = [IO.File]::ReadAllText((Join-Path $repo 'scripts/Instalar-LazyGaming.ps1'))
    $start = $source.IndexOf('function InstallDiagnostic(')
    $end = $source.IndexOf("InstallDiagnostic 'started'")
    . ([ScriptBlock]::Create($source.Substring($start, $end - $start)))
    $diagnosticFile = Join-Path $scratch 'diagnostics/installer.jsonl'
    $installStage = 'checking-integrity'
    InstallDiagnostic 'started'
    InstallDiagnostic 'failed' ([IO.IOException]::new('token=PrivateInstallerToken C:\Users\PrivateName\package.zip'))
    $rows = @(Get-Content -LiteralPath $diagnosticFile | ForEach-Object { $_ | ConvertFrom-Json })
    if ($rows.Count -ne 2 -or $rows[1].stage -ne 'checking-integrity' -or $rows[1].errorType -ne 'System.IO.IOException') { throw 'Etapa/erro do instalador nao registrados.' }
    if ([IO.File]::ReadAllText($diagnosticFile) -match 'PrivateInstallerToken|PrivateName|package.zip') { throw 'Diagnostico do instalador expôs dado privado.' }
    [IO.File]::WriteAllText($diagnosticFile, ('x' * 70000))
    InstallDiagnostic 'failed'
    if (!(Test-Path -LiteralPath ($diagnosticFile + '.previous')) -or (Get-Item -LiteralPath $diagnosticFile).Length -gt 2048) { throw 'Rotacao nao limitou o historico.' }
    $diagnosticFile = Join-Path $scratch 'blocked/child/file.jsonl'
    [IO.File]::WriteAllText((Join-Path $scratch 'blocked'), 'not a directory')
    InstallDiagnostic 'failed'
    Write-Output 'PASS: instalador coleta etapa/codigo de falha, sem dados privados, com rotacao e falha de escrita tolerada.'
} finally {
    $full = [IO.Path]::GetFullPath($scratch)
    if (!$full.StartsWith([IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($full) -notlike 'lg-install-diagnostics-*') { throw 'Limpeza fora do teste.' }
    Remove-Item -LiteralPath $full -Recurse -Force
}
