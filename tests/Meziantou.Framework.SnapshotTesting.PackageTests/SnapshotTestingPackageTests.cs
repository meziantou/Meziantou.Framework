using System.IO.Compression;
using Xunit.Sdk;

namespace Meziantou.Framework.SnapshotTesting.PackageTests;

public sealed class SnapshotTestingPackageTests(SnapshotTestingPackageFixture fixture) : IClassFixture<SnapshotTestingPackageFixture>
{
    private const string DuplicateTypeSource = "namespace Consumer; internal sealed class Duplicate { }";

    [Fact]
    public void Package_ContainsTheBuildFiles()
    {
        using var package = ZipFile.OpenRead(fixture.PackagePath);

        // NuGet uses 'buildTransitive' for PackageReference, so everything must be reachable from there
        AssertEntry(package, "buildTransitive/Meziantou.Framework.SnapshotTesting.targets");
        AssertEntry(package, "buildTransitive/Meziantou.Framework.SnapshotFiles.targets");
        AssertEntry(package, "buildTransitive/Meziantou.Framework.SourceRootRegistration.targets");
        AssertEntry(package, "build/Meziantou.Framework.SnapshotTesting.targets");
        AssertEntry(package, "buildMultiTargeting/Meziantou.Framework.SnapshotTesting.targets");
    }

    [Fact]
    public async Task Package_DoesNotCompileSnapshotSourceFiles()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var projectDirectory = CreateConsumer(temporaryDirectory, "consumer", additionalProperties: "");

