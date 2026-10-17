<#
.SYNOPSIS
Opens an issue for each test that failed in the weekly Imaging fuzzing campaign (.github/workflows/fuzz-imaging.yml).

.DESCRIPTION
Reads the .trx files of the campaign. Each failed test gets an issue titled "Imaging fuzzing: <test> failed" with the
failure messages (signature, case, minimized input in base64) and the commands that reproduce it. When an open issue
with the same title exists, the details are added to it as a comment instead. A failed campaign without any failed
test (a hang dump, a crash, a build error) gets a single issue pointing to the run.

Reads GITHUB_SHA, RUN_URL, FUZZ_JOB_RESULT, MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_SEED and
MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_DURATION_SECONDS from the environment.
#>
param(
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

$Project = 'fuzz/Meziantou.Framework.Imaging.FuzzTests'
# GitHub refuses an issue body or comment longer than 65536 characters
$MaxBodyLength = 60000
$Seed = $env:MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_SEED
$Duration = $env:MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_DURATION_SECONDS
$RunUrl = $env:RUN_URL
$Commit = $env:GITHUB_SHA
$Namespace = @{ t = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010' }

function Get-Fence([string]$Text) {
    # A fence longer than any backtick run of the text, so the text cannot close it
    $longest = ([regex]::Matches($Text, '`+') | ForEach-Object Length | Measure-Object -Maximum).Maximum
    return '`' * [Math]::Max(3, $longest + 1)
}

function Limit-Length([string]$Text) {
    if ($Text.Length -le $MaxBodyLength) {
        return $Text
    }

    return $Text.Substring(0, $MaxBodyLength) + "`n`n... (truncated, see the run: $RunUrl)"
}

# Failed tests by name; a test fails once per target framework, usually with the same message
$failures = [ordered]@{}
# The merged report repeats the results of the per-framework reports
foreach ($file in Get-ChildItem -Path $ResultsDirectory -Recurse -Filter '*.trx' | Where-Object { $_.Directory.Name -ne 'merged' }) {
    [xml]$trx = Get-Content -LiteralPath $file.FullName -Raw
    $methods = @{}
    foreach ($test in Select-Xml -Xml $trx -XPath '//t:UnitTest' -Namespace $Namespace) {
        $methods[$test.Node.id] = $test.Node.SelectSingleNode('*[local-name()="TestMethod"]')
    }

    foreach ($result in Select-Xml -Xml $trx -XPath '//t:UnitTestResult[@outcome="Failed"]' -Namespace $Namespace) {
        $node = $result.Node
        $method = $methods[$node.testId]
        $framework = if ($method -and $method.codeBase -match '(net\d+\.\d+)') { $Matches[1] } else { 'unknown framework' }
        $message = $node.SelectSingleNode('*[local-name()="Output"]/*[local-name()="ErrorInfo"]/*[local-name()="Message"]')?.InnerText
        $stdout = $node.SelectSingleNode('*[local-name()="Output"]/*[local-name()="StdOut"]')?.InnerText
        if (-not $failures.Contains($node.testName)) {
            $failures[$node.testName] = [pscustomobject]@{
                ClassName  = ${method}?.className
                MethodName = ${method}?.name
                Results    = [System.Collections.Generic.List[object]]::new()
            }
        }

        $failures[$node.testName].Results.Add([pscustomobject]@{ Framework = $framework; Message = "$message".Trim(); StdOut = "$stdout".Trim() })
    }
}

$issues = [System.Collections.Generic.List[object]]::new()
foreach ($testName in $failures.Keys) {
    $failure = $failures[$testName]
    $frameworks = @($failure.Results.Framework | Sort-Object -Unique)
    $body = [System.Text.StringBuilder]::new()
    [void]$body.AppendLine("The weekly Imaging fuzzing campaign found a failure in ``$testName``.")
    [void]$body.AppendLine()
    [void]$body.AppendLine("- Run: $RunUrl (the minimized and original inputs are in the ``fuzz-*`` artifacts)")
    [void]$body.AppendLine("- Commit: $Commit")
    [void]$body.AppendLine("- Base seed: ``$Seed``, $Duration s per campaign")
    [void]$body.AppendLine("- Target frameworks: $($frameworks -join ', ')")

    # Frameworks that failed the same way share one section
    foreach ($group in $failure.Results | Group-Object Message) {
        [void]$body.AppendLine()
        [void]$body.AppendLine("### Failure ($(@($group.Group.Framework | Sort-Object -Unique) -join ', '))")
        [void]$body.AppendLine()
        $fence = Get-Fence $group.Name
        [void]$body.AppendLine("${fence}text")
        [void]$body.AppendLine($group.Name)
        [void]$body.AppendLine($fence)
        $stdout = $group.Group[0].StdOut
        if ($stdout) {
            [void]$body.AppendLine()
            $fence = Get-Fence $stdout
            [void]$body.AppendLine("Campaign statistics:")
            [void]$body.AppendLine()
            [void]$body.AppendLine("${fence}text")
            [void]$body.AppendLine($stdout)
            [void]$body.AppendLine($fence)
        }
    }

    # The cases are deterministic for a base seed: a campaign that stops right after the failing case ("mutation N",
    # "iteration N") replays it without the time budget
    $lastCase = ([regex]::Matches(($failure.Results.Message -join "`n"), '\b(?:mutation|iteration) (\d+)\b') | ForEach-Object { [long]($_.Groups[1].Value) } | Measure-Object -Maximum).Maximum
    $iterations = if ($null -ne $lastCase) { $lastCase + 1 } else { $null }
    $group = if ($testName -match 'group: "([^"]+)"') { $Matches[1] } else { $null }
    $framework = if ($frameworks[0] -ne 'unknown framework') { " --framework $($frameworks[0])" } else { '' }
    $filter = if ($failure.ClassName -and $failure.MethodName) { " --filter-method $($failure.ClassName).$($failure.MethodName)" } else { '' }
    [void]$body.AppendLine()
    [void]$body.AppendLine('### Reproduce')
    [void]$body.AppendLine()
    [void]$body.AppendLine('```bash')
    [void]$body.AppendLine("git checkout $Commit")
    [void]$body.AppendLine("export MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_SEED=$Seed")
    if ($group) {
        [void]$body.AppendLine("export MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_GROUPS=$group")
    }

    if ($iterations) {
        [void]$body.AppendLine("export MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_ITERATIONS=$iterations")
    }
    else {
        # Without a case number, the campaign has to run as long as on CI to reach the case
        [void]$body.AppendLine("export MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_ITERATIONS=2000000000")
        [void]$body.AppendLine("export MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_DURATION_SECONDS=$Duration")
    }

    [void]$body.AppendLine("dotnet test --project `"`$PWD/$Project`" --configuration Release$framework$filter")
    [void]$body.AppendLine('```')
    [void]$body.AppendLine()
    [void]$body.AppendLine("Once fixed, commit the minimized input under ``$Project/FuzzRegressions`` (see ``regressions.json``).")
    $issues.Add([pscustomobject]@{ Title = "Imaging fuzzing: $testName failed"; Body = Limit-Length $body.ToString() })
}

if ($issues.Count -eq 0 -and $env:FUZZ_JOB_RESULT -eq 'failure') {
    $body = "The weekly Imaging fuzzing campaign failed without a failed test in its reports: a test host killed by the hang dump (a campaign running longer than 10 minutes), a crash, or a build error. See the run: $RunUrl`n`n- Commit: $Commit`n- Base seed: ``$Seed``, $Duration s per campaign"
    $issues.Add([pscustomobject]@{ Title = 'Imaging fuzzing: the campaign failed without a reported test failure'; Body = $body })
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
