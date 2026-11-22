[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('on', 'off', 'status', 'variant')]
    [string]$Command,
    [Parameter(Position = 1)]
    [string]$Action,
    [Parameter(Position = 2)]
    [string]$Name,
    [string]$Source,
    [string]$KspRoot
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ksp-common.ps1')

$KeptInGameData = @('Squad', 'SquadExpansion', 'FLTCOM.TestHarness')
$TestModeDeploys = @('FLTCOM', 'FLTCOM.TestHarness')
$StockSaves = @('scenarios', 'training')

function Fail([string]$Message) {
    Write-Output "ksp-testmode: $Message"
    exit 1
}

function Get-ItemMeasure([string]$Path) {
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        return @{ kind = 'file'; size = (Get-Item -LiteralPath $Path -Force).Length; files = 1 }
    }
    $size = [long]0
    $count = 0
    foreach ($file in [IO.Directory]::EnumerateFiles($Path, '*', [IO.SearchOption]::AllDirectories)) {
        $size += ([IO.FileInfo]$file).Length
        $count++
    }
    return @{ kind = 'dir'; size = $size; files = $count }
}

function Read-Manifest([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }
    return [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8) | ConvertFrom-Json
}

function Write-Manifest([string]$Path, $Manifest) {
    $json = ConvertTo-Json -InputObject $Manifest -Depth 6
    [IO.File]::WriteAllText("$Path.tmp", $json)
    Move-Item -LiteralPath "$Path.tmp" -Destination $Path -Force
}

function Get-HashListing([string]$Root, [scriptblock]$Exclude) {
    $lines = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path -LiteralPath $Root)) {
        return $lines
    }
    $rootFull = (Resolve-Path -LiteralPath $Root).ProviderPath.TrimEnd('\') + '\'
    $files = [IO.Directory]::EnumerateFiles($rootFull, '*', [IO.SearchOption]::AllDirectories) | Sort-Object
    foreach ($file in $files) {
        $rel = $file.Substring($rootFull.Length)
        if ($Exclude -and (& $Exclude $rel)) {
            continue
        }
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
        $lines.Add("$hash  $rel")
    }
    return $lines
}

function Test-KeptSave($Entry) {
    return -not $Entry.PSIsContainer -or $StockSaves -contains $Entry.Name -or $Entry.Name -like 'fltcom-*'
}

function Get-FixtureFolders([string]$Saves) {
    if (Test-Path -LiteralPath $Saves) {
        Get-ChildItem -LiteralPath $Saves -Directory -Filter 'fltcom-*'
    }
}

function Test-MarkedFixture($Folder) {
    return Test-Path -LiteralPath (Join-Path $Folder.FullName $FixtureMarker)
}

