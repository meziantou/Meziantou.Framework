param (
    [Parameter()][string]$OutputPath = "slnx"
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$RootPath = Join-Path $PSScriptRoot ".." -Resolve
$SrcRootPath = Join-Path $RootPath "src" -Resolve
$TestsRootPath = Join-Path $RootPath "tests" -Resolve
$OutputRootPath = Join-Path $RootPath $OutputPath

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

$srcProjects = GetProjectFiles $SrcRootPath
$testsProjects = GetProjectFiles $TestsRootPath

$srcProjectSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($project in $srcProjects) {
    $null = $srcProjectSet.Add($project.FullName)
}

$testsProjectSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($project in $testsProjects) {
    $null = $testsProjectSet.Add($project.FullName)
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

    $projectsToAdd = @()
    $projectsToAdd += ($srcProjectsToInclude | Sort-Object)
    $projectsToAdd += ($testProjectsToInclude | Sort-Object)

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

if (Test-Path -LiteralPath $OutputRootPath) {
    foreach ($file in (Get-ChildItem -Path $OutputRootPath -Filter *.slnx -File)) {
        if (-not $generatedFiles.Contains($file.FullName)) {
            Remove-Item -LiteralPath $file.FullName
        }
    }
}

$slnxFiles = Get-ChildItem -Path $OutputRootPath -Filter *.slnx -File | Sort-Object FullName
foreach ($slnxFile in $slnxFiles) {
    $slnxPath = $slnxFile.FullName
    dotnet build $slnxPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed: $slnxPath"
    }

    dotnet test $slnxPath --no-build
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test failed: $slnxPath"
    }
}
