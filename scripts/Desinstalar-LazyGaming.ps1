param([switch]$Quiet, [switch]$RemoveData, [string]$Sandbox)
# Desinstalador por usuário. O instalador copia este arquivo para a pasta do programa em UTF-8 com BOM,
# para o Windows PowerShell 5.1 ler os acentos corretamente.
# -Quiet/-RemoveData: sem janela (testes/automação). -Sandbox: raiz isolada para testes; nada fora dela é tocado
# (programa, dados, menu, área de trabalho e registro ficam sob a raiz/uma chave de teste; tarefa agendada é ignorada).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
function Say([string]$text, [string]$icon = 'Information') {
    if (!$Quiet) { [void][System.Windows.Forms.MessageBox]::Show($text, 'Desinstalar LazyGaming', 'OK', $icon) } else { Write-Output $text }
}
function Full([string]$path) { [IO.Path]::GetFullPath($path).TrimEnd('\') }

if ($Sandbox) {
    $base = Full $Sandbox
    $programs = Join-Path $base 'Programs'; $dataRoot = Join-Path $base 'LocalAppData'
    $menuRoot = Join-Path $base 'StartMenu'; $desktop = Join-Path $base 'Desktop'
    $registry = 'HKCU:\Software\LazyGaming-UninstallTest\' + (Split-Path -Leaf $base)
} else {
    $programs = Join-Path $env:LOCALAPPDATA 'Programs'; $dataRoot = $env:LOCALAPPDATA
    $menuRoot = [Environment]::GetFolderPath('Programs'); $desktop = [Environment]::GetFolderPath('Desktop')
    $registry = 'HKCU:\Software'
}
$target = Full (Join-Path $programs 'LazyGaming')
$previous = Full (Join-Path $programs 'LazyGaming.anterior')
$data = Full (Join-Path $dataRoot 'LazyGaming')
$exe = Join-Path $target 'LazyGaming.Host.exe'
$menu = Join-Path $menuRoot 'LazyGaming'
$uninstallKey = "$registry\Microsoft\Windows\CurrentVersion\Uninstall\LazyGaming"
$runKey = "$registry\Microsoft\Windows\CurrentVersion\Run"
$protocolKey = "$registry\Classes\lazygaming-pc"

# Destinos exatos: cada remoção recursiva precisa ser um destes caminhos, existir como pasta real e não ser link.
function Assert-Target([string]$path) {
    $full = Full $path
    $allowed = @($target, $previous, $data, (Full $menu))
    if ($allowed -notcontains $full) { throw "Caminho inesperado recusado: $full" }
    $leaf = Split-Path -Leaf $full
    if ($leaf -notin @('LazyGaming', 'LazyGaming.anterior')) { throw "Caminho inesperado recusado: $full" }
    $item = Get-Item -LiteralPath $full -Force
    if (!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "A pasta $full é um atalho/link; remova-a manualmente." }
    return $full
}
# Remove o conteúdo sem atravessar links: um link interno é apagado como link, nunca o destino dele.
function Remove-Tree([string]$path) {
    $full = Assert-Target $path
    foreach ($child in @(Get-ChildItem -LiteralPath $full -Force)) { Remove-Inside $child }
    Remove-Item -LiteralPath $full -Force
}
function Remove-Inside($item) {
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { $item.Delete(); return }
    if ($item.PSIsContainer) { foreach ($child in @(Get-ChildItem -LiteralPath $item.FullName -Force)) { Remove-Inside $child } }
    else { $item.Attributes = [IO.FileAttributes]::Normal }
    Remove-Item -LiteralPath $item.FullName -Force
}
function Shortcut-Owned([string]$path) {
    if (!(Test-Path -LiteralPath $path)) { return $false }
    try { $link = (New-Object -ComObject WScript.Shell).CreateShortcut($path); return (Full $link.TargetPath) -ieq (Full $exe) } catch { return $false }
}

try {
    # Em uso: qualquer processo executando a partir da pasta do programa (auxiliar, launcher ou cópia anterior).
    $inUse = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and (
        $_.ExecutablePath.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $_.ExecutablePath.StartsWith($previous + '\', [StringComparison]::OrdinalIgnoreCase)) })
    if ($inUse.Count) { throw 'O LazyGaming está aberto. Saia pelo ícone da área de notificação (Sair do LazyGaming) e tente de novo.' }
    # Sessão/restauração pendente: os arquivos guardam as telas e o áudio originais; sem eles não há como devolvê-los.
    $pending = @('session.json', 'display-verification.json') | Where-Object { Test-Path -LiteralPath (Join-Path $data $_) }
    if ($pending) { throw 'Há uma sessão de jogo ou restauração de telas pendente. Abra o LazyGaming, encerre a sessão (as telas voltam ao normal) e desinstale de novo.' }
    $removeAll = [bool]$RemoveData
    if (!$Quiet) {
        $form = New-Object System.Windows.Forms.Form
        $form.Text = 'Desinstalar LazyGaming'; $form.FormBorderStyle = 'FixedDialog'; $form.MaximizeBox = $false; $form.MinimizeBox = $false
        $form.StartPosition = 'CenterScreen'; $form.AutoScaleMode = 'Dpi'; $form.Font = New-Object System.Drawing.Font('Segoe UI', 10)
        $form.ClientSize = New-Object System.Drawing.Size(460, 200); $form.TopMost = $true
        $text = New-Object System.Windows.Forms.Label
        $text.Text = 'O LazyGaming será removido deste computador. Seus jogos e saves são mantidos.'
        $text.SetBounds(20, 18, 420, 44)
        $check = New-Object System.Windows.Forms.CheckBox
        $check.Text = 'Apagar também configurações, biblioteca e cache'; $check.SetBounds(20, 70, 420, 26)
        $detail = New-Object System.Windows.Forms.Label
        $detail.Text = "Remove todos os dados de %LOCALAPPDATA%\LazyGaming. Será preciso configurar e parear novamente."
        $detail.ForeColor = [System.Drawing.SystemColors]::GrayText; $detail.SetBounds(38, 98, 402, 44)
        $ok = New-Object System.Windows.Forms.Button; $ok.Text = 'Desinstalar'; $ok.SetBounds(244, 154, 100, 30); $ok.DialogResult = 'OK'
        $cancel = New-Object System.Windows.Forms.Button; $cancel.Text = 'Cancelar'; $cancel.SetBounds(350, 154, 90, 30); $cancel.DialogResult = 'Cancel'
        $form.AcceptButton = $ok; $form.CancelButton = $cancel
        $form.Controls.AddRange(@($text, $check, $detail, $ok, $cancel))
        $form.Add_Shown({ $cancel.Focus() })
        if ($form.ShowDialog() -ne 'OK') { exit 0 }
        $removeAll = $check.Checked
    }

    $left = New-Object System.Collections.Generic.List[string]
    function Try-Step([string]$label, [scriptblock]$step) { try { & $step } catch { $left.Add("$label ($($_.Exception.Message))") } }

    # Integrações do Windows: somente as que apontam para esta instalação.
    if (Shortcut-Owned (Join-Path $desktop 'LazyGaming.lnk')) { Try-Step 'Atalho da área de trabalho' { Remove-Item -LiteralPath (Join-Path $desktop 'LazyGaming.lnk') -Force } }
    if (Test-Path -LiteralPath $menu) {
        $foreign = @(Get-ChildItem -LiteralPath $menu -Force | Where-Object { $_.Extension -ne '.lnk' -or !(Shortcut-Owned $_.FullName) })
        if ($foreign.Count) { foreach ($link in @(Get-ChildItem -LiteralPath $menu -Filter *.lnk -Force | Where-Object { Shortcut-Owned $_.FullName })) { Try-Step 'Atalho do menu Iniciar' { Remove-Item -LiteralPath $link.FullName -Force } } }
        else { Try-Step 'Pasta do menu Iniciar' { Remove-Tree $menu } }
    }
    $run = (Get-ItemProperty -LiteralPath $runKey -Name LazyGaming -ErrorAction SilentlyContinue).LazyGaming
    if ($run -and $run.IndexOf($exe, [StringComparison]::OrdinalIgnoreCase) -ge 0) { Try-Step 'Iniciar com o Windows' { Remove-ItemProperty -LiteralPath $runKey -Name LazyGaming -Force } }
    if ((Get-ItemProperty -LiteralPath $protocolKey -Name LazyGamingOwner -ErrorAction SilentlyContinue).LazyGamingOwner -eq 'LazyGaming') {
        Try-Step 'Protocolo lazygaming-pc' { Remove-Item -LiteralPath $protocolKey -Recurse -Force }
    }
    if (!$Sandbox) {
        $task = Get-ScheduledTask -TaskName 'LazyGaming - Auxiliar' -ErrorAction SilentlyContinue
        if ($task -and @($task.Actions | Where-Object { "$($_.Execute) $($_.Arguments)" -match 'LazyGaming' }).Count) {
            Try-Step 'Tarefa agendada LazyGaming - Auxiliar' { Unregister-ScheduledTask -TaskName 'LazyGaming - Auxiliar' -Confirm:$false }
        }
    }

    # Programa e cópia anterior. O próprio script está na pasta do programa: o PowerShell já o leu por inteiro,
    # então a pasta pode ser removida daqui; se algo estiver em uso, informar o que restou.
    if (Test-Path -LiteralPath $previous) { Try-Step "Versão anterior em $previous" { Remove-Tree $previous } }
    if (Test-Path -LiteralPath $target) {
        Set-Location -LiteralPath $programs
        Try-Step "Programa em $target" { Remove-Tree $target }
    }
    if ($removeAll -and (Test-Path -LiteralPath $data)) { Try-Step "Dados em $data" { Remove-Tree $data } }
    # Registro de desinstalação só some quando o programa saiu, para permitir nova tentativa por Aplicativos.
    if (!(Test-Path -LiteralPath $target) -and (Test-Path -LiteralPath $uninstallKey)) { Try-Step 'Entrada em Aplicativos' { Remove-Item -LiteralPath $uninstallKey -Recurse -Force } }

    if ($left.Count) {
        $retry = if (Test-Path -LiteralPath $target) { "`n`nDepois de fechar o que estiver usando esses arquivos, execute de novo: $target\Desinstalar-LazyGaming.ps1" } else { '' }
        Say ("A desinstalação não terminou. Ficou para trás:`n- " + ($left -join "`n- ") + $retry) 'Warning'; exit 2
    }
    $kept = if ($removeAll) { 'Configurações, biblioteca e cache também foram apagados.' } else { "Suas configurações continuam em $data para uma reinstalação." }
    Say "LazyGaming removido. $kept"
} catch {
    Say ('Não foi possível desinstalar: ' + $_.Exception.Message) 'Error'; exit 1
}
