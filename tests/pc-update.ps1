$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('lg-update-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$source = Join-Path $root 'nova versao & [pacote]'
$target = Join-Path $root ('OneDrive - Equipe Jo' + [char]0x00e3 + 'o\Area de Trabalho\LazyGaming (amigos)')
New-Item -ItemType Directory -Path $source,$target -Force | Out-Null
function Assert($value, $message) { if (!$value) { throw $message } }
function Manifest([string]$folder, [string]$version) {
    @{ product='LazyGaming'; version=$version; runtime='win-x64'; files=@(
        @{path='LazyGaming.Host.exe';sha256=(Get-FileHash -LiteralPath (Join-Path $folder 'LazyGaming.Host.exe')).Hash},
        @{path='payload.txt';sha256=(Get-FileHash -LiteralPath (Join-Path $folder 'payload.txt')).Hash}
    ) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $folder 'release.json') -Encoding UTF8
}
function Update([switch]$Verify) {
    $arguments = @('-NoProfile','-File',(Join-Path $source 'Atualizar-PC.ps1'),'-InstallationDirectory',$target)
    if ($Verify) { $arguments += '-VerifyOnly' }
    $oldPreference = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $result = & powershell @arguments 2>&1 | Out-String
    $code = $LASTEXITCODE; $ErrorActionPreference = $oldPreference
    if ($code -ne 0) { throw $result }; return $result
}
function Refused([scriptblock]$action, [string]$message) { $failed=$false; try { & $action | Out-Null } catch { $failed=$true }; Assert $failed $message }
try {
    # Only redirect the data directory in the fixture copy; never inspect real session/profile.
    $script = [IO.File]::ReadAllText((Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts/Atualizar-PC.ps1'))
    $needle = '$data = Join-Path ([Environment]::GetFolderPath(''LocalApplicationData'')) ''LazyGaming'''
    Assert ($script.Contains($needle)) 'fixture must redirect exactly the data directory'
    [IO.File]::WriteAllText((Join-Path $source 'Atualizar-PC.ps1'), $script.Replace($needle, '$data = Join-Path $PSScriptRoot ''fixture-data'''))
    $cs = Join-Path $root 'Fixture.cs'
    [IO.File]::WriteAllText($cs, 'class Fixture { static void Main(string[] a) { if(a.Length>0 && a[0]=="--hold") { System.Threading.Thread.Sleep(30000); return; } System.Console.WriteLine("0.18.0"); } }')
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    & $compiler /nologo /target:exe ('/out:' + (Join-Path $source 'LazyGaming.Host.exe')) $cs
    Assert ($LASTEXITCODE -eq 0) 'fixture compile'
    Copy-Item -LiteralPath (Join-Path $source 'LazyGaming.Host.exe') -Destination $target
    Set-Content -LiteralPath (Join-Path $source 'payload.txt') -Value 'new'
    Set-Content -LiteralPath (Join-Path $target 'payload.txt') -Value 'old'
    Manifest $source '0.18.0'; Manifest $target '0.17.0'
    Update -Verify | Out-Null
    Set-Content -LiteralPath (Join-Path $source 'payload.txt') -Value 'corrupt'
    Refused { Update -Verify } 'corrupt payload must fail before replacement'
    Assert ((Get-Content -LiteralPath (Join-Path $target 'payload.txt')) -eq 'old') 'old files intact after rejection'
    Set-Content -LiteralPath (Join-Path $source 'payload.txt') -Value 'new'; Manifest $source '0.18.0'
    New-Item -ItemType Directory -Path (Join-Path $source 'fixture-data') | Out-Null
    Set-Content -LiteralPath (Join-Path $source 'fixture-data/session.json') -Value '{}'
    Refused { Update } 'pending session must block'
    Remove-Item -LiteralPath (Join-Path $source 'fixture-data/session.json')
    $fixture = Start-Process -FilePath (Join-Path $target 'LazyGaming.Host.exe') -ArgumentList '--hold' -WindowStyle Hidden -PassThru
    try { Refused { Update } 'running verified helper must block' }
    finally { if (!$fixture.HasExited) { $fixture.Kill(); $fixture.WaitForExit() }; $fixture.Dispose() }
    Update | Out-Null
    Assert ((Get-Content -LiteralPath (Join-Path $target 'payload.txt')) -eq 'new') 'new payload installed'
    Assert (@(Get-ChildItem -LiteralPath (Split-Path -Parent $target) -Directory -Filter 'LazyGaming (amigos).backup-*').Count -eq 1) 'previous version retained'
    Refused { Update -Verify } 'same version must fail'
    # Trigger the post-swap version failure: valid hashes but an exe claiming the old version.
    Set-Content -LiteralPath (Join-Path $source 'payload.txt') -Value 'next'
    Manifest $source '0.19.0'
    Refused { Update } 'failed version smoke must rollback'
    Assert ((Get-Content -LiteralPath (Join-Path $target 'payload.txt')) -eq 'new') 'rollback restores previous payload'
    Assert ((Get-Content -LiteralPath (Join-Path $target 'release.json') -Raw | ConvertFrom-Json).version -eq '0.18.0') 'rollback restores manifest'
    $originalTarget = $target
    $junction = Join-Path $root 'linked-install'
    New-Item -ItemType Junction -Path $junction -Target $target | Out-Null
    try {
        $target = $junction
        Refused { Update -Verify } 'linked cloud-style installation must be refused safely'
    } finally {
        $target = $originalTarget
        [IO.Directory]::Delete($junction)
    }
    $manifest = Get-Content -LiteralPath (Join-Path $source 'release.json') -Raw | ConvertFrom-Json
    $manifest.files[1].path = '../escape.txt'; $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $source 'release.json')
    Refused { Update -Verify } 'traversal must fail'
    Write-Output 'PASS: isolated updater integrity/path/session/process guards, cloud-style Unicode paths, safe junction refusal, successful upgrade, backup and rollback.'
} finally {
    $full = [IO.Path]::GetFullPath($root)
    if (!$full.StartsWith([IO.Path]::GetTempPath(),[StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $full) -notlike 'lg-update-*') { throw 'Unexpected fixture cleanup path' }
    Remove-Item -LiteralPath $full -Recurse -Force
}
