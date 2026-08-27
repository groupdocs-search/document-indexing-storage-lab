@echo off
rem Thin shim so double-clicking still works. The real implementation is run.ps1 -- one script,
rem not two that drift apart.
rem
rem   run.bat            launch on 5090, stopping any previous host first
rem   run.bat 5091       ... on another port
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" %*
