$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

function GetVersion($fileContent) {
    $doc = [xml](($fileContent | Out-String) -replace "∩╗┐")
    $version = $doc.Project.PropertyGroup.Version
    return $version
}

function IncrementVersion($csprojPath) {
    $csprojContent = Get-Content -LiteralPath $csprojPath
    $version = GetVersion($csprojContent)
    $versionParts = $version -split "\."
    $versionParts[-1] = [int]$versionParts[-1] + 1
    $newVersion = $versionParts -join "."

    $doc = [xml]::new()
    $doc.PreserveWhitespace = $true
    $doc.Load($csprojPath);
    if ($doc.Project.PropertyGroup.SkipAutoVersionUpdates) {
        return
    }

    $doc.Project.PropertyGroup.Version = $newVersion
    $doc.Save($csprojPath)
}

function GetCsproj($path) {
    $fullPath = Join-Path $RootPath $path
    while ($fullPath) {
        $parent = Split-Path $fullPath -Parent
        if (Test-Path -LiteralPath $parent -PathType Container) {
            # Some paths may not exists anymore in the current version
            $csproj = Get-ChildItem -Path $parent -Filter *.csproj
            if ($csproj) {
                return $csproj.FullName
            }
        }

        $fullPath = $parent
    }

    return $null
}

$RootPath = Join-Path $PSScriptRoot ".." -Resolve

$commits = git log --pretty=format:'%H' -n 300
$ChangesPerCsproj = @{}

foreach ($file in Get-ChildItem -Path $RootPath -Recurse -Filter *.csproj) {
    $ChangesPerCsproj[$file.FullName] = @{
        "commits"        = @()
        "stopProcessing" = $false
    }
}

$i = 0;
foreach ($commit in $commits) {
    $i++
    Write-Host "Processing commit $i/$($commits.Length): $commit"

    # Get the changes in the commit
    $changes = git diff --name-only "$commit" "$commit~1"
    $changes = $changes | Where-Object { $_.StartsWith("src/") }

    # Process csproj first to check if the version changed
    foreach ($change in $changes | Where-Object { $_ -like "*.csproj" }) {
        try {
            # The previous version may not exists if the file was added in the commit
            $previousContent = git show "${commit}~1:$change"
        }
        catch {
            $previousContent = ""
        }

        $previousVersion = GetVersion($previousContent)

        $currentContent = git show "${commit}:$change"
        $currentVersion = GetVersion($currentContent)
        if ($previousVersion -ne $currentVersion) {
            $csproj = GetCsproj($change)
            if (-not $csproj) {
                continue
            }

            if ($changesPerCsproj[$csproj]) {
                $ChangesPerCsproj[$csproj].stopProcessing = $true
            }
            else {
                $ChangesPerCsproj[$csproj] = @{
                    "commits"        = @()
                    "stopProcessing" = $true
                }
            }
        }
    }

    foreach ($change in $changes) {
        $csproj = GetCsproj($change)
        if (-not $csproj) {
            continue
        }

        if ($ChangesPerCsproj[$csproj]) {
            if ($ChangesPerCsproj[$csproj].stopProcessing) {
                continue
            }

            $ChangesPerCsproj[$csproj].commits += $commit
        }
        else {
            $ChangesPerCsproj[$csproj] = @{
                "commits"        = @($commit)
                "stopProcessing" = $false
            }
        }
    }
}

foreach ($csproj in $ChangesPerCsproj.Keys | Sort-Object) {
    $info = $ChangesPerCsproj[$csproj]
    if ($info.commits.Count -eq 0) {
        continue
    }

    # Find commit messages
    $messages = $info.commits | Select-Object -Unique | ForEach-Object { git log --format=%B -n 1 "$_" | Where-Object { $_.trim() -ne "" } }

    Write-Host "- $csproj"
    Write-Host "  Commits: $($info.commits.Count)"
    Write-Host "  Messages:"
    foreach ($message in $messages) {
        Write-Host "    - $message"
    }

    IncrementVersion($csproj)
}
