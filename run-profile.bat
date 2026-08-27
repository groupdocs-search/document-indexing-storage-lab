@echo off
setlocal
rem Headless profiling run. Use this for the long ones -- it keeps no web host alive.
rem
rem   run-profile.bat                    lists the available profiles
rem   run-profile.bat smoke              2 GB sanity run
rem   run-profile.bat corpora-30pct      the real 90 GB run
rem
rem Sessions land in the folder configured in src\IndexingLab.Cli\appsettings.json, which is the
rem same folder the UI reads, so headless runs appear in the UI list.
pushd "%~dp0"

if "%~1"=="" (
  echo Available profiles:
  echo.
  powershell -NoProfile -Command "(Get-Content 'src\IndexingLab.Cli\appsettings.json' -Raw | ConvertFrom-Json).Profiles.PSObject.Properties | ForEach-Object { '  {0,-24} {1}' -f $_.Name, $_.Value.Label }"
  echo.
  echo Usage: run-profile.bat ^<profile^>
  popd
  endlocal
  exit /b 0
)

if "%GROUPDOCS_SEARCH_LIC_PATH%"=="" (
  echo WARNING: GROUPDOCS_SEARCH_LIC_PATH is not set ^(full path to a .lic file^). Trial limits
  echo          will cap the run and every figure it reports will be unrepresentative.
  echo.
)

dotnet run --project src\IndexingLab.Cli -c Release -- %*

popd
endlocal
