param([switch]$Installer, [string]$IsccPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'build'
New-Item -ItemType Directory -Force $out | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (!(Test-Path $compiler)) { throw '.NET Framework 4.x C# compiler is required.' }
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ "/out:$out\SsdTemperatureTray.exe" "/win32manifest:$root\src\app.manifest" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "$root\src\SsdTemperatureTray.cs"
if ($LASTEXITCODE -ne 0) { throw 'C# build failed.' }
Copy-Item "$root/src/SsdTemperatureTray.exe.config" $out
if ($Installer) {
    if (!$IsccPath) {
        $candidates = @("${env:ProgramFiles(x86)}/Inno Setup 6/ISCC.exe", "$env:LOCALAPPDATA/Programs/Inno Setup 6/ISCC.exe")
        $IsccPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if (!$IsccPath) { throw 'Install Inno Setup 6 or supply -IsccPath.' }
    & $IsccPath "$root/installer/setup.iss"
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    Get-FileHash "$root/dist/SsdTemperatureTray-Setup-1.0.3.exe" -Algorithm SHA256 | Format-List
}
