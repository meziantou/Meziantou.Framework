$SrcRootPath = Join-Path $PSScriptRoot ".." "src" -Resolve
$EditedFiles = 0

$Files = Get-ChildItem $SrcRootPath -Recurse -Include *.cs, *.csproj, *.fsproj, *.proj, *.props, *.targets, *.save, *.slnx, *.ps1, *.yml, *.yaml, *.md, *.json
foreach ($file in $Files) {
    Write-Host "Processing $($file.FullName)"

    $content = Get-Content -LiteralPath $file -AsByteStream -ReadCount 0
    if($content.Length -lt 3) {
        continue
    }

    if($content[0] -eq 0xEF -and $content[1] -eq 0xBB -and $content[2] -eq 0xBF) {
        Write-Warning "File $($file.FullName) contains BOM. Removing it."
        $content = $content[3..($content.Length - 1)]

        Set-Content -LiteralPath $file -Value $content -AsByteStream
        $EditedFiles += 1
    }
}

exit $EditedFiles