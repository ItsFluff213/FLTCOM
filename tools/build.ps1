[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$package = Join-Path $root 'dist/GameData/FLTCOM'
$web = Join-Path $package 'web'

function Invoke-Native {
    param([string]$Name, [scriptblock]$Command)
    Write-Host "== $Name"
    $global:LASTEXITCODE = 0
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

Push-Location $root
try {
    foreach ($output in 'dist', 'dist-test') {
        if (Test-Path (Join-Path $root $output)) {
            Remove-Item -Recurse -Force (Join-Path $root $output)
        }
    }
    if ($Clean) {
        foreach ($folder in 'src', 'tests') {
            Get-ChildItem -Path (Join-Path $root $folder) -Directory | ForEach-Object {
                foreach ($name in 'bin', 'obj') {
                    $path = Join-Path $_.FullName $name
                    if (Test-Path $path) {
                        Remove-Item -Recurse -Force $path
                    }
                }
            }
        }
    }

    Invoke-Native 'dotnet build' { dotnet build FLTCOM.sln -c $Configuration --nologo -v:minimal }

    if (-not (Test-Path (Join-Path $root 'display/node_modules'))) {
        Invoke-Native 'npm ci' { npm --prefix display ci --no-audit --no-fund }
    }
    Invoke-Native 'display build' { npm --prefix display run build }

    Invoke-Native 'fonts' { node tools/copy-fonts.mjs $package }

    Invoke-Native 'package check' { node tools/check-package.mjs $package }

    Write-Host 'FLTCOM build ok'
}
finally {
    Pop-Location
}
