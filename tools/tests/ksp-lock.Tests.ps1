BeforeAll {
    $script:Lock = Join-Path $PSScriptRoot '..\ksp-lock.ps1'

    function Invoke-Lock {
        $output = & $script:Lock @args -LockFile $script:LockFile 2>&1 | ForEach-Object { "$_" }
        return [pscustomobject]@{ Code = $LASTEXITCODE; Text = ($output -join "`n") }
    }
}

Describe 'ksp-lock.ps1' {
    BeforeEach {
        $script:LockFile = Join-Path $TestDrive ("lock-" + [Guid]::NewGuid().ToString('N') + '.json')
    }

    It 'grants a free lease' {
        $result = Invoke-Lock acquire worker-1
        $result.Code | Should -Be 0
        $result.Text | Should -Match '^granted expires='
        (Invoke-Lock status).Text | Should -Match '^held holder=worker-1'
    }

    It 'answers busy at once while another owner holds the lease' {
        Invoke-Lock acquire worker-1 | Out-Null
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $result = Invoke-Lock acquire worker-2
        $watch.Elapsed.TotalSeconds | Should -BeLessThan 5
        $result.Code | Should -Be 1
        $result.Text | Should -Match '^busy holder=worker-1'
    }

    It 'lets the holder acquire again' {
        Invoke-Lock acquire worker-1 | Out-Null
        (Invoke-Lock acquire worker-1).Code | Should -Be 0
    }

    It 'renew extends the lease for the holder only' {
        Invoke-Lock acquire worker-1 -LeaseSeconds 60 | Out-Null
        $first = (Get-Content $script:LockFile -Raw | ConvertFrom-Json).expiresUtc
        $renewed = Invoke-Lock renew worker-1 -LeaseSeconds 1200
        $renewed.Code | Should -Be 0
        $renewed.Text | Should -Match '^renewed expires='
        $second = (Get-Content $script:LockFile -Raw | ConvertFrom-Json).expiresUtc
        [DateTime]::Parse($second) | Should -BeGreaterThan ([DateTime]::Parse($first))

        $other = Invoke-Lock renew worker-2
        $other.Code | Should -Be 1
        $other.Text | Should -Match '^busy holder=worker-1'
    }

    It 'treats an expired lease as free' {
        Invoke-Lock acquire worker-1 -LeaseSeconds 1 | Out-Null
        (Invoke-Lock acquire worker-2).Code | Should -Be 1
        Start-Sleep -Milliseconds 1500
        (Invoke-Lock status).Text | Should -Be 'free'
        $result = Invoke-Lock acquire worker-2
        $result.Code | Should -Be 0
        $result.Text | Should -Match '^granted'
    }

    It 'release frees the lease for its holder only' {
        Invoke-Lock acquire worker-1 | Out-Null
        (Invoke-Lock release worker-2).Code | Should -Be 1
        (Invoke-Lock release worker-1).Text | Should -Be 'released'
        (Invoke-Lock status).Text | Should -Be 'free'
    }
}
