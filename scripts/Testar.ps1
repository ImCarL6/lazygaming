param([switch]$SkipRestore)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    if (!$SkipRestore) {
        dotnet restore LazyGaming.slnx --packages outputs/nuget
        if ($LASTEXITCODE -ne 0) { throw 'Falha na restauração.' }
    }
    dotnet build LazyGaming.slnx --no-restore -m:1 -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Falha no build.' }
    foreach ($project in @('tests/Core.Tests', 'tests/Host.Tests')) {
        dotnet run --project $project --no-build
        if ($LASTEXITCODE -ne 0) { throw "Falha em $project" }
    }
    foreach ($test in Get-ChildItem tests -Filter '*.mjs' | Sort-Object Name) {
        node $test.FullName
        if ($LASTEXITCODE -ne 0) { throw "Falha em $($test.Name)" }
    }
    python tests/moonlight-adaptation.py
    if ($LASTEXITCODE -ne 0) { throw 'Falha na adaptação Moonlight.' }
    Write-Output 'PASS: build e testes isolados; nenhum teste físico de TV ou gameplay foi executado.'
} finally { Pop-Location }
