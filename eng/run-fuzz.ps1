<#
.SYNOPSIS
Runs a libFuzzer campaign on the targets of a SharpFuzz fuzz project (fuzz/*.Fuzz).

.DESCRIPTION
Publishes the project, writes the seeds and the dictionary of each target, instruments the Meziantou.Framework
assemblies of the publish output with sharpfuzz, then runs libfuzzer-dotnet on each target for a fixed time. Linux
only (libfuzzer-dotnet uses System V shared memory).

Every target gets a directory under OutputDirectory/results/<target>: the libFuzzer log, the failing input libFuzzer
wrote (crash-*, timeout-*, oom-*), result.json, and failure.md, the body of the issue that
eng/report-fuzz-crashes.ps1 opens. The corpus each target reaches is kept under CorpusDirectory/<target>, minimized,
so that the next campaign starts from it (the weekly workflow caches it).

Exits with code 1 when a target fails.
#>
param(
    # The fuzz project, for instance fuzz/Meziantou.Framework.Yaml.Fuzz
    [Parameter(Mandatory)]
    [string]$Project,

    # The targets to fuzz (default: every target of the project)
    [string[]]$Targets,

    # Time budget of each target
    [int]$DurationSeconds = 300,

    # An input running longer than this is reported as a hang
    [int]$TimeoutSeconds = 30,

    [string]$Framework = 'net10.0',

    [string]$OutputDirectory = 'artifacts/fuzz',

    [string]$CorpusDirectory = 'artifacts/fuzz-corpus',

    [string]$LibFuzzer = 'libfuzzer-dotnet',

    [string]$SharpFuzz = 'sharpfuzz',

    # Build output directory, when it must differ from the artifacts directory of the repository (a build in a
    # container that shares the source tree with the host)
    [string]$ArtifactsPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# libFuzzer and the .NET process write to stderr, which PowerShell would color in the logs
$PSStyle.OutputRendering = 'PlainText'

$projectName = Split-Path -Leaf $Project
# x64 or arm64: the libraries take other hardware intrinsics paths on each
$architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$publishDirectory = Join-Path $OutputDirectory "publish/$projectName"
$resultsDirectory = Join-Path $OutputDirectory 'results'
$executable = Join-Path $publishDirectory $projectName

if (Test-Path $publishDirectory) {
    Remove-Item -Recurse -Force $publishDirectory
}

$publishArguments = @('--configuration', 'Release', '--framework', $Framework, '--output', $publishDirectory)
if ($ArtifactsPath) {
    $publishArguments += "/p:ArtifactsPath=$ArtifactsPath"
}

dotnet publish $Project @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$executable = (Resolve-Path $executable).Path
$allTargets = @(& $executable --list)
if ($LASTEXITCODE -ne 0) {
    throw "$projectName --list failed with exit code $LASTEXITCODE"
}

# 'a,b' arrives as a single value through pwsh -File
$Targets = @($Targets | ForEach-Object { $_ -split ',' } | ForEach-Object Trim | Where-Object { $_ })
if ($Targets.Count -eq 0) {
    $Targets = $allTargets
}

foreach ($target in $Targets | Where-Object { $_ -cnotin $allTargets }) {
    throw "$projectName has no target '$target' (targets: $($allTargets -join ', '))"
}

# The seeds and the dictionaries are written before the instrumentation: the instrumented code needs the memory that
# libfuzzer-dotnet shares with the process
foreach ($target in $Targets) {
    $targetDirectory = Join-Path $resultsDirectory $target
    if (Test-Path $targetDirectory) {
        Remove-Item -Recurse -Force $targetDirectory
    }

    New-Item -ItemType Directory -Path $targetDirectory | Out-Null
    & $executable $target --seeds (Join-Path $targetDirectory 'seeds') | Out-Null
    & $executable $target --dictionary (Join-Path $targetDirectory "$target.dict") | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "$projectName could not write the seeds or the dictionary of '$target'"
    }
}

# The libraries under test and their Meziantou.Framework dependencies; the fuzz host and SharpFuzz stay as they are
$instrumented = Get-ChildItem $publishDirectory -Filter 'Meziantou.Framework.*.dll' | Where-Object { $_.BaseName -notin @($projectName, 'Meziantou.Framework.Fuzzing') }
foreach ($assembly in $instrumented) {
    Write-Host "Instrumenting $($assembly.Name)"
    & $SharpFuzz $assembly.FullName
    if ($LASTEXITCODE -ne 0) {
        throw "sharpfuzz failed on $($assembly.Name) with exit code $LASTEXITCODE"
    }
}

# An allocation bomb fails with an OutOfMemoryException instead of exhausting the machine (libFuzzer only measures its
# own memory, not the memory of the .NET process)
$env:DOTNET_GCHeapHardLimit = '0x80000000'

$failed = $false
foreach ($target in $Targets) {
    $targetDirectory = (Resolve-Path (Join-Path $resultsDirectory $target)).Path
    $corpus = Join-Path $CorpusDirectory $target
    New-Item -ItemType Directory -Path $corpus -Force | Out-Null
    $corpus = (Resolve-Path $corpus).Path
    $log = Join-Path $targetDirectory 'libfuzzer.log'
    $dictionary = Join-Path $targetDirectory "$target.dict"
    $arguments = @(
        "--target_path=$executable"
        "--target_arg=$target"
        "-max_total_time=$DurationSeconds"
        "-timeout=$TimeoutSeconds"
        "-artifact_prefix=$targetDirectory/"
        '-print_final_stats=1'
    )

    if ((Get-Item $dictionary).Length -gt 0) {
        $arguments += "-dict=$dictionary"
    }

    Write-Host "::group::Fuzzing $target for $DurationSeconds s"
    $started = Get-Date
    # libFuzzer writes its progress to stderr
    & $LibFuzzer @arguments $corpus (Join-Path $targetDirectory 'seeds') 2>&1 | Tee-Object -FilePath $log | Out-Host
    $exitCode = $LASTEXITCODE
    $elapsed = [int]((Get-Date) - $started).TotalSeconds
    Write-Host '::endgroup::'

    $inputs = @(Get-ChildItem $targetDirectory -File | Where-Object { $_.Name -match '^(crash|timeout|oom|leak|slow-unit)-' })
    $result = [ordered]@{
        project      = $projectName
        target       = $target
        architecture = $architecture
        exitCode     = $exitCode
        seconds      = $elapsed
        failed       = $exitCode -ne 0 -or $inputs.Count -gt 0
        inputs       = @($inputs | ForEach-Object Name)
    }

    if ($result.failed) {
        $failed = $true
        Write-Host "::error::$target failed (libFuzzer exit code $exitCode)"
        # The .NET exception that SharpFuzz printed, or the libFuzzer report of a timeout or a crash of the process,
        # without the progress lines
        $lines = @(Get-Content $log | Where-Object { $_ -notmatch '^#\d+\s+(NEW|REDUCE|pulse|INITED|RELOAD)\b' })
        $tail = ($lines | Select-Object -Last 150) -join "`n"
        $body = [System.Text.StringBuilder]::new()
        [void]$body.AppendLine("libFuzzer exit code: $exitCode, after $elapsed s on $architecture")
        [void]$body.AppendLine()
        [void]$body.AppendLine('~~~~text')
        [void]$body.AppendLine($tail)
        [void]$body.AppendLine('~~~~')
        foreach ($file in $inputs) {
            [void]$body.AppendLine()
            [void]$body.AppendLine("Input ``$($file.Name)`` ($($file.Length) bytes):")
            [void]$body.AppendLine()
            if ($file.Length -le 16384) {
                [void]$body.AppendLine('```text')
                [void]$body.AppendLine([Convert]::ToBase64String([System.IO.File]::ReadAllBytes($file.FullName)))
                [void]$body.AppendLine('```')
            }
            else {
                [void]$body.AppendLine('Too large to include, see the artifacts of the run.')
            }
        }

        [void]$body.AppendLine()
        [void]$body.AppendLine('### Reproduce')
        [void]$body.AppendLine()
        $first = if ($inputs.Count -gt 0) { $inputs[0] } else { $null }
        if ($first -and $first.Length -le 16384) {
            [void]$body.AppendLine("Run the target on the input, on any OS (found on $architecture, a failure of a hardware intrinsics path only reproduces there):")
            [void]$body.AppendLine()
            [void]$body.AppendLine('```bash')
            [void]$body.AppendLine("echo '$([Convert]::ToBase64String([System.IO.File]::ReadAllBytes($first.FullName)))' | base64 --decode > $($first.Name)")
        }
        else {
            [void]$body.AppendLine("Download the input from the artifacts of the run, then run the target on it, on any OS (found on $architecture, a failure of a hardware intrinsics path only reproduces there):")
            [void]$body.AppendLine()
            [void]$body.AppendLine('```bash')
        }

        $first = if ($first) { $first.Name } else { '<input file>' }
        [void]$body.AppendLine("dotnet run --project $Project --framework $Framework -- $target $first")
        [void]$body.AppendLine('```')
        [void]$body.AppendLine()
        [void]$body.AppendLine("Once fixed, commit the input under ``$Project/Regressions/$target`` so that the replay tests (fuzz/Meziantou.Framework.Fuzzing.Tests) keep checking it.")
        Set-Content -LiteralPath (Join-Path $targetDirectory 'failure.md') -Value $body.ToString()
    }
    else {
        # Keep only the inputs that add coverage, so that the cached corpus does not grow without bound
        $merged = Join-Path $targetDirectory 'merged-corpus'
        New-Item -ItemType Directory -Path $merged | Out-Null
        & $LibFuzzer "--target_path=$executable" "--target_arg=$target" "-timeout=$TimeoutSeconds" '-merge=1' $merged $corpus 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Remove-Item -Recurse -Force $corpus
            Move-Item $merged $corpus
        }
        else {
            Remove-Item -Recurse -Force $merged
        }
    }

    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $targetDirectory 'result.json')
    if ($env:GITHUB_STEP_SUMMARY) {
        $status = if ($result.failed) { 'failed' } else { 'passed' }
        "- ``$target``: $status in $elapsed s, $((Get-ChildItem $corpus -File).Count) corpus inputs" >> $env:GITHUB_STEP_SUMMARY
    }
}

if ($failed) {
    exit 1
}
