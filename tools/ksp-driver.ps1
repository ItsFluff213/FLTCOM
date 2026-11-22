[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('deploy', 'boot', 'load', 'state', 'screenshot', 'switch', 'warp', 'log-tail', 'quit', 'wait', 'kill', 'make-fixture')]
    [string]$Command,
    [Parameter(Position = 1)]
    [string]$Arg,
    [switch]$Menu,
    [switch]$Json,
    [string]$Out,
    [int]$TimeoutSeconds,
    [string]$KspRoot
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ksp-common.ps1')

$RepoRoot = Split-Path -Parent $PSScriptRoot
$FixtureRoot = Join-Path $RepoRoot 'tests\fixtures\saves'
$Utf8 = New-Object System.Text.UTF8Encoding($false)

function Fail([string]$Message, [int]$Code = 1) {
    Write-Output "ksp-driver: $Message"
    exit $Code
}

function Get-Paths {
    $root = Resolve-KspRoot $KspRoot
    $harness = Join-Path $root 'GameData\FLTCOM.TestHarness'
    $folder = Join-Path $harness 'PluginData\harness'
    return @{
        Root = $root
        Exe = Join-Path $root 'KSP_x64.exe'
        Manifest = Join-Path $root 'FLTCOM-parked\manifest.json'
        GameData = Join-Path $root 'GameData'
        Saves = Join-Path $root 'saves'
        Log = Join-Path $root 'KSP.log'
        Harness = $harness
        HarnessDll = Join-Path $harness 'FLTCOM.TestHarness.dll'
        Folder = $folder
        Commands = Join-Path $folder 'cmd.jsonl'
        Output = Join-Path $folder 'out.jsonl'
        Boot = Join-Path $folder 'boot.cfg'
        Recipes = Join-Path $folder 'recipes.cfg'
        Pid = Join-Path $folder 'ksp.pid'
    }
}

function Assert-TestModeOn($p) {
    if (-not (Test-Path -LiteralPath $p.Manifest)) {
        Fail 'test mode is off. Run tools/ksp-testmode.ps1 on first; the driver never starts KSP with the operator''s mods in GameData.'
    }
}

function Get-OwnKsp($p) {
    if (-not (Test-Path -LiteralPath $p.Pid)) {
        return $null
    }
    $record = Get-Content -LiteralPath $p.Pid -Raw | ConvertFrom-Json
    $process = Get-Process -Id $record.pid -ErrorAction SilentlyContinue
    if (-not $process -or $process.ProcessName -ne 'KSP_x64' -or $process.StartTime.ToUniversalTime().ToString('o') -ne $record.started) {
        return $null
    }
    return $process
}

function Read-Replies($p) {
    if (-not (Test-Path -LiteralPath $p.Output)) {
        return @()
    }
    $stream = [IO.File]::Open($p.Output, 'Open', 'Read', 'ReadWrite, Delete')
    try {
        $reader = New-Object IO.StreamReader($stream, $Utf8)
        $text = $reader.ReadToEnd()
    } finally {
        $stream.Dispose()
    }
    $replies = @()
    foreach ($line in $text -split "`n") {
        if ($line.Trim()) {
            try {
                $replies += , ($line | ConvertFrom-Json)
            } catch {
                continue
            }
        }
    }
    return $replies
}