function Get-ParkedSavesListing([string]$Saves, $Records) {
    $names = @(@($Records) | ForEach-Object { $_.path })
    return Get-HashListing $Saves { param($rel) $names -notcontains $rel.Split('\')[0] }.GetNewClosure()
}

function Get-Paths {
    $root = Resolve-KspRoot $KspRoot
    $parked = Join-Path $root 'FLTCOM-parked'
    return @{
        Root = $root
        GameData = Join-Path $root 'GameData'
        Saves = Join-Path $root 'saves'
        Settings = Join-Path $root 'settings.cfg'
        Parked = $parked
        ParkedGameData = Join-Path $parked 'GameData'
        ParkedSaves = Join-Path $parked 'saves'
        Manifest = Join-Path $parked 'manifest.json'
        SavesBackup = Join-Path $parked 'saves-backup'
        SettingsBackup = Join-Path $parked 'settings.cfg'
        SavesAtOn = Join-Path $parked 'saves-at-on.sha256'
        History = Join-Path $parked 'history'
    }
}

function Get-ParkRecords([string]$LiveDir, [string]$ParkedDir, [scriptblock]$Keep) {
    $records = New-Object System.Collections.Generic.List[object]
    if (Test-Path -LiteralPath $LiveDir) {
        foreach ($entry in Get-ChildItem -LiteralPath $LiveDir -Force | Sort-Object Name) {
            if (& $Keep $entry) {
                continue
            }
            if (Test-Path -LiteralPath (Join-Path $ParkedDir $entry.Name)) {
                throw "$($entry.Name) is already in $ParkedDir. Resolve it by hand before running on."
            }
            $measure = Get-ItemMeasure $entry.FullName
            $records.Add([ordered]@{ path = $entry.Name; kind = $measure.kind; size = $measure.size; files = $measure.files })
        }
    }
    return , $records.ToArray()
}

function Restore-Records($Records, [string]$LiveDir, [string]$ParkedDir, $Problems) {
    $restored = 0
    foreach ($record in $Records) {
        $parkedPath = Join-Path $ParkedDir $record.path
        $livePath = Join-Path $LiveDir $record.path
        if (Test-Path -LiteralPath $parkedPath) {
            if (Test-Path -LiteralPath $livePath) {
                $Problems.Add("conflict: $($record.path) exists in both $LiveDir and $ParkedDir")
                continue
            }
            Move-Item -LiteralPath $parkedPath -Destination $livePath
        } elseif (-not (Test-Path -LiteralPath $livePath)) {
            $Problems.Add("missing: $($record.path) is neither in $ParkedDir nor in $LiveDir")
            continue
        }
        $measure = Get-ItemMeasure $livePath
        if ($measure.size -ne [long]$record.size -or $measure.files -ne [int]$record.files) {
            $Problems.Add("size mismatch: $($record.path) has $($measure.size) bytes in $($measure.files) files, manifest says $($record.size) bytes in $($record.files) files")
            continue
        }
        $restored++
    }
    return $restored
}

function Assert-GameStopped {
    $running = Get-BlockingProcess
    if ($running) {
        $names = ($running | ForEach-Object { "$($_.ProcessName) (pid $($_.Id))" }) -join ', '
        Fail "refusing to run while $names is running. Quit it first; test mode never stops a game or CKAN it did not start."
    }
}

function Move-ToParked($Records, [string]$LiveDir, [string]$ParkedDir, [string[]]$LiveVariants, [string[]]$Deploys) {
    foreach ($record in $Records) {
        $live = Join-Path $LiveDir $record.path
        $parked = Join-Path $ParkedDir $record.path
        if ($LiveVariants -contains $record.path -or -not (Test-Path -LiteralPath $live)) {
            continue
        }
        if (Test-Path -LiteralPath $parked) {
            if ($Deploys -contains $record.path) {
                continue
            }
            throw "conflict: $($record.path) exists in both $LiveDir and $ParkedDir. Resolve it by hand, then run on again."
        }
        Move-Item -LiteralPath $live -Destination $parked
    }
}

function Move-SavesThenModsToParked($p, $Manifest) {
    $liveVariants = @(@($Manifest.variants) | Where-Object { -not $_.copied } | ForEach-Object { $_.name })
    Move-ToParked @($Manifest.saves) $p.Saves $p.ParkedSaves @() @()
    Move-ToParked @($Manifest.items) $p.GameData $p.ParkedGameData $liveVariants $TestModeDeploys
}

function Invoke-On($p) {
    Assert-GameStopped
    $unmarked = @(Get-FixtureFolders $p.Saves | Where-Object { -not (Test-MarkedFixture $_) })
    if ($unmarked.Count -gt 0) {
        Fail "refusing to run: $(($unmarked | ForEach-Object { "saves/$($_.Name)" }) -join ', ') has no $FixtureMarker marker, so it is not a test fixture, and off deletes test fixtures. Rename it, then run on again."
    }
    $steam = 'launch KSP_x64.exe directly while test mode is on, never through Steam, because Steam Cloud could sync the parked saves away.'
    $manifest = Read-Manifest $p.Manifest
    if ($manifest) {
        Move-SavesThenModsToParked $p $manifest
        Write-Output "test mode already on, $(@($manifest.items).Count) items parked, $(@($manifest.saves).Count) saves parked"
        Write-Output $steam
        return
    }
    $items = Get-ParkRecords $p.GameData $p.ParkedGameData { param($entry) $KeptInGameData -contains $entry.Name }
    $saves = Get-ParkRecords $p.Saves $p.ParkedSaves ${function:Test-KeptSave}
    foreach ($dir in $p.Parked, $p.ParkedGameData, $p.ParkedSaves, $p.History) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }

    if (-not (Test-Path -LiteralPath $p.SavesBackup)) {
        $staging = "$($p.SavesBackup).partial"
        if (Test-Path -LiteralPath $staging) {
            Fail "found an unfinished saves backup at $staging. Inspect it, then move it aside before running on again."
        }
        if (Test-Path -LiteralPath $p.Saves) {
            Copy-Item -LiteralPath $p.Saves -Destination $staging -Recurse -Force
        } else {
            New-Item -ItemType Directory -Path $staging | Out-Null
        }
        $listing = [string[]]@(Get-HashListing $staging $null)
        [IO.File]::WriteAllLines("$($p.SavesBackup).sha256", $listing)
        Move-Item -LiteralPath $staging -Destination $p.SavesBackup
        Write-Output "saves backed up to $($p.SavesBackup) ($($listing.Count) files hashed)"
    }

    [IO.File]::WriteAllLines($p.SavesAtOn, [string[]]@(Get-ParkedSavesListing $p.Saves $saves))

    $settingsBackedUp = $false
    if (Test-Path -LiteralPath $p.Settings) {
        Copy-Item -LiteralPath $p.Settings -Destination $p.SettingsBackup -Force
        $settingsBackedUp = $true
    }

    $manifest = [ordered]@{
        version = 1
        state = 'on'
        createdUtc = (Get-Date).ToUniversalTime().ToString('o')
        settingsBackedUp = $settingsBackedUp
        items = $items
        saves = $saves
        variants = @()
    }
    Write-Manifest $p.Manifest $manifest
    Move-SavesThenModsToParked $p $manifest
    Write-Output "test mode on, $($items.Count) items parked, $($saves.Count) saves parked"
    Write-Output $steam
}

function Remove-Deployed($p, [bool]$TestModeWasOn) {
    $removed = New-Object System.Collections.Generic.List[string]
    $names = if ($TestModeWasOn) { $TestModeDeploys } else { @('FLTCOM.TestHarness') }
    foreach ($name in $names) {
        $path = Join-Path $p.GameData $name
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
            $removed.Add("GameData/$name")
        }
    }
    foreach ($save in Get-FixtureFolders $p.Saves | Where-Object { Test-MarkedFixture $_ }) {
        Remove-Item -LiteralPath $save.FullName -Recurse -Force
        $removed.Add("saves/$($save.Name)")
    }
    return $removed
}

