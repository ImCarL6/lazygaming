param([string]$Package = (Join-Path $PSScriptRoot 'LazyGaming.zip'), [switch]$Quiet, [string]$Root, [switch]$SkipIntegration)
# -Root/-SkipIntegration: verificação isolada (sem atalhos, registro ou abertura).
# Instalador por usuário (sem administrador). Executado pelo LazyGaming-Setup.exe (IExpress).
# Instala em %LOCALAPPDATA%\Programs\LazyGaming; dados e configurações ficam em %LOCALAPPDATA%\LazyGaming e são preservados.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
function Say([string]$text, [string]$icon = 'Information') {
    if (!$Quiet) { [void][System.Windows.Forms.MessageBox]::Show($text, 'LazyGaming', 'OK', $icon) } else { Write-Output $text }
}
try {
    if (![Environment]::Is64BitOperatingSystem) { throw 'O LazyGaming precisa do Windows de 64 bits.' }
    $programs = if ($Root) { $Root } else { Join-Path $env:LOCALAPPDATA 'Programs' }
    $target = Join-Path $programs 'LazyGaming'
    $previous = Join-Path $programs 'LazyGaming.anterior'
    $stage = Join-Path $env:TEMP ('LazyGaming-setup-' + [guid]::NewGuid().ToString('N'))
    $exe = Join-Path $target 'LazyGaming.Host.exe'

    # Nunca substituir binários em uso nem encerrar processos por conta própria.
    $running = @(Get-CimInstance Win32_Process -Filter "Name='LazyGaming.Host.exe'" | Where-Object { $_.ExecutablePath -and $_.ExecutablePath -ieq $exe })
    if ($running.Count) { throw 'O LazyGaming está aberto. Saia pelo ícone da área de notificação (Sair do LazyGaming) e execute o instalador novamente.' }

    Expand-Archive -LiteralPath $Package -DestinationPath $stage -Force
    $content = Join-Path $stage 'LazyGaming'
    $release = Get-Content -LiteralPath (Join-Path $content 'release.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($file in $release.files) {
        $path = Join-Path $content ($file.path -replace '/', '\')
        if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) { throw "Pacote corrompido: $($file.path). Baixe o instalador novamente." }
    }

    New-Item -ItemType Directory -Path $programs -Force | Out-Null
    if (Test-Path -LiteralPath $target) {
        if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous -Recurse -Force }
        Move-Item -LiteralPath $target -Destination $previous
    }
    try { Move-Item -LiteralPath $content -Destination $target }
    catch { if (Test-Path -LiteralPath $previous) { Move-Item -LiteralPath $previous -Destination $target }; throw }

    if ($SkipIntegration) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue; Write-Output "OK $($release.version) em $target"; exit 0 }
    $shell = New-Object -ComObject WScript.Shell
    $menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'LazyGaming'
    New-Item -ItemType Directory -Path $menu -Force | Out-Null
    function Shortcut([string]$path, [string]$arguments, [string]$description) {
        $link = $shell.CreateShortcut($path); $link.TargetPath = $exe; $link.Arguments = $arguments
        $link.WorkingDirectory = $target; $link.IconLocation = "$exe,0"; $link.Description = $description; $link.Save()
    }
    Shortcut (Join-Path $menu 'LazyGaming.lnk') '--open-launcher' 'Abrir o launcher do LazyGaming'
    Shortcut (Join-Path $menu 'Central do LazyGaming.lnk') '--open-hub' 'Biblioteca, telas, controle e configurações'
    Shortcut (Join-Path ([Environment]::GetFolderPath('Desktop')) 'LazyGaming.lnk') '--open-launcher' 'Abrir o launcher do LazyGaming'

    # Desinstalação aparece em Configurações > Aplicativos (somente este usuário).
    $uninstall = Join-Path $target 'Desinstalar-LazyGaming.ps1'
    Set-Content -LiteralPath $uninstall -Encoding UTF8 -Value @'
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$target = Join-Path $env:LOCALAPPDATA 'Programs\LazyGaming'
$exe = Join-Path $target 'LazyGaming.Host.exe'
if (@(Get-CimInstance Win32_Process -Filter "Name='LazyGaming.Host.exe'" | Where-Object { $_.ExecutablePath -ieq $exe }).Count) {
    [void][System.Windows.Forms.MessageBox]::Show('Saia do LazyGaming pelo ícone da área de notificação antes de desinstalar.', 'LazyGaming'); exit 1 }
if ([System.Windows.Forms.MessageBox]::Show('Remover o LazyGaming? Suas configurações e a biblioteca em %LOCALAPPDATA%\LazyGaming são mantidas.', 'LazyGaming', 'YesNo') -ne 'Yes') { exit 0 }
Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('Programs')) 'LazyGaming') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('Desktop')) 'LazyGaming.lnk') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LazyGaming' -Recurse -Force -ErrorAction SilentlyContinue
$previous = Join-Path $env:LOCALAPPDATA 'Programs\LazyGaming.anterior'
if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous -Recurse -Force }
Start-Process -FilePath cmd.exe -ArgumentList '/c', 'timeout /t 2 >nul & rmdir /s /q "' + $target + '"' -WindowStyle Hidden
'@
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LazyGaming'
    New-Item -Path $key -Force | Out-Null
    $values = @{ DisplayName = 'LazyGaming'; DisplayVersion = [string]$release.version; Publisher = 'LazyGaming'; InstallLocation = $target; DisplayIcon = "$exe,0"; NoModify = 1; NoRepair = 1
        UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$uninstall`"" }
    foreach ($name in $values.Keys) { New-ItemProperty -Path $key -Name $name -Value $values[$name] -PropertyType ($(if ($values[$name] -is [int]) { 'DWord' } else { 'String' })) -Force | Out-Null }

    # WebView2 costuma vir com o Windows 11; sem ele o LazyGaming abre a Central no navegador com orientação.
    $webview = @('HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'HKCU:\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') |
        Where-Object { (Get-ItemProperty -LiteralPath $_ -Name pv -ErrorAction SilentlyContinue).pv -match '^[1-9]' }
    $note = if ($webview) { '' } else { "`n`nAtenção: o Microsoft Edge WebView2 Runtime não foi encontrado. Instale-o em https://developer.microsoft.com/microsoft-edge/webview2/ para o launcher nativo." }
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
    Start-Process -FilePath $exe -ArgumentList '--open-hub' -WorkingDirectory $target
    Say ("LazyGaming $($release.version) instalado. A Central vai abrir para a configuração inicial; depois use o atalho LazyGaming ou o atalho do controle.$note")
} catch {
    if ($stage -and (Test-Path -LiteralPath $stage)) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
    Say ("Não foi possível instalar: " + $_.Exception.Message) 'Error'; exit 1
}
