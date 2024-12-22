$TestsRootPath = Join-Path $PSScriptRoot ".." "tests" -Resolve
$Projs = Get-ChildItem $TestsRootPath -Filter *.csproj -Recurse

$Errors = $Projs | Foreach-Object -ThrottleLimit 5 -Parallel {

    $UtilsPath = Join-Path $using:TestsRootPath "TestUtilities/TestUtilities.csproj" -Resolve

    function SimplifyTfm($Tfm) {
        if ($Tfm.Contains('-')) {
            return $Tfm.Substring(0, $Tfm.IndexOf(('-')))
        }

        return $Tfm
    }


    $Proj = $PSItem
    $TestProjectTfms = $(dotnet build --getProperty:TargetFrameworks $Proj.FullName).Split(";") | ForEach-Object { SimplifyTfm($_) }

    [xml]$ProjXml = Get-Content -LiteralPath $Proj.FullName
    # Reference
    $References = Select-Xml -Xml $ProjXml -XPath "/Project/ItemGroup/ProjectReference/@Include"
    foreach ($Reference in $References) {
        $RefProj = Join-Path $Proj.DirectoryName $Reference.Node.'#text' -Resolve

        if ($RefProj -eq $UtilsPath) {
            continue;
        }

        # Only consider the main referenced project
        if (-Not [System.IO.Path]::GetFileNameWithoutExtension($Proj).StartsWith([System.IO.Path]::GetFileNameWithoutExtension($RefProj))) {
            continue;
        }

        $RefTfms = $(dotnet build --getProperty:TargetFrameworks $RefProj).Split(";")
        foreach ($RefTfm in $RefTfms) {
            $RefTfm = SimplifyTfm($RefTfm)
            if ($RefTfm -eq "netstandard2.0") {
                continue;
            }

            if ($RefTfm -eq "netstandard2.1") {
                continue;
            }

            if ($RefTfm -eq "net462" -and $TestProjectTfms.Contains("net472")) {
                continue;
            }

            if (-not $TestProjectTfms.Contains($RefTfm)) {
                Write-Error "Project $($Proj.FullName) does not target $RefTfm, but it references $RefProj which does. ($TestProjectTfms) != ($RefTfms)"
                return 1
            }
        }
    }

    return 0
}

# Check if any errors were found
if (($Errors | Measure-Object -Sum | Select-Object -ExpandProperty Sum) -gt 0) {
    exit 1
}