function Invoke-Off($p) {
    $manifest = Read-Manifest $p.Manifest
    Assert-GameStopped
    if ($manifest) {
        $collisions = @(@($manifest.saves) | Where-Object {
                (Test-Path -LiteralPath (Join-Path $p.ParkedSaves $_.path)) -and (Test-Path -LiteralPath (Join-Path $p.Saves $_.path))
            })
        if ($collisions.Count -gt 0) {
            foreach ($save in $collisions) {
                Write-Output "ksp-testmode: conflict: saves/$($save.path) appeared during test mode, and the operator's own $($save.path) is parked in $($p.ParkedSaves). off never overwrites or merges a save. Move saves/$($save.path) aside by hand, then run off again."
            }
            Write-Output "test mode still on, nothing restored. The manifest stays at $($p.Manifest)."
            exit 1
        }
    }

    $removed = Remove-Deployed $p ($null -ne $manifest)
    foreach ($path in $removed) {
        Write-Output "removed $path"
    }
    foreach ($folder in Get-FixtureFolders $p.Saves) {
        Write-Output "note: saves/$($folder.Name) has no $FixtureMarker marker, so it is not a test fixture and was left in place"
    }
    if (-not $manifest) {
        Write-Output 'test mode off, 0 items restored'
        return
    }

    foreach ($variant in @($manifest.variants)) {
        if ($variant.copied) {
            $path = Join-Path $p.GameData $variant.name
            if (Test-Path -LiteralPath $path) {
                Remove-Item -LiteralPath $path -Recurse -Force
                Write-Output "removed variant copy GameData/$($variant.name)"
            }
        }
    }

    $problems = New-Object System.Collections.Generic.List[string]
    $restored = Restore-Records @($manifest.items) $p.GameData $p.ParkedGameData $problems
    $savesRestored = 0
    if ($problems.Count -gt 0) {
        $problems.Add("the operator's saves stay parked in $($p.ParkedSaves) until every mod is back, because KSP deletes vessels whose parts are missing")
    } else {
        $savesRestored = Restore-Records @($manifest.saves) $p.Saves $p.ParkedSaves $problems
        if (Test-Path -LiteralPath $p.SavesAtOn) {
            $before = New-Object 'System.Collections.Generic.HashSet[string]' (, [string[]]@([IO.File]::ReadAllLines($p.SavesAtOn)))
            $after = New-Object 'System.Collections.Generic.HashSet[string]' (, [string[]]@(Get-ParkedSavesListing $p.Saves $manifest.saves))
            foreach ($line in $before) {
                if (-not $after.Contains($line)) {
                    $problems.Add("saves changed during test mode, was: $line")
                }
            }
            foreach ($line in $after) {
                if (-not $before.Contains($line)) {
                    $problems.Add("saves changed during test mode, now: $line")
                }
            }
        }
    }

    if ($manifest.settingsBackedUp -and (Test-Path -LiteralPath $p.SettingsBackup)) {
        Move-Item -LiteralPath $p.SettingsBackup -Destination $p.Settings -Force
        Write-Output 'restored settings.cfg'
    }

    if ($problems.Count -gt 0) {
        foreach ($problem in $problems) {
            Write-Output "ksp-testmode: $problem"
        }
        Write-Output "test mode still on, $restored of $(@($manifest.items).Count) items restored, $savesRestored of $(@($manifest.saves).Count) saves restored. The manifest stays at $($p.Manifest)."
        exit 1
    }

    $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
    Move-Item -LiteralPath $p.Manifest -Destination (Join-Path $p.History "manifest-$stamp.json")
    if (Test-Path -LiteralPath $p.SavesAtOn) {
        Move-Item -LiteralPath $p.SavesAtOn -Destination (Join-Path $p.History "saves-at-on-$stamp.sha256")
    }
    foreach ($dir in $p.ParkedGameData, $p.ParkedSaves) {
        if ((Test-Path -LiteralPath $dir) -and -not (Get-ChildItem -LiteralPath $dir -Force)) {
            Remove-Item -LiteralPath $dir
        }
    }
    foreach ($entry in Get-ChildItem -LiteralPath $p.GameData -Force) {
        if (($KeptInGameData -notcontains $entry.Name) -and -not (@($manifest.items) | Where-Object { $_.path -eq $entry.Name })) {
            Write-Output "note: GameData/$($entry.Name) was added during test mode and was left in place"
        }
    }
    if (Test-Path -LiteralPath $p.Saves) {
        foreach ($entry in Get-ChildItem -LiteralPath $p.Saves -Force) {
            if (-not (Test-KeptSave $entry) -and -not (@($manifest.saves) | Where-Object { $_.path -eq $entry.Name })) {
                Write-Output "note: saves/$($entry.Name) was added during test mode and was left in place"
            }
        }
    }
    Write-Output "test mode off, $restored items restored, $savesRestored saves restored"
}

