$ErrorActionPreference = "Stop"

$RootPath = Join-Path $PSScriptRoot ".." -Resolve
$SrcPath = Join-Path $RootPath "src"
$TrimmableCsprojPath = Join-Path $RootPath "Samples" "Trimmable" "Trimmable.csproj"
$TrimmableWpfCsprojPath = Join-Path $RootPath "Samples" "Trimmable.Wpf" "Trimmable.Wpf.csproj"
$TrimmableDir = Split-Path $TrimmableCsprojPath

# Find all IsTrimmable=true projects in src/
$TrimmableProjects = @()
$SrcProjects = Get-ChildItem $SrcPath -Filter *.csproj -Recurse
foreach ($Proj in $SrcProjects) {
    [xml]$ProjXml = Get-Content -LiteralPath $Proj.FullName
    foreach ($PropertyGroup in $ProjXml.Project.PropertyGroup) {
        if ($PropertyGroup.IsTrimmable -eq "true") {
            $TrimmableProjects += $Proj
            break
        }
    }
}

# Find projects already referenced in Trimmable.Wpf (these are considered covered)
$WpfReferencedProjectNames = @()
if (Test-Path $TrimmableWpfCsprojPath) {
    [xml]$WpfXml = Get-Content -LiteralPath $TrimmableWpfCsprojPath
    foreach ($ItemGroup in $WpfXml.Project.ItemGroup) {
        foreach ($Ref in $ItemGroup.ProjectReference) {
            if ($null -ne $Ref -and $null -ne $Ref.Include) {
                $RefFullPath = Join-Path (Split-Path $TrimmableWpfCsprojPath) $Ref.Include -Resolve
                $WpfReferencedProjectNames += [System.IO.Path]::GetFileNameWithoutExtension($RefFullPath)
            }
        }
    }
}

# Projects for Trimmable sample = IsTrimmable=true minus those already in Trimmable.Wpf
$ProjectsForTrimmable = $TrimmableProjects | Where-Object {
    $WpfReferencedProjectNames -notcontains $_.BaseName
} | Sort-Object -Property BaseName

# Generate csproj content with LF line endings
$lf = "`n"
$sb = [System.Text.StringBuilder]::new()
[void]$sb.Append("<Project Sdk=`"Meziantou.NET.Sdk`">$lf")
[void]$sb.Append("$lf")
[void]$sb.Append("  <PropertyGroup>$lf")
[void]$sb.Append("    <OutputType>Exe</OutputType>$lf")
[void]$sb.Append("    <TargetFramework>`$(LatestTargetFramework)</TargetFramework>$lf")
[void]$sb.Append("    <ImplicitUsings>enable</ImplicitUsings>$lf")
[void]$sb.Append("$lf")
[void]$sb.Append("    <TrimmerSingleWarn>false</TrimmerSingleWarn>$lf")
[void]$sb.Append("    <PublishTrimmed>true</PublishTrimmed>$lf")
[void]$sb.Append("    <TrimMode>full</TrimMode>$lf")
[void]$sb.Append("  </PropertyGroup>$lf")
[void]$sb.Append("$lf")
[void]$sb.Append("  <ItemGroup>$lf")

foreach ($Proj in $ProjectsForTrimmable) {
    $RelativePath = [System.IO.Path]::GetRelativePath($TrimmableDir, $Proj.FullName).Replace('/', '\')
    [void]$sb.Append("    <ProjectReference Include=`"$RelativePath`" />$lf")
}

[void]$sb.Append("  </ItemGroup>$lf")
[void]$sb.Append("$lf")
[void]$sb.Append("  <ItemGroup>$lf")

foreach ($Proj in $ProjectsForTrimmable) {
    $AssemblyName = $Proj.BaseName
    [void]$sb.Append("    <TrimmerRootAssembly Include=`"$AssemblyName`" />$lf")
}

[void]$sb.Append("  </ItemGroup>$lf")
[void]$sb.Append("</Project>$lf")

$NewContent = $sb.ToString()

# Read existing content, stripping BOM if present
$ExistingContent = ""
if (Test-Path $TrimmableCsprojPath) {
    $ExistingBytes = [System.IO.File]::ReadAllBytes($TrimmableCsprojPath)
    $offset = 0
    if ($ExistingBytes.Length -ge 3 -and $ExistingBytes[0] -eq 0xEF -and $ExistingBytes[1] -eq 0xBB -and $ExistingBytes[2] -eq 0xBF) {
        $offset = 3
    }
    $ExistingContent = [System.Text.Encoding]::UTF8.GetString($ExistingBytes, $offset, $ExistingBytes.Length - $offset)
}

# Normalize existing line endings for comparison
$NormalizedExisting = $ExistingContent -replace "`r`n", "`n"

if ($NormalizedExisting -ne $NewContent) {
    $Utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($TrimmableCsprojPath, $NewContent, $Utf8NoBom)

    Write-Warning "Samples/Trimmable/Trimmable.csproj was not up-to-date"
    git --no-pager diff $TrimmableCsprojPath
    exit 1
}

Write-Host "Samples/Trimmable/Trimmable.csproj is up-to-date"
