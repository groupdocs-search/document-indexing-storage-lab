@echo off
rem Stop any running lab host. See stop.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0stop.ps1" %*