function Get-EntryCount([string]$Dir) {
    if (Test-Path -LiteralPath $Dir) {
        return @(Get-ChildItem -LiteralPath $Dir -Force).Count
    }
    return 0
}

function Invoke-Status($p) {
    $manifest = Read-Manifest $p.Manifest
    $items = Get-EntryCount $p.ParkedGameData
    $saves = Get-EntryCount $p.ParkedSaves
    if ($manifest) {
        Write-Output "on, $items items parked, $saves saves parked"
        foreach ($variant in @($manifest.variants)) {
            Write-Output "variant $($variant.name)"
        }
    } elseif ($items + $saves -gt 0) {
        Write-Output "off, $items items parked, $saves saves parked with no manifest. Inspect $($p.Parked) by hand."
        exit 1
    } else {
        Write-Output 'off, 0 items parked'
    }
}

function Invoke-Variant($p) {
    if ($Action -notin @('add', 'remove') -or -not $Name) {
        Fail 'usage: ksp-testmode.ps1 variant add|remove <name> [-Source <folder>]'
    }
    $manifest = Read-Manifest $p.Manifest
    if (-not $manifest) {
        Fail 'test mode is off. Run on first.'
    }
    Assert-GameStopped
    $live = Join-Path $p.GameData $Name
    $parked = Join-Path $p.ParkedGameData $Name
    $variants = New-Object System.Collections.Generic.List[object]
    foreach ($variant in @($manifest.variants)) {
        if ($variant.name -ne $Name) {
            $variants.Add($variant)
        }
    }
    $existing = @($manifest.variants) | Where-Object { $_.name -eq $Name } | Select-Object -First 1

    if ($Action -eq 'add') {
        if ($existing) {
            Write-Output "variant $Name already added"
            return
        }
        if (Test-Path -LiteralPath $parked) {
            Move-Item -LiteralPath $parked -Destination $live
            $variants.Add([ordered]@{ name = $Name; copied = $false })
        } elseif ($Source) {
            if (Test-Path -LiteralPath $live) {
                Fail "GameData/$Name already exists"
            }
            Copy-Item -LiteralPath $Source -Destination $live -Recurse
            $variants.Add([ordered]@{ name = $Name; copied = $true })
        } else {
            Fail "$Name is not parked. Pass -Source <folder> to copy it in from elsewhere."
        }
        Write-Output "variant $Name added"
    } else {
        if (-not $existing) {
            Write-Output "variant $Name is not added"
            return
        }
        if ($existing.copied) {
            Remove-Item -LiteralPath $live -Recurse -Force
        } else {
            Move-Item -LiteralPath $live -Destination $parked
        }
        Write-Output "variant $Name removed"
    }
    $manifest.variants = $variants.ToArray()
    Write-Manifest $p.Manifest $manifest
}

try {
    $paths = Get-Paths
    switch ($Command) {
        'on' { Invoke-On $paths }
        'off' { Invoke-Off $paths }
        'status' { Invoke-Status $paths }
        'variant' { Invoke-Variant $paths }
    }
} catch {
    Fail $_.Exception.Message
}
exit 0
