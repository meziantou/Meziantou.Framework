param (
    [Parameter()][bool]$CreatePullRequest,
    [Parameter()][int]$NumberOfCommits = 50
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$skippedCommits = @("ef6862b9195bd864e1f449317edc85a19041149f", "c4ac0bdd868a37b37e2eddd6ae99b7b888f3fd29")
function GetVersion($fileContent) {
    $fileContent = ($fileContent | Out-String) -replace "∩╗┐", ""
    $fileContent = $fileContent -replace "^.*?<", "<"

    try {
        $doc = [xml]$fileContent
        $version = $doc.Project.PropertyGroup.Version
        return $version
    }
    catch {
        throw "Cannot parse the version in the csproj file`n$fileContent"
    }
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
        return $false
    }

    $doc.Project.PropertyGroup.Version = $newVersion
    $doc.Save($csprojPath)
    return $true
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

function GetProjectReferences($csprojPath) {
    try {
        $doc = [xml]::new()
        $doc.Load($csprojPath)
        $projectReferences = @()

        foreach ($itemGroup in $doc.Project.ItemGroup) {
            if ($itemGroup.ProjectReference) {
                foreach ($projRef in $itemGroup.ProjectReference) {
                    if ($projRef.Include) {
                        # Convert relative path to absolute path
                        $relativePath = $projRef.Include
                        $referencedProject = Join-Path (Split-Path $csprojPath -Parent) $relativePath -Resolve
                        if (Test-Path $referencedProject) {
                            # Only include references to projects under src/ folder
                            $srcPath = Join-Path $RootPath "src"
                            if ($referencedProject.StartsWith($srcPath, [System.StringComparison]::OrdinalIgnoreCase)) {
                                $projectReferences += $referencedProject
                            }
                        }
                    }
                }
            }
        }

        return $projectReferences
    }
    catch {
        return @()
    }
}

function BuildDependencyGraph($csprojFiles) {
    $dependencyGraph = @{}
    $reverseDependencyGraph = @{}

    foreach ($csproj in $csprojFiles) {
        $dependencyGraph[$csproj] = GetProjectReferences($csproj)
        $reverseDependencyGraph[$csproj] = @()
    }

    # Build reverse dependency graph (which projects depend on each project)
    foreach ($csproj in $csprojFiles) {
        foreach ($dependency in $dependencyGraph[$csproj]) {
            if ($reverseDependencyGraph.ContainsKey($dependency)) {
                $reverseDependencyGraph[$dependency] += $csproj
            }
        }
    }

    return @{
        "Dependencies" = $dependencyGraph
        "ReverseDependencies" = $reverseDependencyGraph
    }
}

function GetTransitiveDependents($csproj, $reverseDependencyGraph, $visited = @{}) {
    if ($visited.ContainsKey($csproj)) {
        return @()
    }

    $visited[$csproj] = $true
    $dependents = @()

    if ($reverseDependencyGraph.ContainsKey($csproj)) {
        foreach ($dependent in $reverseDependencyGraph[$csproj]) {
            $dependents += $dependent
            $dependents += GetTransitiveDependents $dependent $reverseDependencyGraph $visited
        }
    }

    return $dependents | Select-Object -Unique
}

$RootPath = Join-Path $PSScriptRoot ".." -Resolve

$commits = git log --pretty=format:'%H' -n $NumberOfCommits
Write-Host "Commits loaded ($($commits.Length) commits)"

$ChangesPerCsproj = @{}
$allCsprojFiles = @()

$srcPath = Join-Path $RootPath "src"
foreach ($file in Get-ChildItem -Path $srcPath -Recurse -Filter *.csproj) {
    Write-Host "Project file detected: $($file.FullName)"
    $allCsprojFiles += $file.FullName
    $ChangesPerCsproj[$file.FullName] = @{
        "commits"        = @()
        "stopProcessing" = $false
        "updatedDueToDependency" = $false
    }
}

# Build dependency graph
Write-Host "Building dependency graph..."
$dependencyInfo = BuildDependencyGraph($allCsprojFiles)
$reverseDependencyGraph = $dependencyInfo.ReverseDependencies

# Print dependency graph
Write-Host "Dependency Graph:"
foreach ($csproj in ($dependencyInfo.Dependencies.Keys | Sort-Object)) {
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($csproj)
    $dependencies = $dependencyInfo.Dependencies[$csproj]

    if ($dependencies.Count -gt 0) {
        foreach ($dependency in ($dependencies | Sort-Object)) {
            $dependencyName = [System.IO.Path]::GetFileNameWithoutExtension($dependency)
            Write-Host "$projectName -> $dependencyName"
        }
    }
}
Write-Host ""

$i = 0;
foreach ($commit in $commits) {
    if ($skippedCommits -contains $commit) {
        Write-Host "Skipping commit $commit"
        continue
    }

    $i++
    Write-Host "Processing commit $i/$($commits.Length): $commit"

    # Get the changes in the commit
    $changes = git diff --name-only "$commit" "$commit~1"
    $changes = $changes | Where-Object { $_.StartsWith("src/") }

    # Process csproj first to check if the version changed
    foreach ($change in $changes | Where-Object { $_ -like "*.csproj" }) {
        try {
            # The previous version may not exists if the file was added in the commit
            $previousContent = $(git show "${commit}~1:$change")
        }
        catch {
            $previousContent = ""
        }

        Write-Host "Getting previous version of $change"
        $previousVersion = GetVersion($previousContent)

        $currentContent = git show "${commit}:$change"

        Write-Host "Getting current version of $change"
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

$updated = $false
$prMessage = ""
$projectsToUpdate = @{}

# First pass: identify projects that have direct changes
foreach ($csproj in $ChangesPerCsproj.Keys | Sort-Object) {
    $info = $ChangesPerCsproj[$csproj]
    if ($info.commits.Count -gt 0) {
        $projectsToUpdate[$csproj] = $info
    }
}

# Second pass: identify projects that need to be updated due to dependencies
foreach ($csproj in ($projectsToUpdate.Keys | ForEach-Object { $_ })) {
    $dependents = GetTransitiveDependents $csproj $reverseDependencyGraph
    foreach ($dependent in $dependents) {
        if (-not $projectsToUpdate.ContainsKey($dependent)) {
            Write-Host "Project $dependent will be updated due to dependency on $csproj"
            $packageName = [System.IO.Path]::GetFileNameWithoutExtension($csproj)
            $projectsToUpdate[$dependent] = @{
                "commits" = @()
                "stopProcessing" = $false
                "updatedDueToDependency" = $true
                "dependencySource" = $packageName
            }
        }
    }
}

foreach ($csproj in $projectsToUpdate.Keys | Sort-Object) {
    $info = $projectsToUpdate[$csproj]

    if (IncrementVersion($csproj)) {
        $updated = $true

        if ($prMessage) {
            $prMessage += "`n"
        }

        $packageName = [System.IO.Path]::GetFileNameWithoutExtension($csproj)
        $prMessage += "## $packageName`n"

        if ($info.updatedDueToDependency) {
            $prMessage += "- Updated due to dependency on $($info.dependencySource)`n"
        } else {
            foreach ($commit in ($info.commits | Select-Object -Unique)) {
                $message = git log --format=%B -n 1 $commit
                $message = $message -replace "\r?\n", "`n"
                $message = $message -replace "\s+", " "
                $message = $message.Replace("Co-authored-by: renovate[bot] <29139614+renovate[bot]@users.noreply.github.com>", "", [System.StringComparison]::OrdinalIgnoreCase)
                $message = $message.Trim()
                $prMessage += "- ${commit}: $message`n"
            }
        }
    }
}

if ($updated) {
    Write-Host $prMessage
    if ($CreatePullRequest) {
        Write-Host "Commiting changes"
        git config --global user.email "git@meziantou.net"
        git config --global user.name "meziantou"
        git add .
        git commit -m "Bump package versions`n`n$prMessage"

        Write-Host "Pushing changes"
        git push origin main:generated/bump-package-versions --force

        # Give some time to process the push
        Start-Sleep -Seconds 10

        Write-Host "Listing existing pull requests"
        $OpenPR = gh pr list --repo meziantou/meziantou.framework --head generated/bump-package-versions --json number | ConvertFrom-Json
        if ($OpenPR) {
            Write-Host "Editing existing pull request $($OpenPR.number)"
            gh pr edit $OpenPR.number --title "Bump package versions" --body $prMessage
        }
        else {
            Write-Host "Creating new pull request"
            gh pr create --title "Bump package versions" --body $prMessage --base main --head generated/bump-package-versions
        }
    }
}
else {
    Write-Host "No version updated"
}