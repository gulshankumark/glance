<#
.SYNOPSIS
  Builds the Glance installer: publishes the service (self-contained, win-x64, web UI and
  themes included) and compiles installer\glance.iss with Inno Setup 6.

.EXAMPLE
  ./installer/build.ps1 -Version 1.2.3
  Output: out/Glance-Setup-1.2.3.exe
#>
[CmdletBinding()]
param(
    [string]$Version = '0.0.0-dev'
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' is not of the form x.y.z or x.y.z-suffix."
}

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$out = Join-Path $root 'out'
$publish = Join-Path $out 'publish'

Push-Location $root
try {
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

    # Builds the web UI too (npm, via the BuildWebUi target) and copies themes\.
    dotnet publish src/Glance.Service -c Release -r win-x64 --self-contained "-p:Version=$Version" -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

    foreach ($required in 'Glance.Service.exe', 'wwwroot\index.html', 'themes') {
        if (-not (Test-Path (Join-Path $publish $required))) {
            throw "Publish output is missing $required."
        }
    }

    $iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
    if (-not $iscc) {
        $default = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
        if (Test-Path $default) { $iscc = $default }
    }
    if (-not $iscc) {
        throw "Inno Setup 6 compiler (ISCC.exe) not found on PATH or at 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'. Install Inno Setup 6.3 or newer from https://jrsoftware.org/isinfo.php. The published files are in $publish."
    }

    & $iscc "/DAppVersion=$Version" "/DSourceDir=$publish" "/O$out" (Join-Path $root 'installer\glance.iss')
    if ($LASTEXITCODE -ne 0) { throw "iscc failed (exit $LASTEXITCODE)." }

    Write-Host "Installer: $(Join-Path $out "Glance-Setup-$Version.exe")"
}
finally {
    Pop-Location
}
