<#
.SYNOPSIS
Restores the corpus that the SharpFuzz campaigns reached (.github/workflows/fuzz-sharpfuzz.yml).

.DESCRIPTION
Every run of the fuzz-sharpfuzz workflow uploads the corpus of every project as the artifact fuzz-corpus, laid out as
<project>/<architecture>/<target>: the corpus of the projects it fuzzed, and the corpus of the other projects, carried
over from the previous run. This script downloads the most recent one, from the runs of the current branch first, then
from the runs of the default branch. The artifacts of the runs of forks are ignored.

With -Project, it extracts the corpus of the project into Path (Path/<target>): the corpus of the architecture, else
the corpus of another architecture (the inputs do not depend on it). Without -Project, it extracts the whole corpus into
Path. Without any corpus, Path stays empty: the campaign starts from the seeds.

Fails when the GitHub API fails, so that the workflow does not save a corpus that starts from the seeds only, which the
next campaign would restore. Requires the GitHub CLI, with a token that can read the actions of the repository
(GH_TOKEN).
#>
param(
    [Parameter(Mandatory)]
    [string]$Path,

    # The fuzz project, for instance Meziantou.Framework.Yaml.Fuzz
    [string]$Project,

    # The architecture of the campaign, for instance x64
    [string]$Architecture,

    [string]$Repository = $env:GITHUB_REPOSITORY,

    # The branch whose runs are searched before the runs of the default branch
    [string]$Branch = $env:GITHUB_REF_NAME
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $Repository) {
    throw 'Set -Repository (owner/name) or GITHUB_REPOSITORY'
}

if ($Project -and -not $Architecture) {
    throw '-Project requires -Architecture'
}

if ((Test-Path $Path) -and (Get-ChildItem $Path -Force | Select-Object -First 1)) {
    throw "$Path is not empty"
}

function Invoke-GitHubApi([string]$Endpoint, [string]$Query) {
    $output = @(gh api --paginate $Endpoint --jq $Query)
    if ($LASTEXITCODE -ne 0) {
        throw "gh api $Endpoint failed with exit code $LASTEXITCODE"
    }

    return $output | ForEach-Object { $_ | ConvertFrom-Json }
}

function Write-Summary([string]$Message) {
    Write-Host $Message
    if ($env:GITHUB_STEP_SUMMARY) {
        $Message >> $env:GITHUB_STEP_SUMMARY
    }
}

New-Item -ItemType Directory -Path $Path -Force | Out-Null

$repositoryInfo = Invoke-GitHubApi "repos/$Repository" '{ id, default_branch }'
$branches = @($Branch, $repositoryInfo.default_branch | Where-Object { $_ } | Select-Object -Unique)
$artifacts = @(Invoke-GitHubApi "repos/$Repository/actions/artifacts?name=fuzz-corpus&per_page=100" '.artifacts[]' |
    Where-Object { -not $_.expired -and $_.workflow_run.head_repository_id -eq $repositoryInfo.id })

$artifact = $null
foreach ($candidateBranch in $branches) {
    $artifact = $artifacts | Where-Object { $_.workflow_run.head_branch -ceq $candidateBranch } | Sort-Object -Property { [datetimeoffset]$_.created_at } -Descending | Select-Object -First 1
    if ($artifact) {
        break
    }
}

if (-not $artifact) {
    Write-Summary "No fuzz-corpus artifact in the runs of $($branches -join ', '): the campaign starts from the seeds"
    exit 0
}

$source = "the artifact fuzz-corpus of https://github.com/$Repository/actions/runs/$($artifact.workflow_run.id) (branch $($artifact.workflow_run.head_branch), $(([datetimeoffset]$artifact.created_at).ToString('u', [cultureinfo]::InvariantCulture)))"
$download = if ($Project) { Join-Path ([System.IO.Path]::GetTempPath()) "fuzz-corpus-$([guid]::NewGuid())" } else { $Path }
gh run download $artifact.workflow_run.id --repo $Repository --name $artifact.name --dir $download
if ($LASTEXITCODE -ne 0) {
    throw "gh run download failed with exit code $LASTEXITCODE"
}

if (-not $Project) {
    Write-Summary "Restored the corpus of every project ($(@(Get-ChildItem $Path -File -Recurse).Count) inputs) from $source"
    exit 0
}

# The corpus of the architecture, else the corpus of another one
$candidates = @(Get-ChildItem (Join-Path $download $Project) -Directory -ErrorAction Ignore | Sort-Object -Property Name)
$corpus = @($candidates | Where-Object { $_.Name -ceq $Architecture }) + $candidates | Select-Object -First 1
if (-not $corpus) {
    Remove-Item -Recurse -Force $download
    Write-Summary "No corpus of $Project in $source`: the campaign starts from the seeds"
    exit 0
}

Get-ChildItem $corpus.FullName | Move-Item -Destination $Path
Remove-Item -Recurse -Force $download
Write-Summary "Restored the $($corpus.Name) corpus of $Project ($(@(Get-ChildItem $Path -File -Recurse).Count) inputs) from $source"
