@echo off
setlocal
rem Build and launch the lab UI on http://127.0.0.1:5090/
rem
rem Port 5090, deliberately NOT 5080: AsposeNetDocumentProcessor uses 5080 on this machine and the
rem two were colliding. Override with:  run.bat 5091
rem
rem The port is checked BEFORE starting. The browser helper below waits for *something* to accept a
rem connection and cannot tell whose application answered -- if another app already owns the port,
rem it opens that one instead, while our Kestrel fails to bind and the error scrolls past. So we
rem refuse to start rather than open the wrong application.
pushd "%~dp0"

set "PORT=%~1"
if "%PORT%"=="" set "PORT=5090"

powershell -NoProfile -Command "if (Get-NetTCPConnection -LocalPort %PORT% -State Listen -ErrorAction SilentlyContinue) { exit 1 } else { exit 0 }"
if errorlevel 1 (
  echo.
  echo ERROR: port %PORT% is already in use. Whatever is listening there is NOT this lab:
  echo.
  powershell -NoProfile -Command "Get-NetTCPConnection -LocalPort %PORT% -State Listen | ForEach-Object { $p = Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue; '    PID {0}  {1}' -f $_.OwningProcess, $p.ProcessName }"
  echo.
  echo Stop it, or start this lab on another port:  run.bat 5091
  echo.
  popd
  endlocal
  exit /b 1
)

echo Building and starting. A cold build takes about a minute.
echo The lab will open at http://127.0.0.1:%PORT%/
echo.
if "%GROUPDOCS_LIC_PATH%"=="" (
  if "%LIC_PATH%"=="" (
    echo WARNING: neither GROUPDOCS_LIC_PATH nor LIC_PATH is set.
    echo          Runs will be document-capped by trial limits and their numbers meaningless.
    echo.
  )
)

rem `dotnet run` does NOT honour "launchBrowser" in launchSettings.json -- that key is read only by
rem Visual Studio and the VS Code C# extension, so we open the browser ourselves once the port
rem answers. A cold build takes a while; opening earlier just shows a connection error.
start "" /b powershell -NoProfile -WindowStyle Hidden -Command "for($i=0;$i -lt 300;$i++){ try { $c=New-Object Net.Sockets.TcpClient; $c.Connect('127.0.0.1',%PORT%); $c.Close(); Start-Process 'http://127.0.0.1:%PORT%/'; break } catch { Start-Sleep -Seconds 1 } }"

dotnet run --project src\IndexingLab.Web -c Release --urls http://127.0.0.1:%PORT%

popd
endlocal
