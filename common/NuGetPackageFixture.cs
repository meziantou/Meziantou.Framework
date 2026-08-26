using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Meziantou.Framework;
using Xunit.Sdk;

namespace TestUtilities;

public abstract class NuGetPackageFixture(string packageProjectName) : IAsyncLifetime
{
    private const string PackageVersionValue = "999.0.0-local";
    private const string ConfigurationValue = "Debug";

    private TemporaryDirectory _temporaryDirectory = null!;

    public string DotnetSdkVersion { get; private set; } = string.Empty;
    public FullPath PackagePath { get; private set; }
    public FullPath PackagesDirectory { get; private set; }
    public string PackageVersion { get; } = PackageVersionValue;

    public async ValueTask InitializeAsync()
    {
        _temporaryDirectory = TemporaryDirectory.Create();
        PackagesDirectory = _temporaryDirectory.CreateDirectory("packages");
        var artifactsPath = _temporaryDirectory.CreateDirectory("artifacts");

        var repositoryRoot = GetRepositoryRoot();
        DotnetSdkVersion = ReadDotnetSdkVersion(repositoryRoot / "global.json");

        string[] commonArguments =
        [
            "--configuration",
            ConfigurationValue,
            "--disable-build-servers",
            "-nologo",
            "/p:ArtifactsPath=" + artifactsPath,
            "/p:Version=" + PackageVersionValue,
            "/p:RunAnalyzers=false",
            "/p:PublicApiGeneratorGenerateOnBuild=false",
            "/p:PublicApiGeneratorVerifyNoChangeOnBuild=false",
        ];

        // Building the main project also builds the projects it references, so they can all be packed without rebuilding.
        // The Version property applies to them too, so a dependency using that version must be packed to restore the package.
        await RunDotNetCommand(repositoryRoot, ["build", GetProjectPath(repositoryRoot, packageProjectName), .. commonArguments], expectedExitCode: 0);

        var pendingProjectNames = new Queue<string>([packageProjectName]);
        var packedProjectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pendingProjectNames.TryDequeue(out var projectName))
        {
            if (!packedProjectNames.Add(projectName))
                continue;

            var projectPath = GetProjectPath(repositoryRoot, projectName);
            if (!File.Exists(projectPath))
                throw new XunitException($"The package '{projectName}' is a dependency built by this repository, but '{projectPath}' does not exist.");

            await RunDotNetCommand(repositoryRoot, ["pack", projectPath, "--no-build", .. commonArguments, "--output", PackagesDirectory], expectedExitCode: 0);

            foreach (var dependency in GetDependenciesBuiltByThisRepository(PackagesDirectory / $"{projectName}.{PackageVersionValue}.nupkg"))
            {
                pendingProjectNames.Enqueue(dependency);
            }
        }

        PackagePath = PackagesDirectory / $"{packageProjectName}.{PackageVersionValue}.nupkg";
        if (!File.Exists(PackagePath))
            throw new XunitException($"Expected package was not created: {PackagePath}");
    }

    public async ValueTask DisposeAsync()
    {
        await _temporaryDirectory.DisposeAsync();
    }

    private static string[] GetDependenciesBuiltByThisRepository(FullPath packagePath)
    {
        using var package = ZipFile.OpenRead(packagePath);
        var nuspecEntry = package.Entries.FirstOrDefault(entry => !entry.FullName.Contains('/', StringComparison.Ordinal) && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        if (nuspecEntry is null)
            throw new XunitException($"The package '{packagePath}' does not contain a nuspec file.");

        using var stream = nuspecEntry.Open();
        var nuspec = XDocument.Load(stream);

        // Projects referenced by the packed project use the same version, so they are not available on NuGet.org
        return nuspec.Descendants()
            .Where(element => element.Name.LocalName is "dependency")
            .Where(element => string.Equals((string?)element.Attribute("version"), PackageVersionValue, StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("id")?.Value)
            .OfType<string>()
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static FullPath GetProjectPath(FullPath repositoryRoot, string projectName)
    {
        return repositoryRoot / "src" / projectName / (projectName + ".csproj");
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
            .ExecuteBufferedAsync(Xunit.TestContext.Current.CancellationToken);

        if (result.ExitCode != expectedExitCode)
        {
            var output = string.Join('\n', result.Output);
            throw new XunitException($"Command failed: dotnet {string.Join(' ', arguments)}\nExpected exit code: {expectedExitCode}\nActual exit code: {result.ExitCode}\nOutput:\n{output}");
        }
    }

    private static FullPath GetRepositoryRoot([CallerFilePath] string? filePath = null)
    {
        var candidates = new List<FullPath>();
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var sourceFilePath = FullPath.FromPath(filePath);
            if (File.Exists(sourceFilePath))
            {
                candidates.Add(sourceFilePath);
            }
        }

        candidates.Add(FullPath.CurrentDirectory());
        AddPathFromEnvironmentVariable(candidates, "GITHUB_WORKSPACE");
        AddPathFromEnvironmentVariable(candidates, "BUILD_SOURCESDIRECTORY");

        foreach (var candidate in candidates)
        {
            if (candidate.TryFindGitRepositoryRoot(out var repositoryRoot))
                return repositoryRoot;
        }

        throw new InvalidOperationException($"Cannot find git repository root. CallerFilePath='{filePath}', CurrentDirectory='{FullPath.CurrentDirectory()}'");
    }

    private static void AddPathFromEnvironmentVariable(List<FullPath> candidates, string variableName)
    {
        var path = Environment.GetEnvironmentVariable(variableName);
        if (!string.IsNullOrWhiteSpace(path))
        {
            candidates.Add(FullPath.FromPath(path));
        }
    }
}
