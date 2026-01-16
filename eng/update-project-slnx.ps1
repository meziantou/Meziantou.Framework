param (
    [Parameter()][string]$OutputPath = "slnx"
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$RootPath = Join-Path $PSScriptRoot ".." -Resolve
$SrcRootPath = Join-Path $RootPath "src" -Resolve
$TestsRootPath = Join-Path $RootPath "tests" -Resolve
$ToolsRootPath = Join-Path $RootPath "tools" -Resolve
$OutputRootPath = Join-Path $RootPath $OutputPath
$MainSolutionPath = Join-Path $RootPath "Meziantou.Framework.slnx"

function GetProjectFiles($rootPath) {
    return Get-ChildItem -Path $rootPath -Recurse -File -Include *.csproj, *.fsproj
}

function GetProjectReferences($projectPath) {
    try {
        $doc = [xml]::new()
        $doc.Load($projectPath)
    }
    catch {
        return @()
    }

    $references = @()
    foreach ($itemGroup in $doc.Project.ItemGroup) {
        if ($itemGroup.ProjectReference) {
            foreach ($projRef in $itemGroup.ProjectReference) {
                $include = $projRef.Include
                if (-not $include) {
                    continue
                }

                $candidatePath = Join-Path (Split-Path $projectPath -Parent) $include
                if (-not (Test-Path -LiteralPath $candidatePath)) {
                    continue
                }

                $resolvedPath = (Resolve-Path -LiteralPath $candidatePath).Path
                $references += $resolvedPath
            }
        }
    }

    return $references
}

function GetProjectReferencesInSet($projectPath, $projectSet) {
    $references = @()
    foreach ($reference in (GetProjectReferences $projectPath)) {
        if ($projectSet.Contains($reference)) {
            $references += $reference
        }
    }

    return $references
}

function GetTransitiveReferences($projectPath, $projectSet) {
    $queue = [System.Collections.Generic.Queue[string]]::new()
    foreach ($reference in (GetProjectReferencesInSet $projectPath $projectSet)) {
        $queue.Enqueue($reference)
    }

    $result = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    while ($queue.Count -gt 0) {
        $current = $queue.Dequeue()
        if (-not $result.Add($current)) {
            continue
        }

        foreach ($reference in (GetProjectReferencesInSet $current $projectSet)) {
            $queue.Enqueue($reference)
        }
    }

    return $result
}

function ConvertToRelativePath($path) {
    $relativePath = [System.IO.Path]::GetRelativePath($RootPath, $path)
    return $relativePath.Replace("\", "/")
}

function ApplySolutionFolders($slnxPath, $projectsToAdd, $solutionFolderByProjectPath) {
    if (-not (Test-Path -LiteralPath $slnxPath)) {
        return
    }

    $doc = [xml]::new()
    $doc.Load($slnxPath)

    $solutionNode = $doc.Solution
    if (-not $solutionNode) {
        return
    }

    $folderNodesByName = @{}
    foreach ($folderNode in @($solutionNode.Folder)) {
        $folderNodesByName[$folderNode.Name] = $folderNode
    }

    $projectNodesByRelativePath = @{}
    foreach ($projectNode in $doc.SelectNodes("//Project")) {
        $projectPath = $projectNode.Path
        if ($projectPath) {
            $projectNodesByRelativePath[$projectPath] = $projectNode
        }
    }

    foreach ($projectPath in $projectsToAdd) {
        if ([string]::IsNullOrWhiteSpace($projectPath)) {
            continue
        }

        $folderName = $null
        if (-not $solutionFolderByProjectPath.TryGetValue($projectPath, [ref]$folderName)) {
            continue
        }

        if ([string]::IsNullOrWhiteSpace($folderName)) {
            continue
        }

        $relativePath = ConvertToRelativePath $projectPath
        if ([string]::IsNullOrWhiteSpace($relativePath)) {
            continue
        }
        $projectNode = $null
        if (-not $projectNodesByRelativePath.TryGetValue($relativePath, [ref]$projectNode)) {
            continue
        }

        $folderNode = $null
        if (-not $folderNodesByName.TryGetValue($folderName, [ref]$folderNode)) {
            $newFolderNode = $doc.CreateElement("Folder")
            $null = $newFolderNode.SetAttribute("Name", $folderName)
            $null = $solutionNode.AppendChild($newFolderNode)
            $folderNodesByName[$folderName] = $newFolderNode
            $folderNode = $newFolderNode
        }

        if ($projectNode.ParentNode -ne $folderNode) {
            $null = $projectNode.ParentNode.RemoveChild($projectNode)
            $null = $folderNode.AppendChild($projectNode)
        }
    }

    $doc.Save($slnxPath)
}

$srcProjects = GetProjectFiles $SrcRootPath
$testsProjects = GetProjectFiles $TestsRootPath
$toolsProjects = GetProjectFiles $ToolsRootPath

$solutionFolderByProjectPath = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::OrdinalIgnoreCase)
if (Test-Path -LiteralPath $MainSolutionPath) {
    $mainSolution = [xml]::new()
    $mainSolution.Load($MainSolutionPath)
    if ($mainSolution.Solution) {
        foreach ($folderNode in @($mainSolution.Solution.Folder)) {
            $folderName = $folderNode.Name
            foreach ($projectNode in @($folderNode.Project)) {
                $projectPath = $projectNode.Path
                if (-not $projectPath) {
                    continue
                }

                $fullPath = Resolve-Path -LiteralPath (Join-Path $RootPath $projectPath) -ErrorAction SilentlyContinue
                if ($fullPath) {
                    $solutionFolderByProjectPath[$fullPath.Path] = $folderName
                }
            }
        }
    }
}

$srcProjectSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($project in $srcProjects) {
    $null = $srcProjectSet.Add($project.FullName)
}

$testsProjectSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($project in $testsProjects) {
    $null = $testsProjectSet.Add($project.FullName)
}

$toolsProjectSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($project in $toolsProjects) {
    $null = $toolsProjectSet.Add($project.FullName)
}

$toolProjectsBySrcName = @{
    "Meziantou.Framework.Http.Hsts" = "Meziantou.Framework.Http.Hsts.Generator"
    "Meziantou.Framework.Unicode" = "Meziantou.Framework.Unicode.Generator"
}

$toolsBySrcProject = @{}
foreach ($project in $srcProjects) {
    $toolsBySrcProject[$project.FullName] = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
}

foreach ($toolProject in $toolsProjects) {
    $toolProjectName = [System.IO.Path]::GetFileNameWithoutExtension($toolProject.FullName)
    foreach ($srcName in $toolProjectsBySrcName.Keys) {
        if ($toolProjectsBySrcName[$srcName] -ne $toolProjectName) {
            continue
        }

        $srcProject = $srcProjects | Where-Object { [System.IO.Path]::GetFileNameWithoutExtension($_.FullName) -eq $srcName }
        if ($srcProject) {
            $null = $toolsBySrcProject[$srcProject.FullName].Add($toolProject.FullName)
        }
    }
}

$testsBySrcProject = @{}
foreach ($project in $srcProjects) {
    $testsBySrcProject[$project.FullName] = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
}

foreach ($testProject in $testsProjects) {
    foreach ($reference in (GetProjectReferencesInSet $testProject.FullName $srcProjectSet)) {
        $null = $testsBySrcProject[$reference].Add($testProject.FullName)
    }
}

if (-not (Test-Path -LiteralPath $OutputRootPath)) {
    New-Item -Path $OutputRootPath -ItemType Directory | Out-Null
}

$generatedFiles = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$plans = @()

foreach ($project in ($srcProjects | Sort-Object FullName)) {
    $fileName = "{0}.slnx" -f [System.IO.Path]::GetFileNameWithoutExtension($project.FullName)
    $outputFile = Join-Path $OutputRootPath $fileName

    $srcProjectsToInclude = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $null = $srcProjectsToInclude.Add($project.FullName)

    foreach ($dependency in (GetTransitiveReferences $project.FullName $srcProjectSet)) {
        $null = $srcProjectsToInclude.Add($dependency)
    }

    $testProjectsToInclude = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($testProject in $testsBySrcProject[$project.FullName]) {
        $null = $testProjectsToInclude.Add($testProject)
        foreach ($dependency in (GetTransitiveReferences $testProject $testsProjectSet)) {
            $null = $testProjectsToInclude.Add($dependency)
        }
    }

    $toolProjectsToInclude = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($toolProject in $toolsBySrcProject[$project.FullName]) {
        $null = $toolProjectsToInclude.Add($toolProject)
    }

    $projectsToAdd = @()
    $projectsToAdd += ($srcProjectsToInclude | Sort-Object)
    $projectsToAdd += ($testProjectsToInclude | Sort-Object)
    $projectsToAdd += ($toolProjectsToInclude | Sort-Object)

    $plans += [pscustomobject]@{
        ProjectName = [System.IO.Path]::GetFileNameWithoutExtension($project.FullName)
        OutputFile = $outputFile
        ProjectsToAdd = $projectsToAdd
    }

    $null = $generatedFiles.Add($outputFile)
}


$plans | ForEach-Object {
    $outputFile = $PSItem.OutputFile

    if (Test-Path -LiteralPath $outputFile) {
        Remove-Item -LiteralPath $outputFile
    }

    dotnet new sln -n $PSItem.ProjectName -o (Split-Path $outputFile -Parent) --force | Out-Null
}

$plans | ForEach-Object -Parallel {
    $outputFile = $PSItem.OutputFile
    $projectsToAdd = $PSItem.ProjectsToAdd

    if (-not (Test-Path -LiteralPath $outputFile)) {
        throw "Solution file not found: $outputFile"
    }

    if ($projectsToAdd.Count -gt 0) {
        dotnet sln $outputFile add $projectsToAdd | Out-Null
    }
} -ThrottleLimit 8

$plans | ForEach-Object {
    ApplySolutionFolders -slnxPath $PSItem.OutputFile -projectsToAdd $PSItem.ProjectsToAdd -solutionFolderByProjectPath $solutionFolderByProjectPath
}

if (Test-Path -LiteralPath $OutputRootPath) {
    foreach ($file in (Get-ChildItem -Path $OutputRootPath -Filter *.slnx -File)) {
        if (-not $generatedFiles.Contains($file.FullName)) {
            Remove-Item -LiteralPath $file.FullName
        }
    }
}

# $slnxFiles = Get-ChildItem -Path $OutputRootPath -Filter *.slnx -File | Sort-Object FullName
# foreach ($slnxFile in $slnxFiles) {
#     $slnxPath = $slnxFile.FullName
#     dotnet build $slnxPath
#     if ($LASTEXITCODE -ne 0) {
#         throw "dotnet build failed: $slnxPath"
#     }

#     dotnet test $slnxPath --no-build
#     if ($LASTEXITCODE -ne 0) {
#         throw "dotnet test failed: $slnxPath"
#     }
# }
