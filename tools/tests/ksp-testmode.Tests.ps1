BeforeAll {
    $script:TestMode = Join-Path $PSScriptRoot '..\ksp-testmode.ps1'
    . (Join-Path $PSScriptRoot '..\ksp-common.ps1')

    function New-FakeInstall {
        $root = Join-Path $TestDrive ("KSP-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
        $files = @{
            'GameData\Squad\Parts\part.cfg' = 'stock part'
            'GameData\SquadExpansion\MakingHistory\x.cfg' = 'dlc'
            'GameData\ModA\Plugins\ModA.dll' = 'mod a binary'
            'GameData\ModA\ModA.cfg' = 'mod a config'
            'GameData\Mod B\readme.txt' = 'mod with a space'
            'GameData\ModuleManager.4.2.3.dll' = 'mm'
            'GameData\ModuleManager.ConfigCache' = 'cache'
            'saves\career\persistent.sfs' = 'career save'
            'saves\sandbox\persistent.sfs' = 'sandbox save'
            'settings.cfg' = 'SCREEN_RESOLUTION_WIDTH = 1920'
            'KSP_x64.exe' = 'not a real exe'
        }
        foreach ($rel in $files.Keys) {
            $path = Join-Path $root $rel
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
            [IO.File]::WriteAllText($path, $files[$rel])
        }
        return $root
    }

    function Get-Snapshot([string]$Root, [switch]$WithParked) {
        Get-ChildItem -LiteralPath $Root -Recurse -File -Force |
            Where-Object { $WithParked -or $_.FullName -notlike "*\FLTCOM-parked\*" } |
            ForEach-Object { "$($_.FullName.Substring($Root.Length)) $((Get-FileHash -LiteralPath $_.FullName).Hash)" } |
            Sort-Object
    }

    function New-File([string]$Path, [string]$Text = 'data') {
        New-Item -ItemType Directory -Force (Split-Path -Parent $Path) | Out-Null
        [IO.File]::WriteAllText($Path, $Text)
    }

    function New-Fixture([string]$Root, [string]$Name) {
        New-File (Join-Path $Root "saves\fltcom-$Name\persistent.sfs") 'fixture'
        New-File (Join-Path $Root "saves\fltcom-$Name\$FixtureMarker") ''
    }

    function Invoke-TestMode {
        $output = & $script:TestMode @args 2>&1 | ForEach-Object { "$_" }
        return [pscustomobject]@{ Code = $LASTEXITCODE; Output = $output; Text = ($output -join "`n") }
    }
}

Describe 'ksp-testmode.ps1' {
    BeforeEach {
        $root = New-FakeInstall
        $before = Get-Snapshot $root
    }

    It 'on parks every mod except Squad and SquadExpansion and writes the manifest' {
        $result = Invoke-TestMode on -KspRoot $root
        $result.Code | Should -Be 0
        $result.Text | Should -Match 'test mode on, 4 items parked, 2 saves parked'
        $result.Text | Should -Match 'launch KSP_x64.exe directly while test mode is on, never through Steam'

        (Get-ChildItem (Join-Path $root 'GameData')).Name | Sort-Object | Should -Be @('Squad', 'SquadExpansion')
        (Get-ChildItem (Join-Path $root 'FLTCOM-parked\GameData')).Name | Sort-Object |
            Should -Be @('Mod B', 'ModA', 'ModuleManager.4.2.3.dll', 'ModuleManager.ConfigCache')

        $manifest = Get-Content (Join-Path $root 'FLTCOM-parked\manifest.json') -Raw | ConvertFrom-Json
        $manifest.items.Count | Should -Be 4
        ($manifest.items | Where-Object path -EQ 'ModA').size | Should -Be ('mod a binary'.Length + 'mod a config'.Length)
        ($manifest.items | Where-Object path -EQ 'ModA').files | Should -Be 2
    }

    It 'on parks every save and off brings them back' {
        $result = Invoke-TestMode on -KspRoot $root
        $result.Code | Should -Be 0
        @(Get-ChildItem (Join-Path $root 'saves')).Count | Should -Be 0
        Get-Content (Join-Path $root 'FLTCOM-parked\saves\career\persistent.sfs') | Should -Be 'career save'

        $manifest = Get-Content (Join-Path $root 'FLTCOM-parked\manifest.json') -Raw | ConvertFrom-Json
        $manifest.saves.path | Sort-Object | Should -Be @('career', 'sandbox')
        ($manifest.saves | Where-Object path -EQ 'career').size | Should -Be 'career save'.Length
        ($manifest.saves | Where-Object path -EQ 'career').files | Should -Be 1

        $result = Invoke-TestMode off -KspRoot $root
        $result.Code | Should -Be 0
        $result.Text | Should -Match 'test mode off, 4 items restored, 2 saves restored'
        Get-Snapshot $root | Should -Be $before
        Test-Path (Join-Path $root 'FLTCOM-parked\saves') | Should -BeFalse
    }

    It 'on leaves fltcom-* saves in place' {
        New-Fixture $root 'lko-single'
        Invoke-TestMode on -KspRoot $root | Out-Null
        (Get-ChildItem (Join-Path $root 'saves')).Name | Should -Be @('fltcom-lko-single')
        $manifest = Get-Content (Join-Path $root 'FLTCOM-parked\manifest.json') -Raw | ConvertFrom-Json
        $manifest.saves.path | Should -Not -Contain 'fltcom-lko-single'
    }

    It 'off refuses a save that appeared under a parked name and changes nothing' {
        Invoke-TestMode on -KspRoot $root | Out-Null
        New-Fixture $root 'lko-single'
        foreach ($rel in 'GameData\FLTCOM\FLTCOM.dll', 'saves\career\persistent.sfs') {
            $path = Join-Path $root $rel
            New-Item -ItemType Directory -Force (Split-Path -Parent $path) | Out-Null
            Set-Content -LiteralPath $path -Value 'made during test mode'
        }
        $during = Get-Snapshot $root -WithParked

        $result = Invoke-TestMode off -KspRoot $root
        $result.Code | Should -Be 1
        $result.Text | Should -Match 'conflict: saves/career appeared during test mode'
        $result.Text | Should -Match 'test mode still on, nothing restored'
        Get-Snapshot $root -WithParked | Should -Be $during
    }

    It 'off after a worker died mid-move restores every save' {
        Invoke-TestMode on -KspRoot $root | Out-Null
        Move-Item (Join-Path $root 'FLTCOM-parked\saves\sandbox') (Join-Path $root 'saves\sandbox')
        Move-Item (Join-Path $root 'FLTCOM-parked\GameData\ModA') (Join-Path $root 'GameData\ModA')
        $result = Invoke-TestMode off -KspRoot $root
        $result.Code | Should -Be 0
        $result.Text | Should -Match 'test mode off, 4 items restored, 2 saves restored'
        Get-Snapshot $root | Should -Be $before
    }

    It 'on leaves loose files and the stock scenarios and training folders in saves, and off survives Steam and KSP rewriting them' {
        New-File (Join-Path $root 'saves\steam_autocloud.vdf') '"accountid" "1"'
        New-File (Join-Path $root 'saves\scenarios\a.sfs') 'stock scenario'
        New-File (Join-Path $root 'saves\training\b.sfs') 'stock training'
        $before = Get-Snapshot $root

        (Invoke-TestMode on -KspRoot $root).Code | Should -Be 0
        (Get-ChildItem (Join-Path $root 'saves')).Name | Sort-Object | Should -Be @('scenarios', 'steam_autocloud.vdf', 'training')
        (Get-ChildItem (Join-Path $root 'FLTCOM-parked\saves')).Name | Sort-Object | Should -Be @('career', 'sandbox')

        New-File (Join-Path $root 'saves\steam_autocloud.vdf') '"accountid" "1"'
        New-File (Join-Path $root 'saves\scenarios\new.sfs') 'recreated by KSP'
        $result = Invoke-TestMode off -KspRoot $root
        $result.Code | Should -Be 0
        $result.Text | Should -Match 'test mode off, 4 items restored, 2 saves restored'
        $result.Text | Should -Not -Match 'note: saves/'
        Get-Snapshot $root | Where-Object { $_ -notlike '\saves\scenarios\new.sfs *' } | Should -Be $before
    }

    It 'a save and a mod with non-ASCII names round-trip' {
        $save = "Caf$([char]0xE9) $([char]0x3A9)mega $([char]0x41A)$([char]0x435)$([char]0x440)"
        $mod = "M$([char]0xF6)d$([char]0x65E5)$([char]0x672C)"
        New-File (Join-Path $root "saves\$save\persistent.sfs") 'unicode save'
        New-File (Join-Path $root "GameData\$mod\m.cfg") 'unicode mod'
        $before = Get-Snapshot $root

        (Invoke-TestMode on -KspRoot $root).Code | Should -Be 0
        Test-Path -LiteralPath (Join-Path $root "FLTCOM-parked\saves\$save") | Should -BeTrue
        $result = Invoke-TestMode off -KspRoot $root
        $result.Code | Should -Be 0
        $result.Text | Should -Match 'test mode off, 5 items restored, 3 saves restored'
        Get-Snapshot $root | Should -Be $before
    }

    It 'on refuses while a saves/fltcom-* folder has no fixture marker, and names it' {
        New-File (Join-Path $root 'saves\fltcom-mine\persistent.sfs') 'operator save'
        $before = Get-Snapshot $root
        $result = Invoke-TestMode on -KspRoot $root
        $result.Code | Should -Be 1
        $result.Text | Should -Match 'saves/fltcom-mine'
        Get-Snapshot $root | Should -Be $before
        Test-Path (Join-Path $root 'FLTCOM-parked\manifest.json') | Should -BeFalse
    }

    It 'off deletes only fixture folders that carry the marker' {
        New-File (Join-Path $root 'saves\fltcom-x\persistent.sfs') 'operator save'
        New-Fixture $root 'lko-single'
        $result = Invoke-TestMode off -KspRoot $root
        $result.Code | Should -Be 0
        $result.Text | Should -Match 'removed saves/fltcom-lko-single'
        Get-Content (Join-Path $root 'saves\fltcom-x\persistent.sfs') | Should -Be 'operator save'
        Test-Path (Join-Path $root 'saves\fltcom-lko-single') | Should -BeFalse
    }

    It 'on after an on that died past the manifest write finishes the moves' {
        Invoke-TestMode on -KspRoot $root | Out-Null
        Move-Item (Join-Path $root 'FLTCOM-parked\saves\career') (Join-Path $root 'saves\career')
        Move-Item (Join-Path $root 'FLTCOM-parked\GameData\ModA') (Join-Path $root 'GameData\ModA')

        $result = Invoke-TestMode on -KspRoot $root
        $result.Code | Should -Be 0
        $result.Text | Should -Match 'already on, 4 items parked, 2 saves parked'
        @(Get-ChildItem (Join-Path $root 'saves')).Count | Should -Be 0
        (Get-ChildItem (Join-Path $root 'GameData')).Name | Sort-Object | Should -Be @('Squad', 'SquadExpansion')
        (Invoke-TestMode status -KspRoot $root).Text | Should -Be 'on, 4 items parked, 2 saves parked'
    }

    It 'on again leaves an added variant and a deployed FLTCOM live' {
        $installed = Join-Path $root 'GameData\FLTCOM\FLTCOM.dll'
        New-File $installed 'release build installed by CKAN'
        Invoke-TestMode on -KspRoot $root | Out-Null
        New-File $installed 'dev build'
        Invoke-TestMode variant add ModA -KspRoot $root | Out-Null

        (Invoke-TestMode on -KspRoot $root).Code | Should -Be 0
        Get-Content $installed | Should -Be 'dev build'
        Test-Path (Join-Path $root 'GameData\ModA\ModA.cfg') | Should -BeTrue
    }

    It 'on backs up saves with a hash listing and backs up settings.cfg' {
        Invoke-TestMode on -KspRoot $root | Out-Null
        Get-Content (Join-Path $root 'FLTCOM-parked\saves-backup\career\persistent.sfs') | Should -Be 'career save'
        (Get-Content (Join-Path $root 'FLTCOM-parked\saves-backup.sha256')).Count | Should -Be 2
        Get-Content (Join-Path $root 'FLTCOM-parked\settings.cfg') | Should -Be 'SCREEN_RESOLUTION_WIDTH = 1920'
    }

    It 'on twice is a no-op' {
        Invoke-TestMode on -KspRoot $root | Out-Null
        New-Item -ItemType Directory (Join-Path $root 'GameData\FLTCOM') | Out-Null
        New-Fixture $root 'lko-single'
        $second = Invoke-TestMode on -KspRoot $root
        $second.Code | Should -Be 0
        $second.Text | Should -Match 'already on, 4 items parked, 2 saves parked'
        Test-Path (Join-Path $root 'GameData\FLTCOM') | Should -BeTrue
        Test-Path (Join-Path $root 'saves\fltcom-lko-single') | Should -BeTrue
        (Get-ChildItem (Join-Path $root 'FLTCOM-parked\GameData')).Count | Should -Be 4
        (Get-ChildItem (Join-Path $root 'FLTCOM-parked\saves')).Count | Should -Be 2
    }

    It 'off restores the install exactly and removes what test mode deployed' {
        Invoke-TestMode on -KspRoot $root | Out-Null
        New-Fixture $root 'lko-single'
        foreach ($rel in 'GameData\FLTCOM\FLTCOM.dll', 'GameData\FLTCOM.TestHarness\FLTCOM.TestHarness.dll') {
            $path = Join-Path $root $rel
            New-Item -ItemType Directory -Force (Split-Path -Parent $path) | Out-Null
            Set-Content -LiteralPath $path -Value 'deployed'
        }
        Set-Content -LiteralPath (Join-Path $root 'settings.cfg') -Value 'rewritten by KSP on exit'

        $result = Invoke-TestMode off -KspRoot $root
        $result.Code | Should -Be 0
        $result.Text | Should -Match 'test mode off, 4 items restored, 2 saves restored'
        $result.Text | Should -Match 'restored settings.cfg'
        Get-Snapshot $root | Should -Be $before
        Test-Path (Join-Path $root 'FLTCOM-parked\manifest.json') | Should -BeFalse
        Test-Path (Join-Path $root 'FLTCOM-parked\saves-backup\career\persistent.sfs') | Should -BeTrue
    }

    It 'off twice is a no-op' {
        Invoke-TestMode on -KspRoot $root | Out-Null
        Invoke-TestMode off -KspRoot $root | Out-Null
        $second = Invoke-TestMode off -KspRoot $root
        $second.Code | Should -Be 0
        $second.Text | Should -Match 'test mode off, 0 items restored'
        Get-Snapshot $root | Should -Be $before
        Test-Path (Join-Path $root 'FLTCOM-parked\saves') | Should -BeFalse
    }

    It 'off without test mode never removes an installed FLTCOM' {
        $installed = Join-Path $root 'GameData\FLTCOM\FLTCOM.dll'
        New-Item -ItemType Directory -Force (Split-Path -Parent $installed) | Out-Null
        Set-Content -LiteralPath $installed -Value 'release build installed by CKAN'
        $result = Invoke-TestMode off -KspRoot $root
        $result.Code | Should -Be 0
        Test-Path $installed | Should -BeTrue
    }

    It 'on parks an installed FLTCOM and off brings it back' {
        $installed = Join-Path $root 'GameData\FLTCOM\FLTCOM.dll'
        New-Item -ItemType Directory -Force (Split-Path -Parent $installed) | Out-Null
        Set-Content -LiteralPath $installed -Value 'release build installed by CKAN'
        $snapshot = Get-Snapshot $root
        Invoke-TestMode on -KspRoot $root | Out-Null
        Test-Path (Join-Path $root 'FLTCOM-parked\GameData\FLTCOM\FLTCOM.dll') | Should -BeTrue
        New-Item -ItemType Directory -Force (Split-Path -Parent $installed) | Out-Null
        Set-Content -LiteralPath $installed -Value 'dev build'
        Invoke-TestMode off -KspRoot $root | Out-Null
        Get-Snapshot $root | Should -Be $snapshot
    }

    It 'off reports a manifest item that is missing and keeps test mode on' {
        Invoke-TestMode on -KspRoot $root | Out-Null
        Move-Item (Join-Path $root 'FLTCOM-parked\GameData\Mod B') (Join-Path $TestDrive 'stolen-mod-b')

        $result = Invoke-TestMode off -KspRoot $root
        $result.Code | Should -Be 1
        $result.Text | Should -Match 'missing: Mod B'
        $result.Text | Should -Match '3 of 4 items restored, 0 of 2 saves restored'
        $result.Text | Should -Match 'saves stay parked'
        Test-Path (Join-Path $root 'GameData\ModA\ModA.cfg') | Should -BeTrue
        Test-Path (Join-Path $root 'FLTCOM-parked\saves\career\persistent.sfs') | Should -BeTrue
        Test-Path (Join-Path $root 'FLTCOM-parked\manifest.json') | Should -BeTrue
        (Invoke-TestMode status -KspRoot $root).Text | Should -Match '^on'
    }

    It 'off reports a size mismatch' {
        Invoke-TestMode on -KspRoot $root | Out-Null
        Add-Content -LiteralPath (Join-Path $root 'FLTCOM-parked\GameData\ModA\ModA.cfg') -Value 'grown'
        $result = Invoke-TestMode off -KspRoot $root
        $result.Code | Should -Be 1
        $result.Text | Should -Match 'size mismatch: ModA'
    }

    It 'off reports a parked save that changed during test mode' {
        Invoke-TestMode on -KspRoot $root | Out-Null
        [IO.File]::WriteAllText((Join-Path $root 'FLTCOM-parked\saves\career\persistent.sfs'), 'overwritten')
        $result = Invoke-TestMode off -KspRoot $root
        $result.Code | Should -Be 1
        $result.Text | Should -Match 'saves changed during test mode.*career'
    }

    It 'status prints on or off and the parked count' {
        (Invoke-TestMode status -KspRoot $root).Text | Should -Be 'off, 0 items parked'
        Invoke-TestMode on -KspRoot $root | Out-Null
        (Invoke-TestMode status -KspRoot $root).Text | Should -Be 'on, 4 items parked, 2 saves parked'
        Invoke-TestMode off -KspRoot $root | Out-Null
        (Invoke-TestMode status -KspRoot $root).Text | Should -Be 'off, 0 items parked'
    }

    It 'variant add moves a parked mod back and variant remove parks it again' {
        Invoke-TestMode on -KspRoot $root | Out-Null
        (Invoke-TestMode variant add ModA -KspRoot $root).Text | Should -Be 'variant ModA added'
        Test-Path (Join-Path $root 'GameData\ModA\ModA.cfg') | Should -BeTrue
        (Invoke-TestMode status -KspRoot $root).Text | Should -Match 'variant ModA'
        (Invoke-TestMode variant remove ModA -KspRoot $root).Text | Should -Be 'variant ModA removed'
        Test-Path (Join-Path $root 'FLTCOM-parked\GameData\ModA\ModA.cfg') | Should -BeTrue
        Invoke-TestMode variant add ModA -KspRoot $root | Out-Null
        Invoke-TestMode off -KspRoot $root | Out-Null
        Get-Snapshot $root | Should -Be $before
    }

    It 'variant add copies a mod from a source folder and off removes the copy' {
        $source = Join-Path $TestDrive 'FarSource'
        New-Item -ItemType Directory -Force $source | Out-Null
        Set-Content (Join-Path $source 'far.cfg') 'far'
        Invoke-TestMode on -KspRoot $root | Out-Null
        (Invoke-TestMode variant add FerramAerospaceResearch -Source $source -KspRoot $root).Text | Should -Be 'variant FerramAerospaceResearch added'
        Test-Path (Join-Path $root 'GameData\FerramAerospaceResearch\far.cfg') | Should -BeTrue
        $result = Invoke-TestMode off -KspRoot $root
        $result.Code | Should -Be 0
        Get-Snapshot $root | Should -Be $before
        Test-Path (Join-Path $source 'far.cfg') | Should -BeTrue
    }

    It 'fails with a clear message when the folder is not a KSP install' {
        $result = Invoke-TestMode status -KspRoot (Join-Path $TestDrive 'nowhere')
        $result.Code | Should -Be 1
        $result.Text | Should -Match 'no KSP install'
    }

    Context 'while a process named KSP_x64 runs' {
        BeforeEach {
            $fakeExe = Join-Path $TestDrive 'KSP_x64.exe'
            Copy-Item (Join-Path $env:windir 'System32\PING.EXE') $fakeExe -Force
            $fake = Start-Process -FilePath $fakeExe -ArgumentList '-n', '120', '127.0.0.1' -WindowStyle Hidden -PassThru
        }
        AfterEach {
            if (-not $fake.HasExited) {
                $fake.Kill()
                $fake.WaitForExit()
            }
        }

        It 'on refuses and moves nothing' {
            $result = Invoke-TestMode on -KspRoot $root
            $result.Code | Should -Be 1
            $result.Text | Should -Match 'refusing to run while KSP_x64'
            Get-Snapshot $root | Should -Be $before
            Test-Path (Join-Path $root 'FLTCOM-parked\manifest.json') | Should -BeFalse
        }

        It 'off refuses and leaves the mods parked' {
            $fake.Kill()
            $fake.WaitForExit()
            Invoke-TestMode on -KspRoot $root | Out-Null
            $script:again = Start-Process -FilePath $fakeExe -ArgumentList '-n', '120', '127.0.0.1' -WindowStyle Hidden -PassThru
            try {
                $result = Invoke-TestMode off -KspRoot $root
                $result.Code | Should -Be 1
                $result.Text | Should -Match 'refusing to run while KSP_x64'
                (Get-ChildItem (Join-Path $root 'FLTCOM-parked\GameData')).Count | Should -Be 4
            } finally {
                $script:again.Kill()
                $script:again.WaitForExit()
            }
        }
    }
}
