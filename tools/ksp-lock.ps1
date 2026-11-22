[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('acquire', 'renew', 'release', 'status')]
    [string]$Command,
    [Parameter(Position = 1)]
    [string]$Owner,
    [int]$LeaseSeconds = 1200,
    [string]$LockFile
)

$ErrorActionPreference = 'Stop'

if (-not $LockFile) {
    $LockFile = $env:FLTCOM_KSP_LOCK
}
if (-not $LockFile) {
    $LockFile = Join-Path $env:LOCALAPPDATA 'FLTCOM\ksp-lock.json'
}
if ($Command -ne 'status' -and -not $Owner) {
    Write-Output "usage: ksp-lock.ps1 $Command <owner>"
    exit 2
}

$folder = Split-Path -Parent $LockFile
if (-not (Test-Path -LiteralPath $folder)) {
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
}

function Enter-Guard {
    $deadline = (Get-Date).AddSeconds(10)
    while ($true) {
        try {
            return [IO.File]::Open("$LockFile.guard", 'OpenOrCreate', 'ReadWrite', 'None')
        } catch [IO.IOException] {
            if ((Get-Date) -gt $deadline) {
                throw "could not open $LockFile.guard within 10 seconds"
            }
            Start-Sleep -Milliseconds 50
        }
    }
}

function Read-Lease {
    if (-not (Test-Path -LiteralPath $LockFile)) {
        return $null
    }
    $lease = Get-Content -LiteralPath $LockFile -Raw | ConvertFrom-Json
    if ([DateTime]::Parse($lease.expiresUtc, $null, [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime() -le (Get-Date).ToUniversalTime()) {
        return $null
    }
    return $lease
}

function Write-Lease {
    $expires = (Get-Date).ToUniversalTime().AddSeconds($LeaseSeconds).ToString('o')
    $json = ConvertTo-Json -InputObject ([ordered]@{ owner = $Owner; expiresUtc = $expires })
    [IO.File]::WriteAllText($LockFile, $json)
    return $expires
}

$code = 0
$guard = Enter-Guard
try {
    $lease = Read-Lease
    $heldByOther = $lease -and $lease.owner -ne $Owner
    switch ($Command) {
        'acquire' {
            if ($heldByOther) {
                Write-Output "busy holder=$($lease.owner) expires=$($lease.expiresUtc)"
                $code = 1; break
            }
            Write-Output "granted expires=$(Write-Lease)"
        }
        'renew' {
            if ($heldByOther) {
                Write-Output "busy holder=$($lease.owner) expires=$($lease.expiresUtc)"
                $code = 1; break
            }
            Write-Output "renewed expires=$(Write-Lease)"
        }
        'release' {
            if ($heldByOther) {
                Write-Output "busy holder=$($lease.owner); $Owner holds nothing to release"
                $code = 1; break
            }
            if (Test-Path -LiteralPath $LockFile) {
                Remove-Item -LiteralPath $LockFile -Force
            }
            Write-Output 'released'
        }
        'status' {
            if ($lease) {
                Write-Output "held holder=$($lease.owner) expires=$($lease.expiresUtc)"
            } else {
                Write-Output 'free'
            }
        }
    }
} finally {
    $guard.Dispose()
}
exit $code
