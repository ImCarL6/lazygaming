param([string]$Package = (Join-Path $PSScriptRoot 'LazyGaming.zip'), [switch]$Quiet, [string]$Root, [switch]$SkipIntegration)
# -Root/-SkipIntegration: verificação isolada (sem atalhos, registro ou abertura).
# Instalador por usuário (sem administrador). Executado pelo LazyGaming-Setup.exe (IExpress).
# Instala em %LOCALAPPDATA%\Programs\LazyGaming; dados e configurações ficam em %LOCALAPPDATA%\LazyGaming e são preservados.
$ErrorActionPreference = 'Stop'
$installStage = 'preflight'
$diagnosticFile = if (!$Root -and !$SkipIntegration) { Join-Path $env:LOCALAPPDATA 'LazyGaming/diagnostics/installer.jsonl' } else { $null }
function InstallDiagnostic([string]$result, [System.Exception]$failure = $null) {
    if (!$diagnosticFile) { return }
    try {
        New-Item -ItemType Directory -Path (Split-Path -Parent $diagnosticFile) -Force | Out-Null
        if ((Test-Path -LiteralPath $diagnosticFile) -and (Get-Item -LiteralPath $diagnosticFile).Length -gt 65536) { Move-Item -LiteralPath $diagnosticFile -Destination ($diagnosticFile + '.previous') -Force }
        $entry = @{ at = [DateTime]::UtcNow.ToString('o'); component = 'installer'; result = $result; stage = $installStage; os = [Environment]::OSVersion.Version.ToString(); errorType = $(if ($failure) { $failure.GetType().FullName }); hresult = $(if ($failure) { $failure.HResult }) }
        [IO.File]::AppendAllText($diagnosticFile, ($entry | ConvertTo-Json -Compress) + "`n", [Text.UTF8Encoding]::new($false))
    } catch { }
}
InstallDiagnostic 'started'
Add-Type -AssemblyName System.Windows.Forms
function Say([string]$text, [string]$icon = 'Information') {
    if (!$Quiet) { [void][System.Windows.Forms.MessageBox]::Show($text, 'LazyGaming', 'OK', $icon) } else { Write-Output $text }
}
function FileSha256([string]$path) {
    $stream = [IO.File]::OpenRead($path)
    $hash = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $hash.Dispose(); $stream.Dispose() }
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

    $installStage = 'extracting-package'
    Expand-Archive -LiteralPath $Package -DestinationPath $stage -Force
    $content = Join-Path $stage 'LazyGaming'
    $release = Get-Content -LiteralPath (Join-Path $content 'release.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $installStage = 'checking-integrity'
    foreach ($file in $release.files) {
        $path = Join-Path $content ($file.path -replace '/', '\')
        if (!(Test-Path -LiteralPath $path) -or (FileSha256 $path) -ne $file.sha256) { throw "Pacote corrompido: $($file.path). Baixe o instalador novamente." }
    }

    $uninstallSource = Join-Path $PSScriptRoot 'Desinstalar-LazyGaming.ps1'
    if (!(Test-Path -LiteralPath $uninstallSource)) { throw 'Instalador incompleto: Desinstalar-LazyGaming.ps1 ausente. Baixe o instalador novamente.' }
    $installStage = 'replacing-program'
    New-Item -ItemType Directory -Path $programs -Force | Out-Null
    if (Test-Path -LiteralPath $target) {
        if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous -Recurse -Force }
        Move-Item -LiteralPath $target -Destination $previous
    }
    try { Move-Item -LiteralPath $content -Destination $target }
    catch { if (Test-Path -LiteralPath $previous) { Move-Item -LiteralPath $previous -Destination $target }; throw }

    $uninstall = Join-Path $target 'Desinstalar-LazyGaming.ps1'
    # UTF-8 com BOM: o Windows PowerShell 5.1 lê arquivos sem BOM como ANSI e corromperia os acentos.
    [IO.File]::WriteAllText($uninstall, [IO.File]::ReadAllText($uninstallSource, [Text.UTF8Encoding]::new($false)), [Text.UTF8Encoding]::new($true))

    if ($SkipIntegration) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue; Write-Output "OK $($release.version) em $target"; exit 0 }
    $installStage = 'registering-shortcuts'
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
    $installStage = 'opening-launcher'
    Start-Process -FilePath $exe -ArgumentList '--open-launcher' -WorkingDirectory $target
    InstallDiagnostic 'completed'
    # O launcher abrindo em tela cheia é a confirmação; uma caixa de sucesso ficaria atrás dele e prenderia o instalador.
    if ($note) { Say ("LazyGaming $($release.version) instalado.$note") 'Warning' } elseif ($Quiet) { Write-Output "LazyGaming $($release.version) instalado." }
} catch {
    InstallDiagnostic 'failed' $_.Exception
    if ($stage -and (Test-Path -LiteralPath $stage)) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
    Say ("Não foi possível instalar: " + $_.Exception.Message) 'Error'; exit 1
}
