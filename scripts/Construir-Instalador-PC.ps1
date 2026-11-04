param([string]$Archive)
# Gera LazyGaming-Setup-<versão>.exe (IExpress, incluso no Windows) a partir do ZIP de Distribuir-PC.ps1.
# Sem download de ferramentas; o .exe não é assinado digitalmente (o Windows SmartScreen pode avisar).
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$Archive) {
    $output = & (Join-Path $PSScriptRoot 'Distribuir-PC.ps1') | Select-Object -Last 1
    $Archive = ($output -replace '^Pacote para compartilhar: ', '').Trim()
}
$Archive = [IO.Path]::GetFullPath($Archive)
if (!(Test-Path -LiteralPath $Archive)) { throw "Pacote não encontrado: $Archive" }
$iexpress = Join-Path $env:WINDIR 'System32\iexpress.exe'
if (!(Test-Path -LiteralPath $iexpress)) { throw 'IExpress não disponível neste Windows.' }
$version = ([IO.Path]::GetFileNameWithoutExtension($Archive) -replace '^LazyGaming-', '' -replace '-win-x64$', '')
$root = Split-Path -Parent $Archive
$stage = Join-Path $root 'setup-stage'
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item -LiteralPath $Archive -Destination (Join-Path $stage 'LazyGaming.zip')
# Scripts executados pelo Windows PowerShell 5.1 precisam de BOM; as fontes do repositório ficam sem BOM.
foreach ($script in 'Instalar-LazyGaming.ps1', 'Desinstalar-LazyGaming.ps1') {
    $text = [IO.File]::ReadAllText((Join-Path $PSScriptRoot $script), [Text.UTF8Encoding]::new($false, $true))
    [IO.File]::WriteAllText((Join-Path $stage $script), $text, [Text.UTF8Encoding]::new($true))
}
# IExpress roda em 32 bits; Sysnative chama o PowerShell de 64 bits quando disponível.
Set-Content -LiteralPath (Join-Path $stage 'install.cmd') -Encoding Ascii -Value '@echo off',
    'set "PS=%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"',
    'if not exist "%PS%" set "PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"',
    '"%PS%" -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0Instalar-LazyGaming.ps1"'
$target = Join-Path $root ("LazyGaming-Setup-$version.exe")
$sed = Join-Path $root 'setup.sed'
Set-Content -LiteralPath $sed -Encoding Ascii -Value @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=0
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=%PostInstallCmd%
AdminQuietInstCmd=%AdminQuietInstCmd%
UserQuietInstCmd=%UserQuietInstCmd%
SourceFiles=SourceFiles
[Strings]
InstallPrompt=Instalar o LazyGaming $version para este usuario?
DisplayLicense=
FinishMessage=
TargetName=$target
FriendlyName=LazyGaming $version
AppLaunched=cmd /c install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=cmd /c install.cmd
UserQuietInstCmd=cmd /c install.cmd
FILE0="LazyGaming.zip"
FILE1="Instalar-LazyGaming.ps1"
FILE2="install.cmd"
FILE3="Desinstalar-LazyGaming.ps1"
[SourceFiles]
SourceFiles0=$stage\
[SourceFiles0]
%FILE0%=
%FILE1%=
%FILE2%=
%FILE3%=
"@
if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
$process = Start-Process -FilePath $iexpress -ArgumentList '/N', '/Q', $sed -Wait -PassThru
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $target)) { throw "IExpress falhou (código $($process.ExitCode))." }
(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath ($target + '.sha256') -Encoding Ascii
Remove-Item -LiteralPath $stage -Recurse -Force
Write-Output "Instalador: $target"
