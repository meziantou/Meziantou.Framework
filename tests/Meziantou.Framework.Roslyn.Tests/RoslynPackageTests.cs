using System.IO.Compression;
using Xunit.Sdk;

namespace Meziantou.Framework.Roslyn.Tests;

public sealed class RoslynPackageTests(RoslynPackageFixture fixture) : IClassFixture<RoslynPackageFixture>
{
    [Fact]
    public void Package_ContainsSourceAndBuildTransitiveTargets()
    {
        using var package = ZipFile.OpenRead(fixture.PackagePath);

        var source = ReadEntryText(AssertEntry(package, "contentFiles/cs/any/Meziantou.Framework.Roslyn/SymbolExtensions.cs"));
        Assert.Contains("IsVisibleOutsideOfAssembly", source);
        AssertEntry(package, "buildTransitive/Meziantou.Framework.Roslyn.targets");
        Assert.DoesNotContain(package.Entries, entry => entry.FullName.StartsWith("lib/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Package_AddsSourceAndConstants()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var projectDirectory = temporaryDirectory.CreateDirectory("consumer");
        CreateGlobalJson(projectDirectory, fixture.DotnetSdkVersion);
        CreateNuGetConfig(projectDirectory, fixture.PackagesDirectory);

        temporaryDirectory.CreateTextFile("consumer/Sample.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <TreatsWarningsAsErrors>true</TreatsWarningsAsErrors>
                <NoWarn>nullable</NoWarn>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="{{RoslynPackageFixture.PackageName}}" Version="{{fixture.PackageVersion}}" PrivateAssets="all" />
                <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.6.0" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """);
        temporaryDirectory.CreateTextFile("consumer/Consumer.cs", CreateConsumerSourceWithConstantChecks(GetRoslyn56Constants()));

        await RunDotNetCommand(projectDirectory, ["restore", "--disable-build-servers"], expectedExitCode: 0);
        await RunDotNetCommand(projectDirectory, ["build", "--no-restore", "--disable-build-servers", "-nologo"], expectedExitCode: 0);

        var projectAssets = await File.ReadAllTextAsync(projectDirectory / "obj" / "project.assets.json", XunitCancellationToken);
        Assert.Contains("contentFiles/cs/any/Meziantou.Framework.Roslyn/SymbolExtensions.cs", projectAssets);
    }

    [Fact]
    public async Task Package_AddsConstantsWithCentralPackageManagement()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var projectDirectory = temporaryDirectory.CreateDirectory("consumer-cpm");
        CreateGlobalJson(projectDirectory, fixture.DotnetSdkVersion);
        CreateNuGetConfig(projectDirectory, fixture.PackagesDirectory);

        temporaryDirectory.CreateTextFile("consumer-cpm/Directory.Packages.props", $$"""
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>

              <ItemGroup>
                <PackageVersion Include="{{RoslynPackageFixture.PackageName}}" Version="{{fixture.PackageVersion}}" />
                <PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="5.6.0" />
              </ItemGroup>
            </Project>
            """);
        temporaryDirectory.CreateTextFile("consumer-cpm/Sample.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="{{RoslynPackageFixture.PackageName}}" PrivateAssets="all" />
                <PackageReference Include="Microsoft.CodeAnalysis.CSharp" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """);
        temporaryDirectory.CreateTextFile("consumer-cpm/Consumer.cs", CreateConstantChecksSource(GetRoslyn56Constants()));

        await RunDotNetCommand(projectDirectory, ["restore", "--disable-build-servers"], expectedExitCode: 0);
        await RunDotNetCommand(projectDirectory, ["build", "--no-restore", "--disable-build-servers", "-nologo"], expectedExitCode: 0);
    }

    [Fact]
    public async Task PackageTargets_AddsAllConstantsForRoslyn59()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var projectDirectory = temporaryDirectory.CreateDirectory("targets");

        using var package = ZipFile.OpenRead(fixture.PackagePath);
        ExtractEntry(AssertEntry(package, "buildTransitive/Meziantou.Framework.Roslyn.targets"), projectDirectory / "Meziantou.Framework.Roslyn.targets");

        temporaryDirectory.CreateTextFile("targets/Test.proj", """
            <Project>
              <ItemGroup>
                <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.9.0" />
              </ItemGroup>

              <Import Project="Meziantou.Framework.Roslyn.targets" />

              <Target Name="WriteConstants" DependsOnTargets="_MeziantouFrameworkRoslynDefineConstants">
                <WriteLinesToFile File="constants.txt" Lines="$(DefineConstants)" Overwrite="true" />
              </Target>
            </Project>
            """);

        await RunDotNetCommand(projectDirectory, ["msbuild", "Test.proj", "-nologo", "-t:WriteConstants"], expectedExitCode: 0);

        var constants = await File.ReadAllTextAsync(projectDirectory / "constants.txt", XunitCancellationToken);
        foreach (var expectedConstant in GetRoslyn59Constants())
        {
            Assert.Contains(expectedConstant, constants);
        }
    }

    private static ZipArchiveEntry AssertEntry(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        Assert.NotNull(entry);

        return entry;
    }

    private static string ReadEntryText(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private static void ExtractEntry(ZipArchiveEntry entry, FullPath destination)
    {
        using var stream = entry.Open();
        using var file = File.Create(destination);
        stream.CopyTo(file);
    }

    private static string CreateConsumerSourceWithConstantChecks(string[] expectedConstants)
    {
        return $$"""
            using Microsoft.CodeAnalysis;
            using Meziantou.Framework.Roslyn;

            {{CreateConstantChecks(expectedConstants)}}

            namespace Demo;

            internal static class Consumer
            {
                public static bool IsVisible(ISymbol? symbol) => symbol.IsVisibleOutsideOfAssembly();
            }
            """;
    }

    private static string CreateConstantChecksSource(string[] expectedConstants)
    {
        return $$"""
            {{CreateConstantChecks(expectedConstants)}}

            namespace Demo;

            internal static class Consumer;
            """;
    }

    private static string CreateConstantChecks(string[] expectedConstants)
    {
        return string.Join('\n', expectedConstants.Select(constant => $"#if !{constant}\n#error Expected {constant}.\n#endif"));
    }

    private static string[] GetRoslyn56Constants()
    {
        return
        [
            .. GetStableRoslynConstants("5.6.0"),
            "CSHARP9_OR_GREATER",
            "CSHARP10_OR_GREATER",
            "CSHARP11_OR_GREATER",
            "CSHARP12_OR_GREATER",
            "CSHARP13_OR_GREATER",
            "CSHARP14_OR_GREATER",
            "CSHARP15_OR_GREATER",
        ];
    }

    private static string[] GetRoslyn59Constants()
    {
        return
        [
            .. GetStableRoslynConstants("5.9.0"),
            "CSHARP9_OR_GREATER",
            "CSHARP10_OR_GREATER",
            "CSHARP11_OR_GREATER",
            "CSHARP12_OR_GREATER",
            "CSHARP13_OR_GREATER",
            "CSHARP14_OR_GREATER",
            "CSHARP15_OR_GREATER",
            "CSHARP16_OR_GREATER",
        ];
    }

    private static string[] GetStableRoslynConstants(string maximumVersion)
    {
        return StableRoslynVersions
            .TakeWhile(version => string.Compare(version, maximumVersion, StringComparison.Ordinal) <= 0)
            .Select(GetRoslynConstantName)
            .ToArray();
    }

    private static string GetRoslynConstantName(string version)
    {
        if (version.EndsWith(".0", StringComparison.Ordinal))
        {
            version = version[..^2];
        }

        return "ROSLYN_" + version.Replace('.', '_') + "_OR_GREATER";
    }

    private static readonly string[] StableRoslynVersions =
    [
        "1.0.0",
        "1.0.1",
        "1.1.0",
        "1.1.1",
        "1.2.0",
        "1.2.1",
        "1.2.2",
        "1.3.0",
        "1.3.1",
        "1.3.2",
        "2.0.0",
        "2.1.0",
        "2.2.0",
        "2.3.0",
        "2.3.1",
        "2.3.2",
        "2.4.0",
        "2.6.0",
        "2.6.1",
        "2.7.0",
        "2.8.0",
        "2.8.2",
        "2.9.0",
        "2.10.0",
        "3.0.0",
        "3.1.0",
        "3.2.0",
        "3.2.1",
        "3.3.0",
        "3.3.1",
        "3.4.0",
        "3.5.0",
        "3.6.0",
        "3.7.0",
        "3.8.0",
        "3.9.0",
        "3.10.0",
        "3.11.0",
        "4.0.0",
        "4.0.1",
        "4.1.0",
        "4.2.0",
        "4.3.0",
        "4.3.1",
        "4.4.0",
        "4.5.0",
        "4.6.0",
        "4.7.0",
        "4.8.0",
        "4.9.2",
        "4.10.0",
        "4.11.0",
        "4.12.0",
        "4.13.0",
        "4.14.0",
        "5.0.0",
        "5.3.0",
        "5.6.0",
        "5.9.0",
    ];

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
                  <package pattern="{{RoslynPackageFixture.PackageName}}*" />
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
}
