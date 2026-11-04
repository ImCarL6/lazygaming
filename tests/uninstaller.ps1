$ErrorActionPreference = 'Stop'
# Desinstalador em raiz isolada: acentos pelo Windows PowerShell 5.1, manter/apagar dados, integracoes proprias x de terceiros,
# links, processo em uso, sessao pendente e remocao parcial. Nunca toca no perfil real.
$projectRoot = Split-Path -Parent $PSScriptRoot
$ps51 = "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"
$testRoot = Join-Path $env:TEMP ('lg-uninstall-' + [guid]::NewGuid().ToString('N'))
$utf8 = [Text.UTF8Encoding]::new($false)
$failures = 0
function Check([bool]$ok, [string]$name) { if ($ok) { Write-Output "PASS $name" } else { Write-Output "FAIL $name"; $script:failures++ } }

# Mesma conversao do Construir-Instalador-PC.ps1: fontes sem BOM, staging com BOM.
$stage = Join-Path $testRoot 'stage'; New-Item -ItemType Directory -Path $stage -Force | Out-Null
foreach ($name in 'Instalar-LazyGaming.ps1', 'Desinstalar-LazyGaming.ps1') {
    [IO.File]::WriteAllText((Join-Path $stage $name), [IO.File]::ReadAllText((Join-Path $projectRoot "scripts\$name"), [Text.UTF8Encoding]::new($false, $true)), [Text.UTF8Encoding]::new($true))
}
$wrapper = Join-Path $testRoot 'run.ps1'
@'
param($Script, $Sandbox, $Out, [int]$Remove = 0)
$r = & $Script -Quiet -Sandbox $Sandbox -RemoveData:([bool]$Remove)
[IO.File]::WriteAllText($Out, (@($r) -join "`n"), [Text.UTF8Encoding]::new($false))
exit $LASTEXITCODE
'@ | Set-Content -LiteralPath $wrapper -Encoding Ascii