function Wait-Reply($p, [string]$Id, [int]$Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        $reply = Read-Replies $p | Where-Object { $_.id -eq $Id -and ($_.event -eq 'ready' -or $_.PSObject.Properties.Name -notcontains 'event') } | Select-Object -Last 1
        if ($reply) {
            return $reply
        }
        if (-not (Get-OwnKsp $p)) {
            $tail = if (Test-Path -LiteralPath $p.Log) { (Get-Content -LiteralPath $p.Log -Tail 15) -join "`n" } else { 'no KSP.log' }
            Fail "KSP is not running (no process from ksp.pid). Last KSP.log lines:`n$tail"
        }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

function Send-Command($p, [string]$Name, [string]$Argument) {
    $id = [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $line = ConvertTo-Json -Compress -InputObject ([ordered]@{ id = $id; cmd = $Name; arg = $Argument })
    [IO.File]::AppendAllText($p.Commands, "$line`n", $Utf8)
    return $id
}

function Write-Reply($reply, [string]$Id) {
    if (-not $reply) {
        Fail "no answer yet for command $Id. Run: tools/ksp-driver.ps1 wait $Id" 3
    }
    if ($Json) {
        Write-Output (ConvertTo-Json -InputObject $reply -Depth 8 -Compress)
    }
    if (-not $reply.ok) {
        Fail "harness error: $($reply.msg)"
    }
}

function Write-State($data) {
    $ut = if ($null -ne $data.ut) { [Math]::Round([double]$data.ut, 1) } else { '-' }
    Write-Output "scene=$($data.scene) save=$($data.save) ut=$ut active=$($data.active)"
    foreach ($v in @($data.vessels)) {
        $orbit = if ($null -ne $v.peA) { "pe=$([Math]::Round($v.peA / 1000, 1))km ap=$([Math]::Round($v.apA / 1000, 1))km inc=$([Math]::Round($v.inc, 2)) period=$([Math]::Round($v.period, 1))s" } else { '' }
        $marker = if ($v.active) { '*' } else { '-' }
        $distance = if ($null -ne $v.distanceKm) { "dist=$($v.distanceKm)km" } else { '' }
        Write-Output ("$marker $($v.name) | $($v.type) | $($v.situation) | $($v.body) | $orbit | signal=$($v.signal) $distance").TrimEnd()
    }
}

function Copy-Fixture($p, [string]$Fixture) {
    $source = Join-Path $FixtureRoot $Fixture
    if (-not (Test-Path -LiteralPath (Join-Path $source 'persistent.sfs'))) {
        Fail "no fixture '$Fixture' in $FixtureRoot"
    }
    $target = Join-Path $p.Saves "fltcom-$Fixture"
    if (Test-Path -LiteralPath $target) {
        if (-not (Test-Path -LiteralPath (Join-Path $target $FixtureMarker))) {
            Fail "saves/fltcom-$Fixture has no $FixtureMarker marker, so it is not a test fixture. The driver never replaces it."
        }
        Remove-Item -LiteralPath $target -Recurse -Force
    }
    Copy-Item -LiteralPath $source -Destination $target -Recurse
    [IO.File]::WriteAllText((Join-Path $target $FixtureMarker), '')
}

function Invoke-Deploy($p) {
    Assert-TestModeOn $p
    if (Get-BlockingProcess) {
        Fail 'quit KSP and CKAN before deploying'
    }
    $pairs = @(
        @{ From = Join-Path $RepoRoot 'dist\GameData\FLTCOM'; To = Join-Path $p.GameData 'FLTCOM' },
        @{ From = Join-Path $RepoRoot 'dist-test\GameData\FLTCOM.TestHarness'; To = $p.Harness }
    )
    foreach ($pair in $pairs) {
        if (-not (Test-Path -LiteralPath $pair.From)) {
            Fail "$($pair.From) is missing. Run tools/build.ps1 first."
        }
        if (Test-Path -LiteralPath $pair.To) {
            Remove-Item -LiteralPath $pair.To -Recurse -Force
        }
        Copy-Item -LiteralPath $pair.From -Destination $pair.To -Recurse
        Write-Output "deployed $($pair.To)"
    }
}

function Invoke-Boot($p) {
    Assert-TestModeOn $p
    if (-not (Test-Path -LiteralPath $p.HarnessDll)) {
        Fail "the harness is not deployed at $($p.Harness). Run tools/ksp-driver.ps1 deploy."
    }
    $running = Get-BlockingProcess
    if ($running) {
        Fail "KSP or CKAN is already running (pid $(($running | ForEach-Object Id) -join ', ')). One KSP at a time."
    }
    if (-not $Menu -and -not $Arg) {
        Fail 'usage: ksp-driver.ps1 boot <fixture> | boot -Menu'
    }
    New-Item -ItemType Directory -Force -Path $p.Folder | Out-Null
    if (-not $Menu) {
        Copy-Fixture $p $Arg
    }
    Copy-Item -LiteralPath (Join-Path $FixtureRoot 'recipes.cfg') -Destination $p.Recipes -Force
    [IO.File]::WriteAllText($p.Commands, '', $Utf8)
    [IO.File]::WriteAllText($p.Output, '', $Utf8)
    $boot = if ($Menu) { "menu = true`n" } else { "fixture = $Arg`n" }
    [IO.File]::WriteAllText($p.Boot, $boot, $Utf8)

    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $p.Exe -WorkingDirectory $p.Root -PassThru
    $record = [ordered]@{ pid = $process.Id; started = $process.StartTime.ToUniversalTime().ToString('o') }
    [IO.File]::WriteAllText($p.Pid, (ConvertTo-Json -InputObject $record -Compress), $Utf8)
    Write-Output "started KSP_x64 pid $($process.Id)"

    $seconds = if ($TimeoutSeconds) { $TimeoutSeconds } else { 540 }
    $reply = Wait-Reply $p 'boot' $seconds
    if (-not $reply) {
        Fail "KSP did not report ready within $seconds s. KSP is still running; check KSP.log, then run quit or kill." 3
    }
    Write-Reply $reply 'boot'
    Write-Output "$($reply.msg) boot=$([Math]::Round($watch.Elapsed.TotalSeconds, 1))s"
}

function Invoke-Session($p, [string]$Name, [string]$Argument, [int]$DefaultSeconds) {
    if (-not (Get-OwnKsp $p)) {
        Fail 'no KSP started by this driver is running. Run boot first.'
    }
    $id = Send-Command $p $Name $Argument
    $seconds = if ($TimeoutSeconds) { $TimeoutSeconds } else { $DefaultSeconds }
    $reply = Wait-Reply $p $id $seconds
    Write-Reply $reply $id
    return $reply
}

function Invoke-Quit($p) {
    $process = Get-OwnKsp $p
    if (-not $process) {
        Write-Output 'KSP is not running'
        return
    }
    Invoke-Session $p 'quit' '' 30 | Out-Null
    if (-not $process.WaitForExit(30000)) {
        Fail "KSP pid $($process.Id) did not exit within 30 s. Run tools/ksp-driver.ps1 kill." 3
    }
    Remove-Item -LiteralPath $p.Pid -Force
    Write-Output 'KSP exited'
}

function Invoke-Kill($p) {
    $process = Get-OwnKsp $p
    if (-not $process) {
        Write-Output 'no KSP started by this driver is running; nothing to kill'
        return
    }
    $process.Kill()
    $process.WaitForExit(30000) | Out-Null
    Remove-Item -LiteralPath $p.Pid -Force
    Write-Output "killed KSP pid $($process.Id)"
}

try {
    $paths = Get-Paths
    switch ($Command) {
        'deploy' { Invoke-Deploy $paths }
        'boot' { Invoke-Boot $paths }
        'load' {
            if (-not $Arg) { Fail 'usage: ksp-driver.ps1 load <fixture>' }
            Copy-Fixture $paths $Arg
            $watch = [Diagnostics.Stopwatch]::StartNew()
            $reply = Invoke-Session $paths 'load' $Arg 30
            Write-Output "$($reply.msg) load=$([Math]::Round($watch.Elapsed.TotalSeconds, 1))s"
        }
        'state' {
            $reply = Invoke-Session $paths 'state' '' 30
            if (-not $Json) { Write-State $reply.data }
        }
        'screenshot' {
            if (-not $Arg) { Fail 'usage: ksp-driver.ps1 screenshot <slug> [-Out <file.png>]' }
            $reply = Invoke-Session $paths 'screenshot' $Arg 30
            $path = $reply.data.path
            if ($Out) {
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Out) | Out-Null
                Copy-Item -LiteralPath $path -Destination $Out -Force
                $path = (Resolve-Path -LiteralPath $Out).ProviderPath
            }
            Write-Output "screenshot $path"
        }
        'switch' {
            if (-not $Arg) { Fail 'usage: ksp-driver.ps1 switch <vessel name or id>' }
            $reply = Invoke-Session $paths 'switch' $Arg 30
            Write-Output $reply.msg
        }
        'warp' {
            if (-not $Arg) { Fail 'usage: ksp-driver.ps1 warp <ut> | +<seconds> | +<n>orbit' }
            $reply = Invoke-Session $paths 'warp' $Arg 30
            $target = [double]$reply.data.to
            Write-Output "warping from ut=$([Math]::Round([double]$reply.data.from, 1)) to ut=$([Math]::Round($target, 1))"
            $deadline = (Get-Date).AddSeconds(25)
            do {
                Start-Sleep -Seconds 2
                $ut = [double](Invoke-Session $paths 'state' '' 10).data.ut
            } while ($ut -lt $target - 1 -and (Get-Date) -lt $deadline)
            if ($ut -ge $target - 1) {
                Write-Output "warp done ut=$([Math]::Round($ut, 1))"
            } else {
                Write-Output "warp running ut=$([Math]::Round($ut, 1)) target=$([Math]::Round($target, 1)); run state to follow it"
            }
        }
        'log-tail' {
            $reply = Invoke-Session $paths 'log-tail' $Arg 30
            if (-not $Json) { $reply.data.lines | ForEach-Object { Write-Output $_ } }
        }
        'wait' {
            if (-not $Arg) { Fail 'usage: ksp-driver.ps1 wait <id>' }
            $reply = Wait-Reply $paths $Arg $(if ($TimeoutSeconds) { $TimeoutSeconds } else { 30 })
            Write-Reply $reply $Arg
            Write-Output $reply.msg
        }
        'quit' { Invoke-Quit $paths }
        'kill' { Invoke-Kill $paths }
        'make-fixture' {
            if (-not $Arg) { Fail 'usage: ksp-driver.ps1 make-fixture <name>' }
            Copy-Item -LiteralPath (Join-Path $FixtureRoot 'recipes.cfg') -Destination $paths.Recipes -Force
            $reply = Invoke-Session $paths 'make-fixture' $Arg 300
            $target = Join-Path $FixtureRoot $Arg
            New-Item -ItemType Directory -Force -Path $target | Out-Null
            Copy-Item -LiteralPath (Join-Path $paths.Saves "fltcom-$Arg\persistent.sfs") -Destination (Join-Path $target 'persistent.sfs') -Force
            Write-Output "fixture $Arg written to $target ($($reply.msg))"
        }
    }
} catch {
    Fail $_.Exception.Message
}
exit 0
