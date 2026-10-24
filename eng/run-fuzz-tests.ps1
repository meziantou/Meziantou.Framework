<#
.SYNOPSIS
Runs a campaign of the tests of a fuzz test project (fuzz/Meziantou.Framework.Imaging.FuzzTests,
fuzz/Meziantou.Framework.Pdf.FuzzTests) and writes its results like eng/run-fuzz.ps1 does.

.DESCRIPTION
Builds the project and runs its tests with the budget the <SettingsPrefix>_* environment variables set (the
FuzzSettings of the Imaging tests, the PdfFuzzSettings of the Pdf tests), then reads the .trx reports. Each failed test gets a directory under OutputDirectory/results: result.json
and failure.md, the body of the issue that eng/report-fuzz-failures.ps1 opens, with the failure messages (signature,
case, minimized input in base64) and the commands that reproduce it. The campaign gets a result too, named after the
shard: it fails when the tests could not be built or run, or when dotnet test failed without a failed test in its
reports (a test host killed by the hang dump, a crash).

The minimized inputs are written to the directory of the ArtifactsVariable environment variable (OutputDirectory/inputs
by default). Reads <SettingsPrefix>_SEED and <SettingsPrefix>_DURATION_SECONDS from the environment.

Exits with code 1 when the campaign fails.
#>
param(
    [string]$Project = 'fuzz/Meziantou.Framework.Imaging.FuzzTests',

    # Names the result of the campaign, which is split across jobs
    [string]$Shard = 'all',

    # The arguments of dotnet test that select the tests of the shard, for instance
    # '--filter-class Meziantou.Framework.Imaging.FuzzTests.MutationFuzzTests'
    [string]$Filter,

    [string]$OutputDirectory = 'artifacts/fuzz',

    # The prefix of the environment variables of the budget of the project (seed, duration, iterations, groups)
    [string]$SettingsPrefix = 'MEZIANTOU_FRAMEWORK_IMAGING_FUZZ',

    # The environment variable of the directory the tests write the minimized inputs to, or empty when they write none
    [string]$ArtifactsVariable = 'MEZIANTOU_FRAMEWORK_IMAGING_TEST_ARTIFACTS'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectName = Split-Path -Leaf $Project
# x64 or arm64: the codecs take other hardware intrinsics paths on each
$architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$resultsDirectory = Join-Path $OutputDirectory 'results'
# An absolute path: dotnet test resolves a relative project path twice
$projectPath = (Resolve-Path $Project).Path
$testResultsDirectory = [System.IO.Path]::GetFullPath((Join-Path $OutputDirectory "test-results/$Shard"))
if ($ArtifactsVariable -and -not [Environment]::GetEnvironmentVariable($ArtifactsVariable)) {
    [Environment]::SetEnvironmentVariable($ArtifactsVariable, [System.IO.Path]::GetFullPath((Join-Path $OutputDirectory 'inputs')))
}

$Seed = [Environment]::GetEnvironmentVariable("${SettingsPrefix}_SEED")
$Duration = [Environment]::GetEnvironmentVariable("${SettingsPrefix}_DURATION_SECONDS")
$Namespace = @{ t = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010' }

function Get-Fence([string]$Text) {
    # A fence longer than any backtick run of the text, so the text cannot close it
    $longest = ([regex]::Matches($Text, '`+') | ForEach-Object Length | Measure-Object -Maximum).Maximum
    return '`' * [Math]::Max(3, $longest + 1)
}

function Get-Campaign {
    $campaign = if ($Seed) { "Base seed ``$Seed``" } else { 'Default base seed' }
    if ($Duration) {
        $campaign += ", $Duration s per campaign"
    }

    $campaign += ", $architecture"

    return $campaign
}

# A test name holds quotes, spaces and parentheses
function Get-DirectoryName([string]$Name) {
    return [regex]::Replace($Name, '[^A-Za-z0-9._-]+', '-').Trim('-')
}

function Write-Result([string]$Target, [bool]$Failed, [string]$Failure, [int]$Seconds) {
    $directory = Join-Path $resultsDirectory (Get-DirectoryName $Target)
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $result = [ordered]@{
        project      = $projectName
        shard        = $Shard
        architecture = $architecture
        target       = $Target
        seconds      = $Seconds
        failed       = $Failed
    }

    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'result.json')
    if ($Failed) {
        Set-Content -LiteralPath (Join-Path $directory 'failure.md') -Value $Failure
    }
}

# The target of the campaign result, which every shard writes
$campaignTarget = "$projectName ($Shard)"
if (Test-Path $resultsDirectory) {
    # The results of a previous run of the shard; the other projects and shards keep theirs
    foreach ($file in Get-ChildItem $resultsDirectory -Filter 'result.json' -Recurse) {
        $previous = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        if ($previous.project -eq $projectName -and $previous.PSObject.Properties['shard'] -and $previous.shard -eq $Shard) {
            Remove-Item -Recurse -Force $file.DirectoryName
        }
    }
}

if (Test-Path $testResultsDirectory) {
    Remove-Item -Recurse -Force $testResultsDirectory
}

if ($Seed) {
    # The settings of the tests silently fall back to the default seed for a value they cannot parse
    $parsed = [ulong]0
    if (-not [ulong]::TryParse($Seed, [ref]$parsed)) {
        throw "The seed '$Seed' is not a 64-bit unsigned integer"
    }
}

if ($env:GITHUB_STEP_SUMMARY) {
    "$(Get-Campaign), shard ``$Shard``, commit ``$env:GITHUB_SHA``" >> $env:GITHUB_STEP_SUMMARY
}

$started = Get-Date
try {
    dotnet build $projectPath --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE"
    }

    $filterArguments = @(if ($Filter) { $Filter -split '\s+' | Where-Object { $_ } })
    dotnet test --project $projectPath --configuration Release --no-build --results-directory $testResultsDirectory @filterArguments
    $exitCode = $LASTEXITCODE
}
catch {
    $elapsed = [int]((Get-Date) - $started).TotalSeconds
    Write-Host "::error::$($_.Exception.Message)"
    Write-Result -Target $campaignTarget -Failed $true -Seconds $elapsed -Failure "The tests of $projectName could not run: $($_.Exception.Message). See the build log of the run."
    exit 1
}