function New-Sandbox([string]$name) {
    $box = Join-Path $testRoot ("acentua" + [char]0xE7 + [char]0xE3 + "o e espa" + [char]0xE7 + "o\$name")
    $program = Join-Path $box 'Programs\LazyGaming'
    New-Item -ItemType Directory -Path $program, (Join-Path $box 'Programs\LazyGaming.anterior'), (Join-Path $box 'LocalAppData\LazyGaming\library\art'),
        (Join-Path $box 'LocalAppData\LazyGaming\EBWebView\Default'), (Join-Path $box 'StartMenu\LazyGaming'), (Join-Path $box 'Desktop') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $program 'LazyGaming.Host.exe'), 'fixture never executed')
    Copy-Item -LiteralPath (Join-Path $stage 'Desinstalar-LazyGaming.ps1') -Destination $program
    [IO.File]::WriteAllText((Join-Path $box 'Programs\LazyGaming.anterior\old.txt'), 'old')
    foreach ($f in 'pc-settings.json', 'pairing.json', 'tv-recovery.json', 'library\catalog.json', 'library\art\a.jpg', 'EBWebView\Default\Cookies') {
        [IO.File]::WriteAllText((Join-Path $box "LocalAppData\LazyGaming\$f"), ("dados $f " + [char]0xE7))
    }
    $shell = New-Object -ComObject WScript.Shell
    foreach ($link in (Join-Path $box 'StartMenu\LazyGaming\LazyGaming.lnk'), (Join-Path $box 'Desktop\LazyGaming.lnk')) {
        $l = $shell.CreateShortcut($link); $l.TargetPath = Join-Path $program 'LazyGaming.Host.exe'; $l.Save()
    }
    $reg = 'HKCU:\Software\LazyGaming-UninstallTest\' + $name
    New-Item -Path "$reg\Microsoft\Windows\CurrentVersion\Uninstall\LazyGaming" -Force | Out-Null
    New-Item -Path "$reg\Microsoft\Windows\CurrentVersion\Run" -Force | Out-Null
    New-ItemProperty -Path "$reg\Microsoft\Windows\CurrentVersion\Run" -Name LazyGaming -Value "`"$program\LazyGaming.Host.exe`" --startup" -Force | Out-Null
    New-ItemProperty -Path "$reg\Microsoft\Windows\CurrentVersion\Run" -Name OutroApp -Value 'C:\Outro\app.exe' -Force | Out-Null
    New-Item -Path "$reg\Classes\lazygaming-pc" -Force | Out-Null
    New-ItemProperty -Path "$reg\Classes\lazygaming-pc" -Name LazyGamingOwner -Value LazyGaming -Force | Out-Null
    return $box
}
function Run([string]$box, [switch]$RemoveData) {
    $out = Join-Path $testRoot ([guid]::NewGuid().ToString('N') + '.txt')
    & $ps51 -NoProfile -ExecutionPolicy Bypass -File $wrapper -Script (Join-Path $box 'Programs\LazyGaming\Desinstalar-LazyGaming.ps1') -Sandbox $box -Out $out -Remove ([int][bool]$RemoveData)
    $code = $LASTEXITCODE
    return @{ Code = $code; Text = $(if (Test-Path -LiteralPath $out) { [IO.File]::ReadAllText($out, $utf8) } else { '' }) }
}
function Hashes([string]$dir) { Get-ChildItem -LiteralPath $dir -Recurse -File -Force | Sort-Object FullName | ForEach-Object { $_.FullName.Substring($dir.Length) + '=' + [Convert]::ToBase64String([Security.Cryptography.SHA256]::Create().ComputeHash([IO.File]::ReadAllBytes($_.FullName))) } }

try {
    $sentinel = Join-Path $testRoot 'sentinela-fora.txt'; [IO.File]::WriteAllText($sentinel, 'nao apagar')

    # 1. Instalador (com BOM, pelo PS 5.1) grava o desinstalador com BOM e conteudo identico a fonte.
    $content = Join-Path $testRoot 'payload\LazyGaming'; New-Item -ItemType Directory -Path $content -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $content 'LazyGaming.Host.exe'), 'fixture never executed')
    $sha = (Get-FileHash -LiteralPath (Join-Path $content 'LazyGaming.Host.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
    @{ version = '0.0.0-test'; files = @(@{ path = 'LazyGaming.Host.exe'; sha256 = $sha }) } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $content 'release.json') -Encoding UTF8
    Compress-Archive -LiteralPath $content -DestinationPath (Join-Path $testRoot 'payload.zip')
    & $ps51 -NoProfile -ExecutionPolicy Bypass -File (Join-Path $stage 'Instalar-LazyGaming.ps1') -Package (Join-Path $testRoot 'payload.zip') -Root (Join-Path $testRoot 'inst') -SkipIntegration -Quiet | Out-Null
    $installed = [IO.File]::ReadAllBytes((Join-Path $testRoot 'inst\LazyGaming\Desinstalar-LazyGaming.ps1'))
    $expected = [byte[]](0xEF, 0xBB, 0xBF) + [IO.File]::ReadAllBytes((Join-Path $projectRoot 'scripts\Desinstalar-LazyGaming.ps1'))
    Check ($LASTEXITCODE -eq 0 -and [Convert]::ToBase64String($installed) -eq [Convert]::ToBase64String($expected)) 'instalador grava desinstalador UTF-8 com BOM identico a fonte'

    # 2. Padrao: remove programa e integracoes proprias, mantem dados byte a byte e o Run de terceiros.
    $box = New-Sandbox 'keep'; $data = Join-Path $box 'LocalAppData\LazyGaming'; $before = Hashes $data
    $r = Run $box
    $reg = 'HKCU:\Software\LazyGaming-UninstallTest\keep'
    Check ($r.Code -eq 0) "manter dados termina com sucesso ($($r.Code): $($r.Text))"
    Check ($r.Text -match ('Suas configura' + [char]0xE7 + [char]0xF5 + 'es continuam')) 'acentos corretos na mensagem (Windows PowerShell 5.1)'
    Check (!(Test-Path -LiteralPath (Join-Path $box 'Programs\LazyGaming')) -and !(Test-Path -LiteralPath (Join-Path $box 'Programs\LazyGaming.anterior'))) 'programa e versao anterior removidos'
    Check ((@(Hashes $data) -join '|') -eq ($before -join '|')) 'dados preservados byte a byte'
    Check (!(Test-Path -LiteralPath (Join-Path $box 'StartMenu\LazyGaming')) -and !(Test-Path -LiteralPath (Join-Path $box 'Desktop\LazyGaming.lnk'))) 'atalhos removidos'
    $run = Get-ItemProperty -LiteralPath "$reg\Microsoft\Windows\CurrentVersion\Run"
    Check (!$run.LazyGaming -and $run.OutroApp -eq 'C:\Outro\app.exe') 'Run proprio removido, de terceiro preservado'
    Check (!(Test-Path -LiteralPath "$reg\Classes\lazygaming-pc") -and !(Test-Path -LiteralPath "$reg\Microsoft\Windows\CurrentVersion\Uninstall\LazyGaming")) 'protocolo e entrada de Aplicativos removidos'

    # 3. Apagar tudo: pasta de dados inteira some, inclusive WebView2/chaves; link interno apagado sem seguir o destino.
    $box = New-Sandbox 'all'; $data = Join-Path $box 'LocalAppData\LazyGaming'
    $outside = Join-Path $testRoot 'fora-do-destino'; New-Item -ItemType Directory -Path $outside -Force | Out-Null; [IO.File]::WriteAllText((Join-Path $outside 'jogo.sav'), 'save')
    cmd /c mklink /J "$data\link-externo" "$outside" | Out-Null
    $r = Run $box -RemoveData
    Check ($r.Code -eq 0 -and $r.Text -match ('tamb' + [char]0xE9 + 'm foram apagados')) "apagar tudo termina com sucesso ($($r.Code): $($r.Text))"
    Check (!(Test-Path -LiteralPath $data)) 'pasta de dados inteira removida'
    Check ((Test-Path -LiteralPath (Join-Path $outside 'jogo.sav')) -and (Test-Path -LiteralPath $sentinel)) 'junction interna nao atravessada; sentinela fora preservada'

    # 4. Sessao pendente: recusa sem alterar nada.
    $box = New-Sandbox 'pending'; [IO.File]::WriteAllText((Join-Path $box 'LocalAppData\LazyGaming\session.json'), '{}')
    $before = Hashes $box; $r = Run $box -RemoveData
    Check ($r.Code -eq 1 -and $r.Text -match ('sess' + [char]0xE3 + 'o de jogo') -and ((@(Hashes $box) -join '|') -eq ($before -join '|'))) 'sessao pendente recusada sem alteracoes'

    # 5. Pasta de dados como junction para fora: recusada, destino intacto.
    $box = New-Sandbox 'linked'; $data = Join-Path $box 'LocalAppData\LazyGaming'
    Remove-Item -LiteralPath $data -Recurse -Force
    cmd /c mklink /J "$data" "$outside" | Out-Null
    $r = Run $box -RemoveData
    Check ($r.Code -eq 2 -and $r.Text -match 'atalho/link' -and (Test-Path -LiteralPath (Join-Path $outside 'jogo.sav'))) 'pasta de dados ligada para fora recusada e informada'
    cmd /c rmdir "$data" | Out-Null

    # 6. Processo em uso a partir da pasta do programa: recusa sem alteracoes.
    $box = New-Sandbox 'busy'; $busyExe = Join-Path $box 'Programs\LazyGaming\LazyGaming.Host.exe'
    Copy-Item -LiteralPath "$env:WINDIR\System32\PING.EXE" -Destination $busyExe -Force
    $proc = Start-Process -FilePath $busyExe -ArgumentList '-n', '30', '127.0.0.1' -WindowStyle Hidden -PassThru
    try {
        $before = Hashes $box; $r = Run $box
        Check ($r.Code -eq 1 -and $r.Text -match ('est' + [char]0xE1 + ' aberto') -and ((@(Hashes $box) -join '|') -eq ($before -join '|'))) 'programa em uso recusado sem alteracoes'
    } finally { if (!$proc.HasExited) { $proc.Kill(); $proc.WaitForExit() } }

    # 7. Remocao parcial: arquivo de dados aberto fica, erro informado, nova tentativa conclui.
    $box = New-Sandbox 'partial'; $data = Join-Path $box 'LocalAppData\LazyGaming'
    $lock = [IO.File]::Open((Join-Path $data 'pairing.json'), 'Open', 'Read', 'None')
    try { $r = Run $box -RemoveData } finally { $lock.Dispose() }
    Check ($r.Code -eq 2 -and $r.Text -match ('n' + [char]0xE3 + 'o terminou') -and (Test-Path -LiteralPath $data)) 'remocao parcial informada'
    $retry = Join-Path $testRoot 'retry.ps1'; Copy-Item -LiteralPath (Join-Path $stage 'Desinstalar-LazyGaming.ps1') -Destination $retry
    $out = Join-Path $testRoot 'retry.txt'
    & $ps51 -NoProfile -ExecutionPolicy Bypass -File $wrapper -Script $retry -Sandbox $box -Out $out -Remove 1
    Check ($LASTEXITCODE -eq 0 -and !(Test-Path -LiteralPath $data)) 'nova tentativa conclui a remocao'

    # 8. Controle negativo: sem BOM o PS 5.1 corromperia o texto (prova que o teste detecta o defeito).
    $box = New-Sandbox 'nobom'
    [IO.File]::WriteAllText((Join-Path $box 'Programs\LazyGaming\Desinstalar-LazyGaming.ps1'), [IO.File]::ReadAllText((Join-Path $projectRoot 'scripts\Desinstalar-LazyGaming.ps1'), $utf8), $utf8)
    $r = Run $box
    Check ($r.Text -notmatch ('Suas configura' + [char]0xE7 + [char]0xF5 + 'es continuam')) 'controle: copia sem BOM produz texto corrompido'

    Check (Test-Path -LiteralPath $sentinel) 'sentinela fora dos destinos preservada'
} finally {
    Remove-Item -LiteralPath 'HKCU:\Software\LazyGaming-UninstallTest' -Recurse -Force -ErrorAction SilentlyContinue
    $absolute = [IO.Path]::GetFullPath($testRoot)
    if (!$absolute.StartsWith([IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($absolute) -notlike 'lg-uninstall-*') { throw 'Pasta de teste inesperada.' }
    Get-ChildItem -LiteralPath $absolute -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue | ForEach-Object { cmd /c rmdir "$($_.FullName)" | Out-Null }
    Remove-Item -LiteralPath $absolute -Recurse -Force
}
if ($failures) { throw "$failures verificacao(oes) do desinstalador falharam." }
Write-Output 'PASS: desinstalador isolado.'
