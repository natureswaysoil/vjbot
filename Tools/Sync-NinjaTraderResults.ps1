param(
    [string]$RepoPath = "$env:USERPROFILE\Documents\vjbot",
    [string]$ExportPath = "$env:USERPROFILE\Documents\NinjaTrader 8\export",
    [string]$ResultsFolder = "Results",
    [int]$PollSeconds = 10
)

$ErrorActionPreference = "Stop"

function Ensure-GitRepo {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        throw "Repository path not found: $Path"
    }
    if (-not (Test-Path (Join-Path $Path ".git"))) {
        throw "Not a Git repository: $Path"
    }
}

function Sync-Results {
    Ensure-GitRepo -Path $RepoPath

    if (-not (Test-Path $ExportPath)) {
        New-Item -ItemType Directory -Path $ExportPath -Force | Out-Null
    }

    $destRoot = Join-Path $RepoPath $ResultsFolder
    if (-not (Test-Path $destRoot)) {
        New-Item -ItemType Directory -Path $destRoot -Force | Out-Null
    }

    $files = Get-ChildItem -Path $ExportPath -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in '.csv', '.xlsx', '.txt', '.xml' }

    if (-not $files) {
        return
    }

    foreach ($file in $files) {
        $stamp = Get-Date -Format 'yyyy-MM-dd_HHmmss'
        $safeBase = ($file.BaseName -replace '[^A-Za-z0-9._-]', '_')
        $destName = "${stamp}_${safeBase}$($file.Extension)"
        $dest = Join-Path $destRoot $destName
        Copy-Item $file.FullName $dest -Force
    }

    Push-Location $RepoPath
    try {
        git add $ResultsFolder | Out-Null
        $status = git status --porcelain $ResultsFolder
        if ($status) {
            $msg = "Add NinjaTrader results $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
            git commit -m $msg | Out-Null
            git push origin main | Out-Null
            Write-Host "Synced NinjaTrader results to GitHub at $(Get-Date)."
        }
    }
    finally {
        Pop-Location
    }
}

Write-Host "Watching NinjaTrader export folder: $ExportPath"
Write-Host "Repository: $RepoPath"
Write-Host "Press Ctrl+C to stop."

$seen = @{}
while ($true) {
    if (Test-Path $ExportPath) {
        Get-ChildItem -Path $ExportPath -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -in '.csv', '.xlsx', '.txt', '.xml' } |
            ForEach-Object {
                $key = $_.FullName
                $sig = "$($_.Length)|$($_.LastWriteTimeUtc.Ticks)"
                if (-not $seen.ContainsKey($key) -or $seen[$key] -ne $sig) {
                    $seen[$key] = $sig
                    Start-Sleep -Milliseconds 750
                    Sync-Results
                }
            }
    }
    Start-Sleep -Seconds $PollSeconds
}