$elapsed = [int]((Get-Date) - $started).TotalSeconds

# The dumps are large and the issues do not use them
Get-ChildItem -Recurse $testResultsDirectory -Filter '*.dmp' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

# Failed tests by name; a test fails once per target framework, usually with the same message
$failures = [ordered]@{}
# The merged report repeats the results of the per-framework reports
$reports = @(if (Test-Path $testResultsDirectory) { Get-ChildItem -Path $testResultsDirectory -Recurse -Filter '*.trx' | Where-Object { $_.Directory.Name -ne 'merged' } })
foreach ($file in $reports) {
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

foreach ($testName in $failures.Keys) {
    $failure = $failures[$testName]
    $frameworks = @($failure.Results.Framework | Sort-Object -Unique)
    $body = [System.Text.StringBuilder]::new()
    [void]$body.AppendLine("- $(Get-Campaign), shard ``$Shard``")
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
    $testFilter = if ($failure.ClassName -and $failure.MethodName) { " --filter-method $($failure.ClassName).$($failure.MethodName)" } else { '' }
    [void]$body.AppendLine()
    [void]$body.AppendLine('### Reproduce')
    [void]$body.AppendLine()
    [void]$body.AppendLine('```bash')
    [void]$body.AppendLine("git checkout $env:GITHUB_SHA")
    if ($Seed) {
        [void]$body.AppendLine("export ${SettingsPrefix}_SEED=$Seed")
    }

    if ($group) {
        [void]$body.AppendLine("export ${SettingsPrefix}_GROUPS=$group")
    }

    if ($iterations) {
        [void]$body.AppendLine("export ${SettingsPrefix}_ITERATIONS=$iterations")
    }
    elseif ($Duration) {
        # Without a case number, the campaign has to run as long as on CI to reach the case
        [void]$body.AppendLine("export ${SettingsPrefix}_ITERATIONS=2000000000")
        [void]$body.AppendLine("export ${SettingsPrefix}_DURATION_SECONDS=$Duration")
    }

    [void]$body.AppendLine("dotnet test --project `"`$PWD/$Project`" --configuration Release$framework$testFilter")
    [void]$body.AppendLine('```')
    [void]$body.AppendLine()
    [void]$body.AppendLine("Once fixed, commit the minimized input under ``$Project/FuzzRegressions`` (see ``regressions.json``).")
    Write-Result -Target $testName -Failed $true -Seconds $elapsed -Failure $body.ToString()
    Write-Host "::error::$testName failed"
}

# A test host killed by the hang dump leaves no failed test (and maybe no report) behind
$campaignFailed = $exitCode -ne 0 -and $failures.Count -eq 0
$campaignFailure = "dotnet test exited with code $exitCode after $elapsed s without a failed test in its $($reports.Count) report(s): a test host killed by the hang dump (a test running longer than 10 minutes), or a crash. See the log of the run.`n`n- $(Get-Campaign), shard ``$Shard``"
Write-Result -Target $campaignTarget -Failed $campaignFailed -Seconds $elapsed -Failure $campaignFailure
if ($env:GITHUB_STEP_SUMMARY) {
    $status = if ($exitCode -eq 0) { 'passed' } else { "failed ($($failures.Count) failed test(s))" }
    "- ``$campaignTarget``: $status in $elapsed s" >> $env:GITHUB_STEP_SUMMARY
}

if ($exitCode -ne 0) {
    exit 1
}
