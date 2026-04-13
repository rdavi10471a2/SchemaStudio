param(
    [string]$CommitMessage = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    (Get-Location).Path
} else {
    $PSScriptRoot
}

Push-Location $repoRoot
try {
    $gitRoot = (& git rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($gitRoot)) {
        Write-Host "Skipping AIMonitor checkpoint: repository not initialized."
        exit 0
    }

    $statusLines = @(& git status --short)
    if (-not $statusLines -or $statusLines.Count -eq 0) {
        Write-Host "Skipping AIMonitor checkpoint: no changes to commit."
        exit 0
    }

    & git add -A

    if ([string]::IsNullOrWhiteSpace($CommitMessage)) {
        $CommitMessage = "Checkpoint successful build " + (Get-Date -Format "yyyy-MM-dd HH:mm")
    }

    & git commit -m $CommitMessage
    if ($LASTEXITCODE -ne 0) {
        throw "git commit failed with exit code $LASTEXITCODE"
    }

    Write-Host "AIMonitor build checkpoint created."
}
finally {
    Pop-Location
}
