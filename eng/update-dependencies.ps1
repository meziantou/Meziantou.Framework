$RootPath = Join-Path $PSScriptRoot ".." -Resolve

Push-Location $RootPath
& dotnet tool restore
& dotnet tool run depsupdater update --directory $RootPath
& dotnet restore "$RootPath/eng/build.proj"
Pop-Location
