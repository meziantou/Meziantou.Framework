using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit.Sdk;

namespace Meziantou.Framework.PublicApiGenerator.MSBuild.Tests;

public sealed class PublicApiGeneratorMsBuildPackageFixture : IAsyncLifetime
{
    private const string PackageVersionValue = "999.0.0-local";
    private const string ConfigurationValue = "Debug";

    private TemporaryDirectory _temporaryDirectory = null!;

    public string DotnetSdkVersion { get; private set; } = string.Empty;
    public FullPath PackagesDirectory { get; private set; }
    public string PackageVersion { get; } = PackageVersionValue;

    public async ValueTask InitializeAsync()
    {
        _temporaryDirectory = TemporaryDirectory.Create();
        PackagesDirectory = _temporaryDirectory.CreateDirectory("packages");

        var repositoryRoot = GetRepositoryRoot();
        DotnetSdkVersion = ReadDotnetSdkVersion(repositoryRoot / "global.json");
        var packageProjectPath = repositoryRoot / "src" / "Meziantou.Framework.PublicApiGenerator.MSBuild" / "Meziantou.Framework.PublicApiGenerator.MSBuild.csproj";

        await RunDotNetCommand(repositoryRoot, ["build", packageProjectPath, "--configuration", ConfigurationValue, "--disable-build-servers", "-nologo", "/p:Version=" + PackageVersionValue], expectedExitCode: 0);
        await RunDotNetCommand(repositoryRoot, ["pack", packageProjectPath, "--configuration", ConfigurationValue, "--no-build", "--disable-build-servers", "-nologo", "--output", PackagesDirectory, "/p:Version=" + PackageVersionValue], expectedExitCode: 0);
    }

    public async ValueTask DisposeAsync()
    {
        await _temporaryDirectory.DisposeAsync();
    }

    private static string ReadDotnetSdkVersion(FullPath globalJsonPath)
    {
        using var globalJson = JsonDocument.Parse(File.ReadAllText(globalJsonPath));
        var dotnetSdkVersion = globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString();
        if (!string.IsNullOrEmpty(dotnetSdkVersion))
            return dotnetSdkVersion;

        throw new InvalidOperationException("Cannot read sdk.version from global.json.");
    }

    private static async Task RunDotNetCommand(FullPath workingDirectory, IReadOnlyList<string> arguments, int expectedExitCode)
    {
        var result = await ProcessWrapper.Create("dotnet")
            .WithArguments(arguments)
            .WithWorkingDirectory(workingDirectory)
            .WithEnvironmentVariables(env => env.Set("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1"))
            .WithValidation(ProcessValidationMode.None)
            .ExecuteBufferedAsync(XunitCancellationToken);

        if (result.ExitCode != expectedExitCode)
        {
            var output = string.Join('\n', result.Output);
            throw new XunitException($"Command failed: dotnet {string.Join(' ', arguments)}\nExpected exit code: {expectedExitCode}\nActual exit code: {result.ExitCode}\nOutput:\n{output}");
        }
    }

    private static FullPath GetRepositoryRoot([CallerFilePath] string? filePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var sourceFilePath = FullPath.FromPath(filePath);
        if (sourceFilePath.TryFindGitRepositoryRoot(out var repositoryRoot))
            return repositoryRoot;

        throw new InvalidOperationException("Cannot find git repository root.");
    }
}
