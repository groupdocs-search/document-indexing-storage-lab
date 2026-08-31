@echo off
setlocal
rem Runnable recipes for large-corpus problems.
rem
rem   recipes.bat                                    usage
rem   recipes.bat diagnose  <indexFolder>            what an existing index is made of (read-only)
rem   recipes.bat progress  <corpus> <indexFolder>   Add/Optimize progress + async cancellable merge
rem   recipes.bat sharded   <corpus> <indexFolder>   many small indexes, queried as one
rem   recipes.bat alphabet  <corpus> <indexFolder>   vocabulary reduction, measured before/after
pushd "%~dp0"
dotnet run --project samples\IndexingLab.Recipes -c Release -- %*
popd
endlocal
