$SrcRootPath = Join-Path $PSScriptRoot ".." "src" -Resolve
$EditedFiles = 0
foreach ($csproj in Get-ChildItem $SrcRootPath -Recurse -Filter "*.csproj") {
    Write-Host "Processing $($csproj.FullName)"

    [xml]$csprojContent = Get-Content -LiteralPath $csproj.FullName
    if ($csprojContent.Project.PropertyGroup.PackAsTool -ieq "true") {
        $toolReadme = Join-Path $csproj.DirectoryName "readme.md"
        if (Test-Path -LiteralPath $toolReadme) {
            $helpText = dotnet run --project $csproj --framework net7.0 -- --help
            $helpText = $($helpText -join "`n").TrimEnd(@(" ", "`t", "`r", "`n"))

            [string]$toolReadmeContent = Get-Content -LiteralPath $toolReadme -Raw -Encoding utf8

            $Pattern = '(?s)(?<=<!-- help -->)(.*)(?=<!-- help -->)'
            $newToolReadmeContent = $toolReadmeContent -replace $Pattern, "`n```````n$helpText`n```````n"
            $newToolReadmeContent = $newToolReadmeContent.TrimEnd(@(" ", "`t", "`r", "`n"))

            if ($toolReadmeContent -ne $newToolReadmeContent) {
                # This script is used in CI. exit 1 makes the CI fails
                Set-Content -LiteralPath $toolReadme -Value $newToolReadmeContent -Encoding utf8 -NoNewline
                Write-Warning "README was not up-to-date"
                $EditedFiles += 1
            }
        }
        else {
            Write-Error "Tool $csproj does not have a readme.md file"
            exit 1
        }
    }
}

if ($EditedFiles -gt 0) {
    Write-Error "Some README files were not up-to-date"
    exit 1
}