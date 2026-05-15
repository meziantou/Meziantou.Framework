using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit.Sdk;

namespace Meziantou.Framework.PublicApiGenerator.MSBuild.Tests;

public sealed class PublicApiGeneratorMsBuildTests
{
    [Fact]
    public async Task GenerateOnBuild_SingleTarget_GeneratesFile()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var packageVersion = await BuildPackageAsync(temporaryDirectory);
        var projectDirectory = temporaryDirectory.CreateDirectory("single");
        CreateGlobalJson(projectDirectory);
        CreateNuGetConfig(projectDirectory, temporaryDirectory.GetFullPath("packages"));

        temporaryDirectory.CreateTextFile("single/Sample.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <PublicApiGeneratorOutputPath>PublicApi/PublicApi.g.cs</PublicApiGeneratorOutputPath>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Meziantou.Framework.PublicApiGenerator.MSBuild" Version="{{packageVersion}}" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """);
        temporaryDirectory.CreateTextFile("single/Sample.cs", """
            public class Sample
            {
                public void A()
                {
                }
            }
            """);

        await RunDotNetCommand(projectDirectory, ["restore", "--disable-build-servers"], expectedExitCode: 0);
        await RunDotNetCommand(projectDirectory, ["build", "--no-restore", "--disable-build-servers", "-nologo"], expectedExitCode: 0);

        var generatedFilePath = projectDirectory / "PublicApi" / "PublicApi.g.cs";
        Assert.True(File.Exists(generatedFilePath));
        var content = await File.ReadAllTextAsync(generatedFilePath, XunitCancellationToken);
        Assert.Contains("public class Sample", content, StringComparison.Ordinal);
        Assert.Contains("public void A() { }", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateOnBuild_MultiTarget_MergesTargetOutputs()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var packageVersion = await BuildPackageAsync(temporaryDirectory);
        var projectDirectory = temporaryDirectory.CreateDirectory("multi");
        CreateGlobalJson(projectDirectory);
        CreateNuGetConfig(projectDirectory, temporaryDirectory.GetFullPath("packages"));

        temporaryDirectory.CreateTextFile("multi/Sample.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
                <PublicApiGeneratorOutputPath>PublicApi/PublicApi.g.cs</PublicApiGeneratorOutputPath>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Meziantou.Framework.PublicApiGenerator.MSBuild" Version="{{packageVersion}}" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """);
        temporaryDirectory.CreateTextFile("multi/Sample.cs", """
            public class Sample
            {
                public void A()
                {
                }

            #if NET10_0
                public void B()
                {
                }
            #endif
            }
            """);

        await RunDotNetCommand(projectDirectory, ["restore", "--disable-build-servers"], expectedExitCode: 0);
        await RunDotNetCommand(projectDirectory, ["build", "--no-restore", "--disable-build-servers", "-nologo"], expectedExitCode: 0);

        var generatedFilePath = projectDirectory / "PublicApi" / "PublicApi.g.cs";
        Assert.True(File.Exists(generatedFilePath));
        var content = await File.ReadAllTextAsync(generatedFilePath, XunitCancellationToken);
        Assert.Contains("public class Sample", content, StringComparison.Ordinal);
        Assert.Contains("public void A() { }", content, StringComparison.Ordinal);
        Assert.Contains("#if NET10_0", content, StringComparison.Ordinal);
        Assert.Contains("public void B() { }", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateOnBuild_MissingOutputPath_FailsBuild()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var packageVersion = await BuildPackageAsync(temporaryDirectory);
        var projectDirectory = temporaryDirectory.CreateDirectory("missing-output");
        CreateGlobalJson(projectDirectory);
        CreateNuGetConfig(projectDirectory, temporaryDirectory.GetFullPath("packages"));

        temporaryDirectory.CreateTextFile("missing-output/Sample.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Meziantou.Framework.PublicApiGenerator.MSBuild" Version="{{packageVersion}}" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """);
        temporaryDirectory.CreateTextFile("missing-output/Sample.cs", """
            public class Sample
            {
            }
            """);

        await RunDotNetCommand(projectDirectory, ["restore", "--disable-build-servers"], expectedExitCode: 0);
        var buildResult = await RunDotNetCommand(projectDirectory, ["build", "--no-restore", "--disable-build-servers", "-nologo"], expectedExitCode: 1);
        var buildOutput = string.Join('\n', buildResult.Output);
        Assert.Contains("PublicApiGeneratorOutputPath", buildOutput, StringComparison.Ordinal);
    }

    private static async Task<string> BuildPackageAsync(TemporaryDirectory temporaryDirectory)
    {
        const string PackageVersion = "999.0.0-local";
        const string Configuration = "Debug";
        var repositoryRoot = GetRepositoryRoot();
        var packageProjectPath = repositoryRoot / "src" / "Meziantou.Framework.PublicApiGenerator.MSBuild" / "Meziantou.Framework.PublicApiGenerator.MSBuild.csproj";
        var packagesDirectory = temporaryDirectory.CreateDirectory("packages");

        await RunDotNetCommand(repositoryRoot, ["build", packageProjectPath, "--configuration", Configuration, "--disable-build-servers", "-nologo", "/p:Version=" + PackageVersion], expectedExitCode: 0);
        await RunDotNetCommand(repositoryRoot, ["pack", packageProjectPath, "--configuration", Configuration, "--no-build", "--disable-build-servers", "-nologo", "--output", packagesDirectory, "/p:Version=" + PackageVersion], expectedExitCode: 0);
        return PackageVersion;
    }

    private static void CreateGlobalJson(FullPath projectDirectory)
    {
        var repositoryGlobalJsonPath = GetRepositoryRoot() / "global.json";
        using var globalJson = JsonDocument.Parse(File.ReadAllText(repositoryGlobalJsonPath));
        var sdkVersion = globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString();
        if (string.IsNullOrEmpty(sdkVersion))
            throw new InvalidOperationException("Cannot read sdk.version from global.json.");

        File.WriteAllText(projectDirectory / "global.json", $$"""
            {
              "sdk": {
                "version": "{{sdkVersion}}",
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
                  <package pattern="Meziantou.Framework.PublicApiGenerator.MSBuild*" />
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
            throw new XunitException($"Command failed: dotnet {string.Join(' ', arguments)}\nExpected exit code: {expectedExitCode}\nActual exit code: {result.ExitCode}\nOutput:\n{output}");
        }

        return result;
    }

    private static FullPath GetRepositoryRoot([CallerFilePath] string? filePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return FullPath.FromPath(filePath).Parent.Parent.Parent;
    }
}
