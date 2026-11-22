$BlockingProcessNames = @('KSP_x64', 'CKAN')
$FixtureMarker = '.fltcom-fixture'

function Resolve-KspRoot([string]$Explicit) {
    $root = $Explicit
    if (-not $root) {
        $root = $env:KSP_ROOT
    }
    if (-not $root) {
        $root = Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\Kerbal Space Program'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $root 'GameData\Squad'))) {
        throw "no KSP install at '$root'. Pass -KspRoot or set KSP_ROOT to a KSP 1.12.5 folder that contains GameData\Squad."
    }
    return (Resolve-Path -LiteralPath $root).ProviderPath
}

function Get-BlockingProcess {
    return @(Get-Process -Name $BlockingProcessNames -ErrorAction SilentlyContinue)
}
