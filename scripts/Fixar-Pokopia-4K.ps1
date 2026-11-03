param([switch]$Apply)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskScript = Join-Path $PSScriptRoot 'Configurar-Pokopia.py'
$taskRegistrations = @(Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'LazyGaming\library\local-games.json') -Raw -Encoding UTF8 | ConvertFrom-Json)
$taskCandidates = @($taskRegistrations | Where-Object { $_.Id -eq 'local-140985860802' -or $_.Metadata.Title -eq 'Pokémon Pokopia' })
if ($taskCandidates.Count -ne 1 -or [IO.Path]::GetFileName($taskCandidates[0].Executable) -ne 'eglantine.exe' -or !$taskCandidates[0].ContentPath) { throw 'Não foi possível identificar o cadastro atual do Pokopia.' }
$taskEmulatorDirectory = Split-Path -Parent $taskCandidates[0].Executable
$taskContent = $taskCandidates[0].ContentPath
if (@(Get-CimInstance Win32_Process -Filter "Name='eglantine.exe'" -ErrorAction Stop).Count) { throw 'Feche o Pokopia/Eglantine normalmente antes de fixar a resolução.' }
$taskPlan = & python $taskScript --fixed-4k --emulator-directory $taskEmulatorDirectory --content-path $taskContent | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Não foi possível conferir o perfil do Pokopia.' }
if (!$Apply) { $taskPlan | ConvertTo-Json -Depth 5; return }
$taskBackup = Join-Path $taskRoot ('outputs\pokopia\4k-before-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,6))
& python $taskScript --fixed-4k --emulator-directory $taskEmulatorDirectory --content-path $taskContent --apply --backup-directory $taskBackup --expected-sha256 $taskPlan.beforeSha256
if ($LASTEXITCODE -ne 0) { throw 'A configuração não foi concluída. Consulte a mensagem acima.' }
Write-Output 'Perfil do Pokopia salvo: escala 2x, modo docked, sem herdar a escala global.'
Write-Output "Backup: $taskBackup"
Write-Output 'Abra o jogo pela TV. O log da próxima execução deve registrar Renderer.resolution_setup: Res2X.'
