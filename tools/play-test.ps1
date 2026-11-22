[CmdletBinding()]
param(
    [switch]$Off,
    [string]$KspRoot
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ksp-common.ps1')

$RepoRoot = Split-Path -Parent $PSScriptRoot
$FixtureRoot = Join-Path $RepoRoot 'tests\fixtures\saves'
$Shell = Join-Path $PSHOME 'powershell.exe'

function Fail([string]$Message) {
    Write-Output "play-test: $Message"
    exit 1
}

function Invoke-Tool([string]$Script, [string[]]$Arguments) {
    $all = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot $Script)) + $Arguments
    if ($KspRoot) {
        $all += @('-KspRoot', $KspRoot)
    }
    & $Shell @all
    if ($LASTEXITCODE -ne 0) {
        Fail "$Script $($Arguments -join ' ') failed"
    }
}

if (Get-BlockingProcess) {
    Fail 'quit KSP and CKAN first'
}

if ($Off) {
    Invoke-Tool 'ksp-testmode.ps1' @('off')
    Write-Output 'play-test: your mods and saves are back. Launch KSP however you like.'
    exit 0
}

$root = Resolve-KspRoot $KspRoot
Invoke-Tool 'ksp-testmode.ps1' @('on')

& $Shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) {
    Fail 'build failed; test mode stays on, run tools/play-test.ps1 -Off to restore'
}
Invoke-Tool 'ksp-driver.ps1' @('deploy')

$saves = Join-Path $root 'saves'
$installed = foreach ($fixture in Get-ChildItem -LiteralPath $FixtureRoot -Directory) {
    if (-not (Test-Path -LiteralPath (Join-Path $fixture.FullName 'persistent.sfs'))) {
        continue
    }
    $target = Join-Path $saves "fltcom-$($fixture.Name)"
    if (Test-Path -LiteralPath $target) {
        if (-not (Test-Path -LiteralPath (Join-Path $target $FixtureMarker))) {
            Fail "saves/fltcom-$($fixture.Name) is not a test fixture; rename it and run again"
        }
        Remove-Item -LiteralPath $target -Recurse -Force
    }
    Copy-Item -LiteralPath $fixture.FullName -Destination $target -Recurse
    [IO.File]::WriteAllText((Join-Path $target $FixtureMarker), '')
    "fltcom-$($fixture.Name)"
}

$port = 47800
$settings = Join-Path $root 'GameData\FLTCOM\PluginData\settings.cfg'
if (Test-Path -LiteralPath $settings) {
    $match = Select-String -LiteralPath $settings -Pattern 'port\s*=\s*(\d+)' | Select-Object -First 1
    if ($match) {
        $port = [int]$match.Matches[0].Groups[1].Value
    }
}

Write-Output ''
Write-Output "play-test ready. Saves installed: $($installed -join ', ')"
Write-Output "1. Launch $(Join-Path $root 'KSP_x64.exe') directly, NOT through Steam (Steam Cloud could sync your parked saves away)."
Write-Output '2. Resume Game > fltcom-fleet-demo.'
Write-Output "3. Open http://127.0.0.1:$port/ in a browser, or click the FLTCOM toolbar button in game."
Write-Output 'When done, quit KSP and run: tools/play-test.ps1 -Off'
