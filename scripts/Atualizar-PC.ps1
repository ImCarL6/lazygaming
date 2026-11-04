param([Parameter(Mandatory=$true)][string]$InstallationDirectory, [switch]$VerifyOnly)
$ErrorActionPreference = 'Stop'
# Run from the NEW extracted package. User data stays in LocalAppData.
$source = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$target = [IO.Path]::GetFullPath($InstallationDirectory).TrimEnd('\')
if ($target -eq [IO.Path]::GetPathRoot($target).TrimEnd('\') -or $source -eq $target -or $source.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Escolha a pasta da instalacao anterior, separada do novo pacote.' }
if (!(Test-Path -LiteralPath $target -PathType Container)) { throw 'Pasta da instalacao anterior nao encontrada.' }
foreach ($folder in @($source, $target)) {
    $item = Get-Item -LiteralPath $folder
    while ($null -ne $item) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Use pastas locais sem links para atualizar.' }
        $item = $item.Parent
    }
}
function Read-Release([string]$folder) {
    $release = Get-Content -LiteralPath (Join-Path $folder 'release.json') -Raw | ConvertFrom-Json
    $parsedVersion = $null
    if ($release.product -ne 'LazyGaming' -or !$release.files -or ![version]::TryParse([string]$release.version, [ref]$parsedVersion)) { throw 'Pacote de atualizacao invalido.' }
    return $release
}
function Test-Release([string]$folder, $release) {
    $seen = @{}
    foreach ($entry in $release.files) {
        $relative = [string]$entry.path
        if (!$relative -or [IO.Path]::IsPathRooted($relative) -or $relative.Contains(':') -or $relative -match '(^|[\\/])\.\.([\\/]|$)' -or $relative -match '(^|[\\/])(pairing|session|admin|pc-command|tv-recovery|pc-settings)\.json$' -or $relative -match '(^|[\\/])(library|private|certificates)([\\/]|$)') { throw 'O pacote contem um caminho nao permitido.' }
        $path = [IO.Path]::GetFullPath((Join-Path $folder $relative))
        if (!$path.StartsWith($folder + '\', [StringComparison]::OrdinalIgnoreCase) -or $seen.ContainsKey($path)) { throw 'Caminho duplicado ou fora do pacote.' }
        $seen[$path] = $true
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Arquivo ausente: $relative" }
        $item = Get-Item -LiteralPath $path
        while ($item.FullName -ne $folder) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'O pacote contem links nao permitidos.' }
            if ($item -is [IO.FileInfo]) { $item = $item.Directory } else { $item = $item.Parent }
            if ($null -eq $item) { throw 'Caminho de pacote invalido.' }
        }
        if ($entry.sha256 -notmatch '^[0-9a-fA-F]{64}$' -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Integridade invalida: $relative" }
    }
    if (!$seen.ContainsKey((Join-Path $folder 'LazyGaming.Host.exe'))) { throw 'Executavel ausente do pacote.' }
}
$next = Read-Release $source
$previous = Read-Release $target
if ([version]$next.version -le [version]$previous.version) { throw 'A atualizacao precisa ter uma versao mais recente.' }
if ($next.runtime -ne $previous.runtime) { throw 'Use um pacote para a mesma arquitetura da instalacao.' }
Test-Release $source $next
if ($VerifyOnly) { Write-Output 'PASS: versao, arquitetura, caminhos e integridade do novo pacote.'; return }
$data = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'LazyGaming'
if (Test-Path -LiteralPath (Join-Path $data 'session.json')) { throw 'Encerre o jogo pelo launcher e aguarde a restauracao das telas antes de atualizar.' }
function Assert-Closed {
    foreach ($process in @(Get-CimInstance Win32_Process -Filter "Name='LazyGaming.Host.exe'")) {
        if (!$process.ExecutablePath) { throw 'Nao foi possivel conferir o auxiliar aberto. Feche o LazyGaming antes de atualizar.' }
        if ([IO.Path]::GetFullPath($process.ExecutablePath) -eq (Join-Path $target 'LazyGaming.Host.exe')) { throw 'Feche o LazyGaming antes de atualizar. Nenhum processo foi encerrado.' }
    }
}
Assert-Closed
$suffix = [guid]::NewGuid().ToString('N')
$stage = $target + '.new-' + $suffix
$backup = $target + '.backup-' + $previous.version + '-' + $suffix
if ((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath $backup)) { throw 'Pastas de preparacao ja existem.' }
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($entry in $next.files) {
    $destination = Join-Path $stage $entry.path
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $source $entry.path) -Destination $destination
}
Copy-Item -LiteralPath (Join-Path $source 'release.json') -Destination (Join-Path $stage 'release.json')
Test-Release $stage $next
Assert-Closed
if (Test-Path -LiteralPath (Join-Path $data 'session.json')) { throw 'Uma sessao foi iniciada. Feche o jogo antes de atualizar.' }
# Verify resolved sibling paths before moving the previous installation.
$parent = Split-Path -Parent $target
foreach ($path in @($stage,$backup)) { if ((Split-Path -Parent ([IO.Path]::GetFullPath($path))) -ne $parent) { throw 'Pasta de preparacao fora do destino autorizado.' } }
Move-Item -LiteralPath $target -Destination $backup
try {
    Move-Item -LiteralPath $stage -Destination $target
    $versionOutput = & (Join-Path $target 'LazyGaming.Host.exe') --package-version | Out-String
    if ($LASTEXITCODE -ne 0 -or ([string]$versionOutput).Trim() -ne $next.version) { throw 'O novo executavel nao confirmou a versao.' }
} catch {
    if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination ($stage + '.failed') }
    Move-Item -LiteralPath $backup -Destination $target
    throw
}
Write-Output "Atualizado para $($next.version). Biblioteca e preferencias preservadas. Backup: $backup"
