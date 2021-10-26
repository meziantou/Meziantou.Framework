$ProjectPath = Join-Path $PSScriptRoot "ParallelizeTests" "ParallelizeTests.csproj" -Resolve
$ProjectRootPath = Join-Path $PSScriptRoot ".." -Resolve

dotnet run --project $ProjectPath -- "$ProjectRootPath"
exit $LASTEXITCODE