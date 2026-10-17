<#
.SYNOPSIS
Opens an issue for each target of a fuzz project that failed in the weekly fuzzing campaign (.github/workflows/fuzz.yml).

.DESCRIPTION
Reads the result.json and failure.md files that eng/run-fuzz.ps1 (SharpFuzz projects) and eng/run-imaging-fuzz.ps1
(Imaging fuzz tests) write for each target of a fuzz project. Each failed target gets an issue titled
"Fuzzing: <target> failed", with the failure report, the failing input and the commands that reproduce it. When an open
issue with the same title and all the labels exists, the details are added to it as a comment instead. A failed campaign
without any result for the project (a build error, a job timeout) gets a single issue pointing to the run.

Reads GITHUB_SHA, RUN_URL and FUZZ_JOB_RESULT from the environment.
#>
param(
    # The fuzz project, for instance Meziantou.Framework.Yaml.Fuzz
    [Parameter(Mandatory)]
    [string]$Project,

    # The results of the project (the fuzz-results-<project> artifacts)
    [Parameter(Mandatory)]
    [string]$ResultsDirectory,

    # Labels of the issues, created when missing. An open issue with the same title and all these labels is commented
    # on instead of opening another one.
    [Parameter(Mandatory)]
    [string[]]$Labels,

    # Writes the issues to this directory instead of calling gh
    [string]$DryRunDirectory
)

$ErrorActionPreference = 'Stop'

# GitHub refuses an issue body or comment longer than 65536 characters
$MaxBodyLength = 60000
$RunUrl = $env:RUN_URL
$Commit = $env:GITHUB_SHA

function Limit-Length([string]$Text) {
    if ($Text.Length -le $MaxBodyLength) {
        return $Text
    }

    return $Text.Substring(0, $MaxBodyLength) + "`n`n... (truncated, see the run: $RunUrl)"
}

# 'a,b' arrives as a single value through pwsh -File
$Labels = @($Labels | ForEach-Object { $_ -split ',' } | ForEach-Object Trim | Where-Object { $_ })
$issues = [System.Collections.Generic.List[object]]::new()
$results = @(if (Test-Path -LiteralPath $ResultsDirectory) { Get-ChildItem -Path $ResultsDirectory -Recurse -Filter 'result.json' | Sort-Object FullName })
foreach ($file in $results) {
    $result = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if (-not $result.failed) {
        continue
    }

    $failure = Join-Path $file.DirectoryName 'failure.md'
    $details = if (Test-Path -LiteralPath $failure) { Get-Content -LiteralPath $failure -Raw } else { "No failure report, see the run." }
    $body = [System.Text.StringBuilder]::new()
    [void]$body.AppendLine("The weekly fuzzing campaign found a failure in the target ``$($result.target)`` of ``fuzz/$($result.project)``.")
    [void]$body.AppendLine()
    [void]$body.AppendLine("- Run: $RunUrl (the failing inputs and the logs are in the ``fuzz-results-$($result.project)`` artifacts)")
    [void]$body.AppendLine("- Commit: $Commit")
    [void]$body.AppendLine()
    [void]$body.AppendLine($details)
    $issues.Add([pscustomobject]@{ Title = "Fuzzing: $($result.target) failed"; Body = Limit-Length $body.ToString() })
}

# The jobs of a project always upload the results of the targets they fuzzed; none means they failed before
# (FUZZ_JOB_RESULT is the result of all the jobs of the matrix, so it says that some project failed, not which one)
if ($results.Count -eq 0 -and $env:FUZZ_JOB_RESULT -eq 'failure') {
    $body = "The weekly fuzzing campaign of ``fuzz/$Project`` failed without any target result: a build error, an error of the tools, or a job timeout. See the run: $RunUrl`n`n- Commit: $Commit"
    $issues.Add([pscustomobject]@{ Title = "Fuzzing: the campaign of $Project failed without a reported target failure"; Body = $body })
}

if ($issues.Count -eq 0) {
    Write-Host 'No fuzz failure.'
    exit 0
}

if ($DryRunDirectory) {
    New-Item -ItemType Directory -Path $DryRunDirectory -Force | Out-Null
    $index = 0
    foreach ($issue in $issues) {
        $index++
        Set-Content -LiteralPath (Join-Path $DryRunDirectory "issue-$index.md") -Value "# $($issue.Title)`n`nLabels: $($Labels -join ', ')`n`n$($issue.Body)"
    }

    Write-Host "$($issues.Count) issue(s) written to $DryRunDirectory"
    exit 0
}

function Invoke-Gh {
    & gh @args
    if ($LASTEXITCODE -ne 0) {
        throw "gh $($args[0]) $($args[1]) failed with exit code $LASTEXITCODE"
    }
}

$existingLabels = gh label list --limit 1000 --json name --jq '.[].name'
if ($LASTEXITCODE -ne 0) {
    throw "gh label list failed with exit code $LASTEXITCODE"
}

foreach ($label in $Labels | Where-Object { $_ -cnotin $existingLabels }) {
    Invoke-Gh label create $label --description 'Added to the issues of the weekly fuzzing campaign'
}

$labelArguments = $Labels | ForEach-Object { '--label', $_ }
$openIssues = gh issue list @labelArguments --state open --limit 500 --json number,title | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) {
    throw "gh issue list failed with exit code $LASTEXITCODE"
}

foreach ($issue in $issues) {
    $bodyFile = New-TemporaryFile
    try {
        Set-Content -LiteralPath $bodyFile -Value $issue.Body -NoNewline
        $existing = $openIssues | Where-Object { $_.title -ceq $issue.Title } | Select-Object -First 1
        if ($existing) {
            Write-Host "Commenting on #$($existing.number): $($issue.Title)"
            Invoke-Gh issue comment $existing.number --body-file $bodyFile
        }
        else {
            Write-Host "Opening: $($issue.Title)"
            Invoke-Gh issue create --title $issue.Title --body-file $bodyFile @labelArguments
        }
    }
    finally {
        Remove-Item -LiteralPath $bodyFile -Force
    }
}
