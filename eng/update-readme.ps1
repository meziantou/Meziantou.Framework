$SrcRootPath = Join-Path $PSScriptRoot ".." "src" -Resolve
$ReadmePath = Join-Path $PSScriptRoot ".." "README.md" -Resolve
$Projs = Get-ChildItem $SrcRootPath -Filter *.csproj -Recurse | Sort-Object -Property BaseName

$OriginalContent = Get-Content $ReadmePath

$content = "| Name | Version |`n";
$content += "| :--- | :---: |`n";
foreach ($Proj in $Projs) {
    [xml]$ProjContent = Get-Content -LiteralPath $Proj
    if($ProjContent.Project.PropertyGroup.IsPackable -eq $False) {
        continue;
    }

    $FileName = $Proj.BaseName;
    $content += "| $FileName | [![NuGet](https://img.shields.io/nuget/v/$FileName.svg)](https://www.nuget.org/packages/$FileName/) |`n"
}

$NewContent = "";
$IsInPackages = $false
foreach ($line in $OriginalContent) {
    if ($line -eq "# NuGet packages") {
        $NewContent += "$line`n`n$content`n"
        $IsInPackages = $true;
    }
    else {
        if ($line -like "#*") {
            $IsInPackages = $false
        }
    }

    if (!$IsInPackages) {
        $NewContent += "$line`n"
    }
}

$NewContent = $NewContent.TrimEnd("`n", "`r")

if(($OriginalContent -join "`n") -ne $NewContent)
{
    # This script is used in CI. exit 1 makes the CI fails
    Set-Content -Path $ReadmePath -Value $NewContent
    Write-Warning "README was not up-to-date"
    exit 1
}