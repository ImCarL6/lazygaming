# Exemplo de previa: .\scripts\Limpar-Projeto.ps1 -WhatIf
# Execucao manual:   .\scripts\Limpar-Projeto.ps1
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param()

$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$taskPrefix = $taskRoot + '\'
$taskProject = Join-Path $taskRoot 'src\LazyGaming.Host\LazyGaming.Host.csproj'
if (!(Test-Path -LiteralPath $taskProject -PathType Leaf) -or
    !(Test-Path -LiteralPath (Join-Path $taskRoot 'LazyGaming.slnx') -PathType Leaf) -or
    !(Test-Path -LiteralPath (Join-Path $taskRoot '.git'))) {
    throw 'Nao foi possivel confirmar a raiz do projeto LazyGaming.'
}

[xml]$taskXml = Get-Content -LiteralPath $taskProject -Raw
$taskVersion = [string]$taskXml.Project.PropertyGroup.Version
if ($taskVersion -ne '0.28.4') {
    throw 'Este plano foi revisado para a versao 0.28.4. Revise o script antes de limpar outra versao.'
}

$taskTargets = [Collections.Generic.List[object]]::new()
function Add-CleanupTarget([string]$RelativePath, [string]$Group) {
    $candidate = [IO.Path]::GetFullPath((Join-Path $taskRoot $RelativePath)).TrimEnd('\')
    if (!$candidate.StartsWith($taskPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Caminho fora do projeto: $candidate"
    }
    if (Test-Path -LiteralPath $candidate) {
        $taskTargets.Add([pscustomobject]@{ Path = $candidate; Group = $Group })
    }
}

# Materiais recentes e recuperacao ainda util permanecem nos caminhos originais.
$taskKeepOutputs = @('distribution', 'Sons do LazyGaming', 'Sons do LazyGaming.zip',
    'exportar-sons-lazygaming.py', 'Restaurar-Tizen-Original.ps1')
$taskOutputRoot = Join-Path $taskRoot 'outputs'
if (Test-Path -LiteralPath $taskOutputRoot) {
    foreach ($item in Get-ChildItem -LiteralPath $taskOutputRoot -Force) {
        if ($item.Name -notin $taskKeepOutputs) {
            Add-CleanupTarget ('outputs\' + $item.Name) 'Evidencias, backups e builds antigos'
        }
    }
}

$taskRelease = '0.28.4-win-x64-f4e4322293c3410397f4ec9ce2e73309'
$taskDistribution = Join-Path $taskRoot 'outputs\distribution'
$taskCurrent = Join-Path $taskDistribution $taskRelease
$taskSetup = Join-Path $taskCurrent 'LazyGaming-Setup-0.28.4.exe'
$taskChecksum = $taskSetup + '.sha256'
if (Test-Path -LiteralPath $taskDistribution) {
    if (!(Test-Path -LiteralPath $taskSetup -PathType Leaf) -or
        !(Test-Path -LiteralPath $taskChecksum -PathType Leaf)) {
        throw 'Instalador final ou checksum ausente. Distribuicoes preservadas; nenhuma exclusao realizada.'
    }
    $expected = 'a03020262bf5cdf636c4dfbbd95b7b173cc460b1ee8c862a09c21bc61c92833b'
    if ((Get-FileHash -LiteralPath $taskSetup -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected -or
        (Get-Content -LiteralPath $taskChecksum -Raw).Trim() -ne $expected) {
        throw 'Integridade do instalador final nao confirmada. Nenhuma exclusao realizada.'
    }
    foreach ($item in Get-ChildItem -LiteralPath $taskDistribution -Force) {
        if ($item.Name -ne $taskRelease) {
            Add-CleanupTarget ('outputs\distribution\' + $item.Name) 'Distribuicoes antigas'
        }
    }
    foreach ($item in Get-ChildItem -LiteralPath $taskCurrent -Force) {
        if ($item.Name -notin @('LazyGaming-Setup-0.28.4.exe', 'LazyGaming-Setup-0.28.4.exe.sha256')) {
            Add-CleanupTarget ('outputs\distribution\' + $taskRelease + '\' + $item.Name) 'Staging e duplicatas da distribuicao atual'
        }
    }
}

# Nao remove dist inteiro: uma futura publicacao em dist\LazyGaming e preservada.
Add-CleanupTarget 'dist\lixostream-0.1.12.zip' 'Arquivo antigo de distribuicao'
Add-CleanupTarget '.codex-remote-attachments' 'Anexos antigos de investigacao'
foreach ($project in @('src\LazyGaming.Core', 'src\LazyGaming.Host', 'src\LazyGaming.Platform.Windows',
    'tests\Core.Tests', 'tests\Host.Tests', 'tests\NativeDesktopFixture')) {
    foreach ($generated in @('bin', 'obj', '__pycache__')) {
        Add-CleanupTarget ($project + '\' + $generated) 'Cache de compilacao regeneravel'
    }
}
foreach ($obsolete in @('DIAGNOSTICO-BLOQUEIO-0.7.2.md', 'DIAGNOSTICO-CULT.md', 'INDEXAR-POKOPIA.md',
    'POKOPIA-2.5.0.md', 'PROMPT-CLAUDE-PROXIMA-SESSAO.md', 'PROMPT-CODEX-0282.md', 'PROMPT-CONTINUAR-0.25.md')) {
    Add-CleanupTarget ('docs\' + $obsolete) 'Documentos locais superados'
}

# Conferir todos os destinos antes de excluir o primeiro. Nao seguir links/junctions.
$taskTracked = @(& git -C $taskRoot ls-files)
if ($LASTEXITCODE -ne 0 -or !$taskTracked.Count) { throw 'Falha ao consultar os arquivos versionados.' }
$taskTrackedPaths = @($taskTracked | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $taskRoot $_)) })
$taskRunning = @(Get-CimInstance Win32_Process | Where-Object {
    $_.ExecutablePath -and $_.ExecutablePath.StartsWith($taskPrefix, [StringComparison]::OrdinalIgnoreCase)
})
$taskTotalBytes = [long]0
$taskSummary = @{}
foreach ($target in $taskTargets) {
    $resolved = (Resolve-Path -LiteralPath $target.Path).ProviderPath.TrimEnd('\')
    if ($resolved -ne $target.Path -or !$resolved.StartsWith($taskPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        $resolved -eq (Join-Path $taskRoot '.git')) { throw "Destino inesperado: $resolved" }
    foreach ($trackedPath in $taskTrackedPaths) {
        if ($trackedPath -eq $resolved -or $trackedPath.StartsWith($resolved + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Destino contem arquivo versionado; preservado: $resolved"
        }
    }
    foreach ($process in $taskRunning) {
        if ($process.ExecutablePath -eq $resolved -or $process.ExecutablePath.StartsWith($resolved + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Programa em execucao dentro de um destino: $resolved. Feche-o antes da limpeza."
        }
    }
    # Conferir os ancestrais tambem impede chegar a um destino por uma junction.
    $ancestor = Get-Item -LiteralPath $resolved -Force
    while ($ancestor -and $ancestor.FullName.TrimEnd('\') -ne $taskRoot) {
        if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Link encontrado: $($ancestor.FullName)" }
        if ($ancestor.PSIsContainer) { $ancestor = $ancestor.Parent } else { $ancestor = $ancestor.Directory }
        if (!$ancestor) { break }
    }
    $item = Get-Item -LiteralPath $resolved -Force
    $entries = @($item)
    if ($item.PSIsContainer) { $entries += @(Get-ChildItem -LiteralPath $resolved -Force -Recurse) }
    foreach ($entry in $entries) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Link encontrado: $($entry.FullName)" }
    }
    # Chaves/certificados pessoais nao entram na limpeza. As CAs publicas do
    # pacote sao duplicatas das fontes; o bundle curl e publico e regeneravel.
    foreach ($entry in $entries | Where-Object { !$_.PSIsContainer }) {
        if ($entry.Extension -in @('.p12', '.pfx', '.pem', '.key', '.pwd', '.pri', '.csr')) {
            throw "Material privado potencial encontrado; revisar manualmente: $($entry.FullName)"
        }
        if ($entry.Extension -in @('.crt', '.cer')) {
            $caHash = (Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            $knownCa = $false
            foreach ($caName in @('vd_tizen_dev_author_ca.cer', 'vd_tizen_dev_public2.crt')) {
                $sourceCa = Join-Path $taskRoot ('src\LazyGaming.Host\SamsungCA\' + $caName)
                if ($caHash -eq (Get-FileHash -LiteralPath $sourceCa -Algorithm SHA256).Hash.ToLowerInvariant()) { $knownCa = $true }
            }
            $curlBundle = Join-Path $taskRoot 'outputs\upstream\moonlight-source-1.19.0\wasm\static\curl\ca-bundle.crt'
            if (!$knownCa -and $entry.FullName -ne $curlBundle) { throw "Certificado a revisar: $($entry.FullName)" }
        }
    }
    $size = [long](($entries | Where-Object { !$_.PSIsContainer } | Measure-Object Length -Sum).Sum)
    $taskTotalBytes += $size
    if (!$taskSummary.ContainsKey($target.Group)) { $taskSummary[$target.Group] = [long]0 }
    $taskSummary[$target.Group] += $size
    Write-Verbose ('Remover: ' + $resolved)
}

Write-Host "Projeto: $taskRoot"
Write-Host 'Preservar: Git, fontes, testes, scripts, documentacao operacional, motor atual,'
Write-Host 'instalador final 0.28.4/checksum, sons recentes e recuperacao do SDK Tizen.'
foreach ($group in $taskSummary.Keys | Sort-Object) {
    Write-Host ('  {0}: {1:N2} GiB' -f $group, ($taskSummary[$group] / 1GB))
}
Write-Host ('Total previsto: {0:N2} GiB em {1} destinos.' -f ($taskTotalBytes / 1GB), $taskTargets.Count)
Write-Host 'A exclusao e permanente. Instalacao, dados e certificados fora do projeto nao sao alterados.'
if (!$taskTargets.Count) { Write-Host 'Nada a limpar.'; return }
if (!$PSCmdlet.ShouldProcess($taskRoot, "Excluir permanentemente $($taskTargets.Count) destinos revisados")) { return }

$taskRemoved = 0
foreach ($target in $taskTargets) {
    Remove-Item -LiteralPath $target.Path -Recurse -Force -ErrorAction Stop -Confirm:$false
    $taskRemoved++
}
Write-Host ('Limpeza concluida: {0} destinos; {1:N2} GiB removidos.' -f $taskRemoved, ($taskTotalBytes / 1GB))
Write-Host 'Caches/dependencias serao recriados na proxima compilacao pelos scripts habituais.'
