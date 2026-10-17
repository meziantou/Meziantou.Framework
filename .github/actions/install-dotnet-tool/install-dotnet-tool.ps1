param(
    [Parameter(Mandatory = $true)]
    [string]$PackageId
)

# nuget.org intermittently answers with a transient error (e.g. 503 The server is busy), which fails the
# whole job. "update" installs the tool when it is missing, and succeeds when a previous step of the same
# job already installed it.
$maxAttempts = 5
$baseDelaySeconds = 5
$maxDelaySeconds = 30

for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
    Write-Host "Installing $PackageId (attempt $attempt/$maxAttempts)"
    dotnet tool update --global $PackageId
    $exitCode = $LASTEXITCODE

    if ($exitCode -eq 0) {
        exit 0
    }

    if ($attempt -lt $maxAttempts) {
        $delaySeconds = [math]::Min([int]($baseDelaySeconds * [math]::Pow(2, $attempt - 1)), $maxDelaySeconds)
        Write-Host "Retrying installation of $PackageId in $delaySeconds second(s)."
        Start-Sleep -Seconds $delaySeconds
    }
}

throw "dotnet tool update --global $PackageId failed with exit code $exitCode after $maxAttempts attempt(s)"
