$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    & "$PSScriptRoot/build.ps1"
    New-Item -ItemType Directory -Force test-results | Out-Null
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    & $compiler /nologo /target:exe /out:build\FakeSmartctl.exe tests\FakeSmartctl.cs
    if ($LASTEXITCODE -ne 0) { throw 'Fake smartctl compilation failed.' }
    & $compiler /nologo /target:exe /out:build\Tests.exe /reference:build\SsdTemperatureTray.exe /reference:System.Drawing.dll tests\Tests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & ./build/Tests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
} finally { Pop-Location }
