param(
    [Parameter(Mandatory = $true)]
    [string]$Name,

    [int]$TimeoutSeconds = 180,

    [int]$SettleSeconds = 10
)

$ErrorActionPreference = 'Stop'

$encodedName = [Uri]::EscapeDataString($Name)
$endpoint = "repos/$($env:GITHUB_REPOSITORY)/actions/runs/$($env:GITHUB_RUN_ID)/artifacts?name=$encodedName"
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$delaySeconds = 2

while ($true) {
    # A failed request (network error, 5xx) is retried like an artifact that is not listed yet
    $count = 0
    $output = gh api $endpoint --jq '.total_count' 2>&1
    if ($LASTEXITCODE -eq 0) {
        $count = [int]$output
    }
    else {
        Write-Host "Listing the artifacts failed: $output"
    }

    if ($count -gt 0) {
        Write-Host "Artifact '$Name' is listed after $([int]$stopwatch.Elapsed.TotalSeconds) seconds"
        break
    }

    if ($stopwatch.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
        # The next step reports the actual error if the artifact is still missing
        Write-Host "::warning::Artifact '$Name' is not listed after $TimeoutSeconds seconds"
        exit 0
    }

    Write-Host "Artifact '$Name' is not listed yet, retrying in $delaySeconds seconds"
    Start-Sleep -Seconds $delaySeconds
    $delaySeconds = [Math]::Min($delaySeconds * 2, 15)
}

if ($SettleSeconds -gt 0) {
    Start-Sleep -Seconds $SettleSeconds
}