        await RunDotNetCommand(projectDirectory, ["restore", "--disable-build-servers"], expectedExitCode: 0);
        await RunDotNetCommand(projectDirectory, ["build", "--no-restore", "--disable-build-servers", "-nologo"], expectedExitCode: 0);
    }

    [Fact]
    public async Task Package_CompilesSnapshotSourceFiles_WhenOptedOut()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var projectDirectory = CreateConsumer(temporaryDirectory, "consumer-optout", additionalProperties: "<SnapshotTestingExcludeSnapshotFilesFromCompilation>false</SnapshotTestingExcludeSnapshotFilesFromCompilation>");

        await RunDotNetCommand(projectDirectory, ["restore", "--disable-build-servers"], expectedExitCode: 0);
        var result = await RunDotNetCommand(projectDirectory, ["build", "--no-restore", "--disable-build-servers", "-nologo"], expectedExitCode: 1);

        var output = string.Join('\n', result.Output);
        Assert.Contains("CS0101", output);
    }

    [Fact]
    public async Task Package_GeneratesTheSourceRootFile()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var projectDirectory = CreateConsumer(temporaryDirectory, "consumer-sourceroot", additionalProperties: "<DeterministicSourcePaths>true</DeterministicSourcePaths>");

        await RunDotNetCommand(projectDirectory, ["restore", "--disable-build-servers"], expectedExitCode: 0);
        await RunDotNetCommand(projectDirectory, ["build", "--no-restore", "--disable-build-servers", "-nologo"], expectedExitCode: 0);

        AssertSourceRootFileWasGenerated(projectDirectory);
    }

    [Fact]
    public async Task Package_AppliesToProjectsReferencingThePackageTransitively()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var rootDirectory = temporaryDirectory.CreateDirectory("consumer-transitive");
        CreateGlobalJson(rootDirectory, fixture.DotnetSdkVersion);
        CreateNuGetConfig(rootDirectory, fixture.PackagesDirectory);

        temporaryDirectory.CreateTextFile("consumer-transitive/Library/Library.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="{{SnapshotTestingPackageFixture.PackageName}}" Version="{{fixture.PackageVersion}}" />
              </ItemGroup>
            </Project>
            """);
        temporaryDirectory.CreateTextFile("consumer-transitive/Library/Library.cs", "namespace Library; internal sealed class Sample { }");

        temporaryDirectory.CreateTextFile("consumer-transitive/Consumer/Consumer.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <DeterministicSourcePaths>true</DeterministicSourcePaths>
              </PropertyGroup>

              <ItemGroup>
                <ProjectReference Include="../Library/Library.csproj" />
              </ItemGroup>
            </Project>
            """);
        temporaryDirectory.CreateTextFile("consumer-transitive/Consumer/Consumer.cs", DuplicateTypeSource);
        temporaryDirectory.CreateTextFile("consumer-transitive/Consumer/Nested/__snapshots__/Consumer_Snapshot.verified.cs", DuplicateTypeSource);

        var projectDirectory = rootDirectory / "Consumer";
        await RunDotNetCommand(projectDirectory, ["restore", "--disable-build-servers"], expectedExitCode: 0);
        await RunDotNetCommand(projectDirectory, ["build", "--no-restore", "--disable-build-servers", "-nologo"], expectedExitCode: 0);

        AssertSourceRootFileWasGenerated(projectDirectory);
    }

    private FullPath CreateConsumer(TemporaryDirectory temporaryDirectory, string directoryName, string additionalProperties)
    {
        var projectDirectory = temporaryDirectory.CreateDirectory(directoryName);
        CreateGlobalJson(projectDirectory, fixture.DotnetSdkVersion);
        CreateNuGetConfig(projectDirectory, fixture.PackagesDirectory);

        temporaryDirectory.CreateTextFile($"{directoryName}/Sample.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                {{additionalProperties}}
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="{{SnapshotTestingPackageFixture.PackageName}}" Version="{{fixture.PackageVersion}}" />
              </ItemGroup>
            </Project>
            """);

        // Both files declare the same type, so the build only succeeds when the snapshot is not compiled
        temporaryDirectory.CreateTextFile($"{directoryName}/Consumer.cs", DuplicateTypeSource);
        temporaryDirectory.CreateTextFile($"{directoryName}/Nested/__snapshots__/Sample_Snapshot.verified.cs", DuplicateTypeSource);

        return projectDirectory;
    }

    private static void AssertSourceRootFileWasGenerated(FullPath projectDirectory)
    {
        var intermediateDirectory = projectDirectory / "obj";
        var files = Directory.Exists(intermediateDirectory)
            ? Directory.GetFiles(intermediateDirectory, "SnapshotTestingSourceRoot.g.cs", SearchOption.AllDirectories)
            : [];

        Assert.NotEmpty(files, $"No source root file was generated in '{intermediateDirectory}'.");
    }

    private static ZipArchiveEntry AssertEntry(ZipArchive package, string entryName)
    {
        var entry = package.GetEntry(entryName);
        if (entry is null)
            throw new XunitException($"The package does not contain '{entryName}'. Entries:{Environment.NewLine}{string.Join(Environment.NewLine, package.Entries.Select(item => item.FullName))}");

        return entry;
    }

    private static void CreateGlobalJson(FullPath projectDirectory, string dotnetSdkVersion)
    {
        File.WriteAllText(projectDirectory / "global.json", $$"""
            {
              "sdk": {
                "version": "{{dotnetSdkVersion}}",
                "rollForward": "latestFeature"
              }
            }
            """);
    }

    private static void CreateNuGetConfig(FullPath projectDirectory, FullPath packagesDirectory)
    {
        var packagesCachePath = packagesDirectory / "global-packages";
        File.WriteAllText(projectDirectory / "NuGet.config", $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="{{packagesCachePath}}" />
              </config>
              <packageSources>
                <clear />
                <add key="local" value="{{packagesDirectory}}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="local">
                  <package pattern="Meziantou.Framework.*" />
                </packageSource>
                <packageSource key="nuget.org">
                  <package pattern="*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);
    }

    private static async Task<BufferedProcessResult> RunDotNetCommand(FullPath workingDirectory, IReadOnlyList<string> arguments, int expectedExitCode)
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
            throw new XunitException($"Command failed: dotnet {string.Join(' ', arguments)}{Environment.NewLine}Expected exit code: {expectedExitCode}{Environment.NewLine}Actual exit code: {result.ExitCode}{Environment.NewLine}Output:{Environment.NewLine}{output}");
        }

        return result;
    }
}
