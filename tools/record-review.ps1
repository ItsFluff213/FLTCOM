[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$WindowTitle,
    [Parameter(Mandatory = $true, Position = 1)][ValidateRange(1, 600)][int]$Seconds,
    [Parameter(Mandatory = $true, Position = 2)][string]$OutFile
)

$ErrorActionPreference = 'Stop'

function Find-Ffmpeg {
    $command = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }
    $packages = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    if (Test-Path $packages) {
        $found = Get-ChildItem -Path $packages -Filter ffmpeg.exe -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($found) {
            return $found.FullName
        }
    }
    throw 'ffmpeg not found. Install it with: winget install --id Gyan.FFmpeg -e --source winget'
}

$ffmpeg = Find-Ffmpeg
$outDir = Split-Path -Parent $OutFile
if ($outDir -and -not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

$ffmpegArgs = @(
    '-y', '-hide_banner', '-loglevel', 'error',
    '-f', 'gdigrab', '-framerate', '30', '-i', "title=$WindowTitle",
    '-t', "$Seconds",
    '-vf', 'scale=trunc(iw/2)*2:trunc(ih/2)*2',
    '-c:v', 'libx264', '-preset', 'veryfast', '-pix_fmt', 'yuv420p',
    $OutFile
)
& $ffmpeg @ffmpegArgs
if ($LASTEXITCODE -ne 0) {
    throw "ffmpeg failed with exit code $LASTEXITCODE. Check that a window titled '$WindowTitle' is open and not minimized."
}
Write-Host "recorded $OutFile"
