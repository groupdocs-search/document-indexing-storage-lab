<#
.SYNOPSIS
    Stop any running lab host, build, and launch the UI.

.DESCRIPTION
    A running host holds the build output, so a plain `dotnet run` fails with MSB3027 after a long
    silent MSBuild retry. This stops the old host first, which is safe: a finished run is already
    written to its session file, and an in-progress one is abandoned at its last flush.

    The browser is opened from a background job once the port answers -- `dotnet run` does not
    honour "launchBrowser" in launchSettings.json, that key is read only by Visual Studio and the
    VS Code C# extension.

.PARAMETER Port
    Port to listen on. Default 5090 (not 5080).

.PARAMETER NoBuild
    Skip the build and run the existing output.

.PARAMETER NoBrowser
    Do not open a browser.

.PARAMETER KeepExisting
    Fail instead of stopping an already-running host.

.EXAMPLE
    .\run.ps1
.EXAMPLE
    .\run.ps1 -Port 5091 -NoBrowser
#>
[CmdletBinding()]
param(
    [int]    $Port = 5090,
    [switch] $NoBuild,
    [switch] $NoBrowser,
    [switch] $KeepExisting
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Push-Location $root

try {
    # --- 1. clear any host of ours -------------------------------------------------------------
    $running = @(Get-Process -Name IndexingLab.Web -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        if ($KeepExisting) {
            Write-Host "A lab host is already running and -KeepExisting was given:" -ForegroundColor Yellow
            $running | ForEach-Object { Write-Host ("    PID {0}  started {1}" -f $_.Id, $_.StartTime) }
            exit 1
        }

        foreach ($p in $running) {
            Write-Host ("Stopping previous lab host PID {0} (started {1})" -f $p.Id, $p.StartTime) -ForegroundColor Yellow
            Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        }

        # Wait for the file handles to actually drop, or the build races the dying process.
        for ($i = 0; $i -lt 20 -and (Get-Process -Name IndexingLab.Web -ErrorAction SilentlyContinue); $i++) {
            Start-Sleep -Milliseconds 250
        }
        Write-Host "Stopped. Any finished run is already saved to its session file." -ForegroundColor Yellow
        Write-Host ""
    }

    # --- 2. the port must be free, and if it is not it is now someone else's ---------------------
    $holder = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    if ($holder) {
        Write-Host "ERROR: port $Port is in use by another application:" -ForegroundColor Red
        $holder | ForEach-Object {
            $p = Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue
            Write-Host ("    PID {0}  {1}" -f $_.OwningProcess, $p.ProcessName)
        }
        Write-Host ""
        Write-Host "Stop it, or choose another port:  .\run.ps1 -Port $($Port + 1)"
        exit 1
    }

    # --- 3. licence ------------------------------------------------------------------------------
    if (-not $env:GROUPDOCS_SEARCH_LIC_PATH) {
        Write-Host "WARNING: GROUPDOCS_SEARCH_LIC_PATH is not set (full path to a .lic file)." -ForegroundColor Yellow
        Write-Host "         Runs will be document-capped by trial limits and their numbers meaningless." -ForegroundColor Yellow
        Write-Host ""
    }

    # --- 4. build --------------------------------------------------------------------------------
    if (-not $NoBuild) {
        Write-Host "Building..." -ForegroundColor Cyan
        & dotnet build (Join-Path $root 'IndexingStorageLab.sln') -c Release --nologo
        if ($LASTEXITCODE -ne 0) {
            Write-Host ""
            Write-Host "Build failed. Nothing was started." -ForegroundColor Red
            exit $LASTEXITCODE
        }
        Write-Host ""
    }

    # --- 5. browser, once the port answers -------------------------------------------------------
    if (-not $NoBrowser) {
        Start-Job -ScriptBlock {
            param($p)
            for ($i = 0; $i -lt 300; $i++) {
                try {
                    $c = New-Object Net.Sockets.TcpClient
                    $c.Connect('127.0.0.1', $p)
                    $c.Close()
                    Start-Process "http://127.0.0.1:$p/"
                    break
                } catch { Start-Sleep -Seconds 1 }
            }
        } -ArgumentList $Port | Out-Null
    }

    Write-Host "Starting the lab at http://127.0.0.1:$Port/  (Ctrl+C to stop)" -ForegroundColor Green
    Write-Host ""

    & dotnet run --project (Join-Path $root 'src\IndexingLab.Web') -c Release --no-build --urls "http://127.0.0.1:$Port"
}
finally {
    Get-Job -ErrorAction SilentlyContinue | Where-Object State -eq 'Completed' | Remove-Job -Force -ErrorAction SilentlyContinue
    Pop-Location
}
