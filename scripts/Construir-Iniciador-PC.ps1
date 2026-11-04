param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Compilador .NET Framework do Windows ausente para preparar o iniciador independente.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll ('/out:' + (Join-Path $OutputDirectory 'LazyGaming.exe')) (Join-Path $PSScriptRoot 'Iniciador-PC.cs')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao preparar o iniciador do PC.' }
