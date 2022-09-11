$TestsRootPath = Join-Path $PSScriptRoot ".." "tests" -Resolve
$Projs = Get-ChildItem $TestsRootPath -Filter *.csproj -Recurse

$UtilsPath = Join-Path $TestsRootPath "TestUtilities/TestUtilities.csproj" -Resolve
$ErrorCount = 0

foreach ($Proj in $Projs) {
    [xml]$ProjXml = Get-Content -LiteralPath $Proj.FullName
    $tfms = @()
    Select-Xml -Xml $ProjXml -XPath "/Project/PropertyGroup/TargetFrameworks" | ForEach-Object { $tfms += $_.Node.InnerText -split ";" }

    # Reference
    $References = Select-Xml -Xml $ProjXml -XPath "/Project/ItemGroup/ProjectReference/@Include"
    foreach ($Reference in $References) {
        $RefProj = Join-Path $Proj.DirectoryName $Reference.Node.'#text' -Resolve

        if ($RefProj -eq $UtilsPath) {
            continue;
        }

        # Only consider the main referenced project
        if(-Not [System.IO.Path]::GetFileNameWithoutExtension($Proj).StartsWith([System.IO.Path]::GetFileNameWithoutExtension($RefProj))) {
            continue;
        }

        [xml]$RefProjXml = Get-Content -LiteralPath $RefProj
        $RefTfms = @()
        Select-Xml -Xml $RefProjXml -XPath "/Project/PropertyGroup/TargetFrameworks" | ForEach-Object { $RefTfms += $_.Node.InnerText -split ";" }
        foreach ($RefTfm in $RefTfms) {
            if ($RefTfm -eq "netstandard2.0") {
                continue;
            }

            if ($RefTfm -eq "netstandard2.1") {
                continue;
            }

            if (-not $tfms.Contains($RefTfm)) {
                if($RefTfm -eq '$(LatestTargetFrameworks)' -and $tfms.Contains('$(LatestTargetFrameworksWindows)')) {
                    continue;
                }

                Write-Error "Project $($Proj.FullName) does not target $RefTfm, but it references $RefProj which does."
                $ErrorCount++
            }
        }
    }
}

if ($ErrorCount -gt 0) {
    exit 1
}