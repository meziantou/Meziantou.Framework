pwsh --version

# $env:NuGetDirectory= Join-Path $PSScriptRoot "../artifacts/package/debug" -Resolve
$PackagePath = (Get-ChildItem $env:NuGetDirectory | Where-Object FullName -Match "Meziantou.Framework.StronglyTypedId.[0-9.]+.nupkg").FullName
$AnnotationPath = Join-Path $PSScriptRoot ".." "src" "Meziantou.Framework.StronglyTypedId.Annotations" -Resolve

$Tfms = $(dotnet build --getProperty:TargetFrameworks $AnnotationPath).Split(";")

[Reflection.Assembly]::LoadWithPartialName('System.IO.Compression.FileSystem')
$Entries = [IO.Compression.ZipFile]::OpenRead($PackagePath).Entries.FullName
foreach ($Tfm in $Tfms) {
    # Check if there is an entry with a path that starts with "lib/$Tfm/"
    $Entry = $Entries | Where-Object { $_.StartsWith("lib/$Tfm/") }
    if (-not $Entry) {
        Write-Error "Package does not contain a lib/$Tfm/ entry"
        exit 1
    }
}

dotnet tool update Meziantou.Framework.NuGetPackageValidation.Tool --global --no-cache --add-source $env:NuGetDirectory
$files = Get-ChildItem "$env:NuGetDirectory/*" -Include *.nupkg

& meziantou.validate-nuget-package @files --excluded-rules "ReadmeMustBeSet,TagsMustBeSet" --github-token=$env:GITHUB_TOKEN --only-report-errors
if ($LASTEXITCODE -ne 0) {
    exit 1
}