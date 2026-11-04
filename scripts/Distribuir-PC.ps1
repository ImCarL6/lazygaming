param([string]$RuntimeIdentifier = 'win-x64', [switch]$FrameworkDependent)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'src/LazyGaming.Host/LazyGaming.Host.csproj')
$version = [string]$project.Project.PropertyGroup.Version
$root = Join-Path $projectRoot ('outputs/distribution/' + $version + '-' + $RuntimeIdentifier + '-' + [guid]::NewGuid().ToString('N'))
$package = Join-Path $root 'LazyGaming'
New-Item -ItemType Directory -Path $package -Force | Out-Null
& (Join-Path $PSScriptRoot 'Publicar-PC.ps1') -OutputDirectory $package -SelfContained:(!$FrameworkDependent) -RuntimeIdentifier $RuntimeIdentifier
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Atualizar-PC.ps1') -Destination (Join-Path $package 'Atualizar-PC.ps1')
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/PC-PARA-AMIGOS.md') -Destination (Join-Path $package 'LEIA-ME.md')
$tvKit = Join-Path $package 'TV-opcional'
New-Item -ItemType Directory -Path (Join-Path $tvKit 'scripts') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'tv') -Destination (Join-Path $tvKit 'tv') -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Empacotar-TV.ps1') -Destination (Join-Path $tvKit 'scripts/Empacotar-TV.ps1')
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE'),(Join-Path $projectRoot 'THIRD_PARTY.md') -Destination $tvKit
# Include the matching modifiable sources/build scripts, without Git/private state.
$sourceStage = Join-Path $root 'LazyGaming-source'
New-Item -ItemType Directory -Path $sourceStage | Out-Null
$sourceFiles = & git -C $projectRoot ls-files --cached --others --exclude-standard
if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel enumerar as fontes publicas.' }
foreach ($file in $sourceFiles) {
    if ($file -notmatch '^(src|tests|tv|scripts|docs)/' -and $file -notin @('LazyGaming.slnx','global.json','requirements-dev.txt','README.md','LICENSE','THIRD_PARTY.md')) { continue }
    $destination = Join-Path $sourceStage $file
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $destination
}
Compress-Archive -LiteralPath $sourceStage -DestinationPath (Join-Path $package 'LazyGaming-source.zip') -CompressionLevel Optimal
$upstream = Join-Path $projectRoot 'outputs/upstream/moonlight-source-1.19.0'
if (!(Test-Path -LiteralPath $upstream)) {
    & git clone --depth 1 --branch v1.19.0 --recurse-submodules --shallow-submodules https://github.com/brightcraft/moonlight-tizen.git $upstream
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao obter as fontes correspondentes do motor.' }
}
$upstreamCommit = (& git -C $upstream rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $upstreamCommit -ne '4bb3134fd1a7ecfb5eb556b4f09583006748d180') { throw 'As fontes do motor divergem da revisao fixada.' }
& git -C $upstream archive HEAD --format=zip --prefix=Moonlight-1.19.0/ -o (Join-Path $package 'Moonlight-1.19.0-source.zip')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao preparar o arquivo de fontes do motor.' }
Set-Content -LiteralPath (Join-Path $package 'SOURCES.txt') -Encoding Ascii -Value "LazyGaming $version`: LazyGaming-source.zip (current sources and build/test/package scripts).", "Moonlight Tizen 1.19.0: Moonlight-1.19.0-source.zip, commit $upstreamCommit; upstream Dockerfile/CMake/ports and vendored libraries included.", 'No private certificates, pairings, libraries, game files or saves are included.'
$files = @(Get-ChildItem -LiteralPath $package -Recurse -File | ForEach-Object {
    [ordered]@{ path = $_.FullName.Substring($package.Length + 1).Replace('\','/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
@{ product = 'LazyGaming'; version = $version; runtime = $RuntimeIdentifier; selfContained = !$FrameworkDependent; files = $files } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $package 'release.json') -Encoding UTF8
$archive = Join-Path $root ('LazyGaming-' + $version + '-' + $RuntimeIdentifier + '.zip')
Compress-Archive -LiteralPath $package -DestinationPath $archive -CompressionLevel Optimal
(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath ($archive + '.sha256') -Encoding Ascii
Write-Output "Pacote para compartilhar: $archive"
