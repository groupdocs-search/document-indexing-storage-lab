@echo off
setlocal
rem Build everything in Release. Run this before the bat files that use --no-build.
pushd "%~dp0"
dotnet build IndexingStorageLab.sln -c Release
popd
endlocal
