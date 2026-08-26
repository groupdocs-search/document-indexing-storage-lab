@echo off
setlocal
rem Offline test suite. Needs no corpus, no licence and no network -- it exercises the storage
rem analyzer, the corpus walker and the session store against temporary folders it creates itself.
pushd "%~dp0"
dotnet test tests\IndexingLab.Tests %*
popd
endlocal
