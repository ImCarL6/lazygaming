$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskOutput = Join-Path $taskRoot 'outputs\autostart'
if (!(Test-Path -LiteralPath $taskCompiler)) { throw 'Compilador .NET Framework do Windows não encontrado.' }
$null = New-Item -ItemType Directory -Path $taskOutput -Force
$taskLauncher = Join-Path $taskOutput 'LazyGaming.IniciarOculto.exe'
& $taskCompiler /nologo /target:winexe /optimize+ "/out:$taskLauncher" (Join-Path $PSScriptRoot 'Iniciador-Auxiliar-Oculto.cs')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o iniciador sem console.' }
Get-Item -LiteralPath $taskLauncher
