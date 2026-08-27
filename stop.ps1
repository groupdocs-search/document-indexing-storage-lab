<#
.SYNOPSIS
    Stop any running lab host.

.DESCRIPTION
    A host holds the build output while it runs, so builds fail until it stops. This never loses
    results: a finished run is already written to its session file, and an in-progress run is
    abandoned at its last flush, which is at most a few batches.
#>
[CmdletBinding()]
param()

$running = @(Get-Process -Name IndexingLab.Web -ErrorAction SilentlyContinue)

if ($running.Count -eq 0) {
    Write-Host "No lab host is running."
    exit 0
}

foreach ($p in $running) {
    Write-Host ("Stopping PID {0} (started {1})" -f $p.Id, $p.StartTime)
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
}

for ($i = 0; $i -lt 20 -and (Get-Process -Name IndexingLab.Web -ErrorAction SilentlyContinue); $i++) {
    Start-Sleep -Milliseconds 250
}

if (Get-Process -Name IndexingLab.Web -ErrorAction SilentlyContinue) {
    Write-Host "Still running - stop it manually." -ForegroundColor Red
    exit 1
}

Write-Host "Stopped." -ForegroundColor Green
