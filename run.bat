@echo off
setlocal
rem Build and launch the lab UI on http://127.0.0.1:5080/
rem
rem `dotnet run` does NOT honour "launchBrowser" in launchSettings.json -- that key is read only by
rem Visual Studio and the VS Code C# extension. So we open the browser ourselves, from a detached
rem helper that waits for the port to accept a connection first: a cold build takes a while, and
rem opening earlier just shows a connection error.
pushd "%~dp0"

echo Building and starting. A cold build takes about a minute.
echo The lab will open at http://127.0.0.1:5080/
echo.
if "%GROUPDOCS_LIC_PATH%"=="" (
  if "%LIC_PATH%"=="" (
    echo WARNING: neither GROUPDOCS_LIC_PATH nor LIC_PATH is set.
    echo          Runs will be document-capped by trial limits and their numbers meaningless.
    echo.
  )
)

start "" /b powershell -NoProfile -WindowStyle Hidden -Command "for($i=0;$i -lt 300;$i++){ try { $c=New-Object Net.Sockets.TcpClient; $c.Connect('127.0.0.1',5080); $c.Close(); Start-Process 'http://127.0.0.1:5080/'; break } catch { Start-Sleep -Seconds 1 } }"

dotnet run --project src\IndexingLab.Web -c Release --urls http://127.0.0.1:5080 %*

popd
endlocal
