using System.Text.Json;
using Meziantou.Framework;
using NuGet.Versioning;

namespace Meziantou.Framework.DependencyScanning.Tool.Tests;

public sealed class FunctionalTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public FunctionalTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task UpdateNuGetPackage()
    {
        await using var tempDir = TemporaryDirectory.Create();

        var path = tempDir.CreateEmptyFile("test.csproj");
        await File.WriteAllTextAsync(path, """
            <Project>
                <ItemGroup>
                    <PackageReference Include="Meziantou.Framework" Version="1.0.0" />
                </ItemGroup>
            </Project>
            """, XunitCancellationToken);

        var console = new ConsoleHelper(_testOutputHelper);
        var result = await Program.MainImpl(["update", "--directory", tempDir.FullPath], console.ConfigureConsole);
        Assert.Equal(0, result);

        var deps = await ScanDependencies(tempDir);
        Assert.True(SemanticVersion.Parse(deps[0].Version!) > SemanticVersion.Parse("1.0.700"));
    }

    [Fact]
    public async Task UpdateDotNetSdk()
    {
        await using var tempDir = TemporaryDirectory.Create();

        var path = tempDir.CreateEmptyFile("global.json");
        await File.WriteAllTextAsync(path, """
            {
              "sdk": {
                "version": "8.0.404"
              }
            }
            """, XunitCancellationToken);

        var console = new ConsoleHelper(_testOutputHelper);
        var result = await Program.MainImpl(["update", "--directory", tempDir.FullPath, "--dependency-type", "DotNetSdk"], console.ConfigureConsole);
        Assert.Equal(0, result);

        var dependencies = await DependencyScanner.ScanDirectoryAsync(tempDir.FullPath, options: null, XunitCancellationToken);
        var sdk = Assert.Single(dependencies, static dep => dep.Type is DependencyType.DotNetSdk);
        Assert.True(SemanticVersion.Parse(sdk.Version!) > SemanticVersion.Parse("8.0.404"));
    }

    [Theory]
    [InlineData(DependencyType.NuGet)]
    [InlineData(DependencyType.Npm)]
    [InlineData(DependencyType.DockerImage)]
    [InlineData(DependencyType.GitHubActions)]
    public async Task UpdatersRequiringAPackageNameIgnoreNamelessDependencies(DependencyType dependencyType)
    {
        var dependency = new Dependency(name: null, "1.0.0", dependencyType, nameLocation: null, versionLocation: null);

        foreach (var updater in new PackageUpdater[] { new NuGetPackageUpdater(), new NpmPackageUpdater(), new DockerPackageUpdater(), new GitHubActionsUpdater() })
        {
            Assert.Null(await updater.GetUpdatedVersionAsync(dependency, XunitCancellationToken));
        }
    }

    [Fact]
    public async Task FilterDependencyType()
    {
        await using var tempDir = TemporaryDirectory.Create();

        await File.WriteAllTextAsync(tempDir.CreateEmptyFile("a.csproj"), """
            <Project>
                <ItemGroup>
                    <PackageReference Include="Meziantou.Framework" Version="1.0.0" />
                </ItemGroup>
            </Project>
            """, XunitCancellationToken);

        await File.WriteAllTextAsync(tempDir.CreateEmptyFile("package.json"), """
            {
            "dependencies": {
                "npm": "8.0.0"
              }
            }
            """, XunitCancellationToken);

        var console = new ConsoleHelper(_testOutputHelper);
        var result = await Program.MainImpl(["update", "--directory", tempDir.FullPath, "--dependency-type", "Npm"], console.ConfigureConsole);
        Assert.Equal(0, result);

        var deps = await ScanDependencies(tempDir);
        Assert.Equal("1.0.0", deps[0].Version);
        Assert.True(SemanticVersion.Parse(deps[1].Version!) > SemanticVersion.Parse("8.6.0"));
    }

    [Fact]
    public async Task FilterDependencyType_DockerImage()
    {
        await using var tempDir = TemporaryDirectory.Create();

        await File.WriteAllTextAsync(tempDir.CreateEmptyFile("Dockerfile"), """
            FROM nginx:1.27.1
            """, XunitCancellationToken);

        await File.WriteAllTextAsync(tempDir.CreateEmptyFile("a.csproj"), """
            <Project>
                <ItemGroup>
                    <PackageReference Include="Meziantou.Framework" Version="1.0.0" />
                </ItemGroup>
            </Project>
            """, XunitCancellationToken);

        var console = new ConsoleHelper(_testOutputHelper);
        var result = await Program.MainImpl(["update", "--directory", tempDir.FullPath, "--dependency-type", "DockerImage"], console.ConfigureConsole);
        Assert.Equal(0, result);

        var dependencies = await DependencyScanner.ScanDirectoryAsync(tempDir.FullPath, options: null, XunitCancellationToken);
        var dockerDependency = Assert.Single(dependencies, static dep => dep.Type is DependencyType.DockerImage);
        Assert.True(SemanticVersion.Parse(dockerDependency.Version!) > SemanticVersion.Parse("1.27.1"));

        var nugetDependency = Assert.Single(dependencies, static dep => dep.Type is DependencyType.NuGet);
        Assert.Equal("1.0.0", nugetDependency.Version);
    }

    [Fact]
    public async Task FilterDependencyType_GitHubActions()
    {
        await using var tempDir = TemporaryDirectory.Create();

        await File.WriteAllTextAsync(tempDir.CreateEmptyFile(".github/workflows/sample.yml"), """
            jobs:
              test:
                steps:
                  - uses: actions/checkout@v2
            """, XunitCancellationToken);

        await File.WriteAllTextAsync(tempDir.CreateEmptyFile("Dockerfile"), """
            FROM nginx:1.27.1
            """, XunitCancellationToken);

        var console = new ConsoleHelper(_testOutputHelper);
        var result = await Program.MainImpl(["update", "--directory", tempDir.FullPath, "--dependency-type", "GitHubActions"], console.ConfigureConsole);
        Assert.Equal(0, result);

        var dependencies = await DependencyScanner.ScanDirectoryAsync(tempDir.FullPath, options: null, XunitCancellationToken);
        var gitHubActionsDependency = Assert.Single(dependencies, static dep => dep.Type is DependencyType.GitHubActions);
        Assert.NotNull(gitHubActionsDependency.Version);
        Assert.True(GitHubActionsVersioningStrategy.Instance.CompareVersions(gitHubActionsDependency.Version, "v2") >= 0);

        var dockerDependency = Assert.Single(dependencies, static dep => dep.Type is DependencyType.DockerImage);
        Assert.Equal("1.27.1", dockerDependency.Version);
    }

    [Fact]
    public async Task UnreachableSourceDoesNotAbortTheRun()
    {
        await using var tempDir = TemporaryDirectory.Create();

        await File.WriteAllTextAsync(tempDir.CreateEmptyFile("nuget.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="broken" value="https://nonexistent-feed.invalid/v3/index.json" />
              </packageSources>
            </configuration>
            """, XunitCancellationToken);
        await File.WriteAllTextAsync(tempDir.CreateEmptyFile("a.csproj"), """
            <Project>
                <ItemGroup>
                    <PackageReference Include="Meziantou.Framework" Version="1.0.0" />
                    <PackageReference Include="Newtonsoft.Json" Version="10.0.0" />
                </ItemGroup>
            </Project>
            """, XunitCancellationToken);

        var console = new ConsoleHelper(_testOutputHelper);
        var result = await Program.MainImpl(["update", "--directory", tempDir.FullPath], console.ConfigureConsole);

        Assert.Equal(1, result);
        // The run continued past the first failure instead of throwing out of the command
        Assert.Contains("Meziantou.Framework", console.Error);
        Assert.Contains("Newtonsoft.Json", console.Error);
        Assert.Contains("2 failed", console.Output);
    }

    [Fact]
    public async Task ListDependenciesAsJson()
    {
        await using var tempDir = TemporaryDirectory.Create();

        await File.WriteAllTextAsync(tempDir.CreateEmptyFile("a.csproj"), """
            <Project>
                <ItemGroup>
                    <PackageReference Include="Meziantou.Framework" Version="1.0.0" />
                </ItemGroup>
            </Project>
            """, XunitCancellationToken);

        await File.WriteAllTextAsync(tempDir.CreateEmptyFile("package.json"), """
            {
            "dependencies": {
                "npm": "8.0.0"
              }
            }
            """, XunitCancellationToken);

        var console = new ConsoleHelper(_testOutputHelper);
        var result = await Program.MainImpl(["list", "--directory", tempDir.FullPath, "--dependency-type", "Npm", "--format", "json"], console.ConfigureConsole);
        Assert.Equal(0, result);

        using var json = JsonDocument.Parse(console.Output);
        var dependencies = json.RootElement;
        Assert.Equal(JsonValueKind.Array, dependencies.ValueKind);
        Assert.Single(dependencies.EnumerateArray());

        var dependency = dependencies[0];
        Assert.Equal("Npm", dependency.GetProperty("type").GetString());
        Assert.Equal("npm", dependency.GetProperty("name").GetString());
        Assert.Equal("8.0.0", dependency.GetProperty("version").GetString());
        Assert.True(dependency.GetProperty("isUpdatable").GetBoolean());
    }

    [Fact]
    public async Task ListUpgradableDependenciesAsJson()
    {
        await using var tempDir = TemporaryDirectory.Create();

        var projectPath = tempDir.CreateEmptyFile("a.csproj");
        var projectContent = """
            <Project>
                <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                </PropertyGroup>
                <ItemGroup>
                    <PackageReference Include="Meziantou.Framework" Version="1.0.0" />
                </ItemGroup>
            </Project>
            """;
        await File.WriteAllTextAsync(projectPath, projectContent, XunitCancellationToken);

        var console = new ConsoleHelper(_testOutputHelper);
        var result = await Program.MainImpl(["list", "--directory", tempDir.FullPath, "--upgradable", "--format", "json"], console.ConfigureConsole);
        Assert.Equal(0, result);

        using var json = JsonDocument.Parse(console.Output);
        var dependency = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal("NuGet", dependency.GetProperty("type").GetString());
        Assert.Equal("Meziantou.Framework", dependency.GetProperty("name").GetString());
        Assert.Equal("1.0.0", dependency.GetProperty("version").GetString());
        Assert.Equal(projectContent, await File.ReadAllTextAsync(projectPath, XunitCancellationToken));
    }

    [Theory]
    [InlineData("nuget.config")]
    [InlineData("NuGet.config")]
    [InlineData("NuGet.Config")]
    public async Task NuGetPackageSourceResolver_SupportAllNuGetConfigCasings(string fileName)
    {
        await using var tempDir = TemporaryDirectory.Create();
        var projectFile = tempDir.CreateEmptyFile("a.csproj");
        await File.WriteAllTextAsync(projectFile, "<Project />", XunitCancellationToken);
        await File.WriteAllTextAsync(tempDir.CreateEmptyFile(fileName), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="feed1" value="https://feed1/v3/index.json" />
              </packageSources>
            </configuration>
            """, XunitCancellationToken);

        var resolution = NuGetPackageSourceResolver.Resolve(FullPath.FromPath(projectFile), "Package.Id");

        Assert.Equal(["https://feed1/v3/index.json"], resolution.PackageSources);
        Assert.Equal(["https://feed1/v3/index.json"], resolution.AllConfiguredSources);
        Assert.False(resolution.HasSourceMappings);
    }

    [Fact]
    public async Task NuGetPackageSourceResolver_NoConfig_ReturnsNoSources()
    {
        await using var tempDir = TemporaryDirectory.Create();
        var projectFile = tempDir.CreateEmptyFile("a.csproj");
        await File.WriteAllTextAsync(projectFile, "<Project />", XunitCancellationToken);

        var resolution = NuGetPackageSourceResolver.Resolve(FullPath.FromPath(projectFile), "Package.Id");

        Assert.Empty(resolution.PackageSources);
        Assert.Empty(resolution.AllConfiguredSources);
        Assert.False(resolution.HasSourceMappings);
    }

    [Fact]
    public async Task NuGetPackageSourceResolver_NearestConfigOverridesParentConfig()
    {
        await using var tempDir = TemporaryDirectory.Create();
        var srcDirectory = tempDir.GetFullPath("src");
        Directory.CreateDirectory(srcDirectory);

        var projectFile = srcDirectory / "a.csproj";
        await File.WriteAllTextAsync(projectFile, "<Project />", XunitCancellationToken);
        await File.WriteAllTextAsync(tempDir.CreateEmptyFile("nuget.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="feed" value="https://root/v3/index.json" />
              </packageSources>
            </configuration>
            """, XunitCancellationToken);
        await File.WriteAllTextAsync(srcDirectory / "nuget.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="feed" value="https://child/v3/index.json" />
              </packageSources>
            </configuration>
            """, XunitCancellationToken);

        var resolution = NuGetPackageSourceResolver.Resolve(FullPath.FromPath(projectFile), "Package.Id");

        Assert.Equal(["https://child/v3/index.json"], resolution.PackageSources);
        Assert.Equal(["https://child/v3/index.json"], resolution.AllConfiguredSources);
    }

    [Fact]
    public async Task NuGetPackageSourceResolver_ClearPackageSources()
    {
        await using var tempDir = TemporaryDirectory.Create();
        var srcDirectory = tempDir.GetFullPath("src");
        Directory.CreateDirectory(srcDirectory);

        var projectFile = srcDirectory / "a.csproj";
        await File.WriteAllTextAsync(projectFile, "<Project />", XunitCancellationToken);
        await File.WriteAllTextAsync(tempDir.CreateEmptyFile("nuget.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="feed1" value="https://feed1/v3/index.json" />
                <add key="feed2" value="https://feed2/v3/index.json" />
              </packageSources>
            </configuration>
            """, XunitCancellationToken);
        await File.WriteAllTextAsync(srcDirectory / "NuGet.Config", """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="feed3" value="https://feed3/v3/index.json" />
              </packageSources>
            </configuration>
            """, XunitCancellationToken);

        var resolution = NuGetPackageSourceResolver.Resolve(FullPath.FromPath(projectFile), "Package.Id");

        Assert.Equal(["https://feed3/v3/index.json"], resolution.PackageSources);
        Assert.Equal(["https://feed3/v3/index.json"], resolution.AllConfiguredSources);
    }

    [Fact]
    public async Task NuGetPackageSourceResolver_PackageSourceMapping_SelectsMatchingSource()
    {
        await using var tempDir = TemporaryDirectory.Create();
        var projectFile = tempDir.CreateEmptyFile("a.csproj");
        await File.WriteAllTextAsync(projectFile, "<Project />", XunitCancellationToken);
        await File.WriteAllTextAsync(tempDir.CreateEmptyFile("nuget.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="nuget" value="https://api.nuget.org/v3/index.json" />
                <add key="private" value="https://private/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="nuget">
                  <package pattern="Newtonsoft.*" />
                </packageSource>
                <packageSource key="private">
                  <package pattern="Contoso.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """, XunitCancellationToken);

        var resolution = NuGetPackageSourceResolver.Resolve(FullPath.FromPath(projectFile), "Contoso.Library");

        Assert.Equal(["https://private/v3/index.json"], resolution.PackageSources);
        Assert.True(resolution.HasSourceMappings);
    }

    [Fact]
    public async Task NuGetPackageSourceResolver_PackageSourceMapping_LongestPatternWinsOverWildcard()
    {
        await using var tempDir = TemporaryDirectory.Create();
        var projectFile = tempDir.CreateEmptyFile("a.csproj");
        await File.WriteAllTextAsync(projectFile, "<Project />", XunitCancellationToken);
        await File.WriteAllTextAsync(tempDir.CreateEmptyFile("nuget.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="nuget" value="https://api.nuget.org/v3/index.json" />
                <add key="private" value="https://private/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="nuget">
                  <package pattern="*" />
                </packageSource>
                <packageSource key="private">
                  <package pattern="Contoso.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """, XunitCancellationToken);

        var contoso = NuGetPackageSourceResolver.Resolve(FullPath.FromPath(projectFile), "Contoso.Library");
        var newtonsoft = NuGetPackageSourceResolver.Resolve(FullPath.FromPath(projectFile), "Newtonsoft.Json");

        // 'Contoso.*' is more specific than '*', so the public feed must not be queried for it
        Assert.Equal(["https://private/v3/index.json"], contoso.PackageSources);
        Assert.Equal(["https://api.nuget.org/v3/index.json"], newtonsoft.PackageSources);
    }

    [Fact]
    public async Task NuGetPackageSourceResolver_PackageSourceMapping_ExactPatternWinsOverPrefix()
    {
        await using var tempDir = TemporaryDirectory.Create();
        var projectFile = tempDir.CreateEmptyFile("a.csproj");
        await File.WriteAllTextAsync(projectFile, "<Project />", XunitCancellationToken);
        await File.WriteAllTextAsync(tempDir.CreateEmptyFile("nuget.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="prefix" value="https://prefix/v3/index.json" />
                <add key="exact" value="https://exact/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="prefix">
                  <package pattern="Contoso.*" />
                </packageSource>
                <packageSource key="exact">
                  <package pattern="Contoso.Library" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """, XunitCancellationToken);

        var resolution = NuGetPackageSourceResolver.Resolve(FullPath.FromPath(projectFile), "Contoso.Library");
        var other = NuGetPackageSourceResolver.Resolve(FullPath.FromPath(projectFile), "Contoso.Other");

        Assert.Equal(["https://exact/v3/index.json"], resolution.PackageSources);
        Assert.Equal(["https://prefix/v3/index.json"], other.PackageSources);
    }

    [Fact]
    public async Task NuGetPackageSourceResolver_PackageSourceMapping_EquallySpecificPatternsKeepEverySource()
    {
        await using var tempDir = TemporaryDirectory.Create();
        var projectFile = tempDir.CreateEmptyFile("a.csproj");
        await File.WriteAllTextAsync(projectFile, "<Project />", XunitCancellationToken);
        await File.WriteAllTextAsync(tempDir.CreateEmptyFile("nuget.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="first" value="https://first/v3/index.json" />
                <add key="second" value="https://second/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="first">
                  <package pattern="Contoso.*" />
                </packageSource>
                <packageSource key="second">
                  <package pattern="Contoso.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """, XunitCancellationToken);

        var resolution = NuGetPackageSourceResolver.Resolve(FullPath.FromPath(projectFile), "Contoso.Library");

        Assert.Equal(2, resolution.PackageSources.Count);
        Assert.Contains("https://first/v3/index.json", resolution.PackageSources);
        Assert.Contains("https://second/v3/index.json", resolution.PackageSources);
    }

    [Fact]
    public async Task NuGetPackageSourceResolver_PackageSourceMapping_UnmatchedPackageReturnsNoSource()
    {
        await using var tempDir = TemporaryDirectory.Create();
        var projectFile = tempDir.CreateEmptyFile("a.csproj");
        await File.WriteAllTextAsync(projectFile, "<Project />", XunitCancellationToken);
        await File.WriteAllTextAsync(tempDir.CreateEmptyFile("NuGet.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="private" value="https://private/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="private">
                  <package pattern="Contoso.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """, XunitCancellationToken);

        var resolution = NuGetPackageSourceResolver.Resolve(FullPath.FromPath(projectFile), "Newtonsoft.Json");

        Assert.Empty(resolution.PackageSources);
        Assert.Equal(["https://private/v3/index.json"], resolution.AllConfiguredSources);
        Assert.True(resolution.HasSourceMappings);
    }

    [Fact]
    public async Task NuGetPackageSourceResolver_PackageSourceMapping_OnlyUsesDeclaredSources()
    {
        await using var tempDir = TemporaryDirectory.Create();
        var projectFile = tempDir.CreateEmptyFile("a.csproj");
        await File.WriteAllTextAsync(projectFile, "<Project />", XunitCancellationToken);
        await File.WriteAllTextAsync(tempDir.CreateEmptyFile("nuget.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="nuget" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="missing-source">
                  <package pattern="Contoso.*" />
                </packageSource>
                <packageSource key="nuget">
                  <package pattern="Newtonsoft.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """, XunitCancellationToken);

        var resolution = NuGetPackageSourceResolver.Resolve(FullPath.FromPath(projectFile), "Contoso.Library");

        Assert.Empty(resolution.PackageSources);
        Assert.True(resolution.HasSourceMappings);
    }

    [Fact]
    public async Task NuGetPackageSourceResolver_PackageSourceMapping_ClearInChildConfig()
    {
        await using var tempDir = TemporaryDirectory.Create();
        var srcDirectory = tempDir.GetFullPath("src");
        Directory.CreateDirectory(srcDirectory);

        var projectFile = srcDirectory / "a.csproj";
        await File.WriteAllTextAsync(projectFile, "<Project />", XunitCancellationToken);
        await File.WriteAllTextAsync(tempDir.CreateEmptyFile("nuget.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <add key="private" value="https://private/v3/index.json" />
                <add key="nuget" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="private">
                  <package pattern="Contoso.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """, XunitCancellationToken);
        await File.WriteAllTextAsync(srcDirectory / "NuGet.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSourceMapping>
                <clear />
                <packageSource key="nuget">
                  <package pattern="Newtonsoft.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """, XunitCancellationToken);

        var contosoResolution = NuGetPackageSourceResolver.Resolve(FullPath.FromPath(projectFile), "Contoso.Library");
        var newtonsoftResolution = NuGetPackageSourceResolver.Resolve(FullPath.FromPath(projectFile), "Newtonsoft.Json");

        Assert.Empty(contosoResolution.PackageSources);
        Assert.Equal(["https://api.nuget.org/v3/index.json"], newtonsoftResolution.PackageSources);
    }

    [Fact]
    public async Task NpmPackageSourceResolver_NoNpmrc_UsesNpmjsRegistry()
    {
        await using var tempDir = TemporaryDirectory.Create();
        var packageFile = tempDir.CreateEmptyFile("package.json");
        await File.WriteAllTextAsync(packageFile, "{}", XunitCancellationToken);

        var registry = NpmPackageSourceResolver.ResolveRegistry(FullPath.FromPath(packageFile), "lodash");

        Assert.Equal("https://registry.npmjs.org/", registry.ToString());
    }

    [Fact]
    public async Task NpmPackageSourceResolver_UsesNearestDefaultRegistry()
    {
        await using var tempDir = TemporaryDirectory.Create();
        var srcDirectory = tempDir.GetFullPath("src");
        Directory.CreateDirectory(srcDirectory);

        var packageFile = srcDirectory / "package.json";
        await File.WriteAllTextAsync(packageFile, "{}", XunitCancellationToken);
        await File.WriteAllTextAsync(tempDir.CreateEmptyFile(".npmrc"), "registry=https://root.registry/", XunitCancellationToken);
        await File.WriteAllTextAsync(srcDirectory / ".npmrc", "registry=https://child.registry/", XunitCancellationToken);

        var registry = NpmPackageSourceResolver.ResolveRegistry(FullPath.FromPath(packageFile), "lodash");

        Assert.Equal("https://child.registry/", registry.ToString());
    }

    [Fact]
    public async Task NpmPackageSourceResolver_UsesScopedRegistryWhenMatchingPackage()
    {
        await using var tempDir = TemporaryDirectory.Create();
        var packageFile = tempDir.CreateEmptyFile("package.json");
        await File.WriteAllTextAsync(packageFile, "{}", XunitCancellationToken);
        await File.WriteAllTextAsync(tempDir.CreateEmptyFile(".npmrc"), """
            registry=https://default.registry/
            @contoso:registry=https://scope.registry/
            """, XunitCancellationToken);

        var scopedRegistry = NpmPackageSourceResolver.ResolveRegistry(FullPath.FromPath(packageFile), "@contoso/pkg");
        var defaultRegistry = NpmPackageSourceResolver.ResolveRegistry(FullPath.FromPath(packageFile), "left-pad");

        Assert.Equal("https://scope.registry/", scopedRegistry.ToString());
        Assert.Equal("https://default.registry/", defaultRegistry.ToString());
    }

    [Fact]
    public async Task NpmPackageSourceResolver_ScopeWithoutAtPrefixIsSupported()
    {
        await using var tempDir = TemporaryDirectory.Create();
        var packageFile = tempDir.CreateEmptyFile("package.json");
        await File.WriteAllTextAsync(packageFile, "{}", XunitCancellationToken);
        await File.WriteAllTextAsync(tempDir.CreateEmptyFile(".npmrc"), "contoso:registry=https://scope.registry/", XunitCancellationToken);

        var scopedRegistry = NpmPackageSourceResolver.ResolveRegistry(FullPath.FromPath(packageFile), "@contoso/pkg");

        Assert.Equal("https://scope.registry/", scopedRegistry.ToString());
    }

    [Fact]
    public async Task NpmPackageSourceResolver_ParsesQuotedAndCommentedEntries()
    {
        await using var tempDir = TemporaryDirectory.Create();
        var packageFile = tempDir.CreateEmptyFile("package.json");
        await File.WriteAllTextAsync(packageFile, "{}", XunitCancellationToken);
        await File.WriteAllTextAsync(tempDir.CreateEmptyFile(".npmrc"), """
            # this is a comment
            ; another comment
            registry="https://quoted.registry"
            @contoso:registry='https://scoped.registry'
            """, XunitCancellationToken);

        var defaultRegistry = NpmPackageSourceResolver.ResolveRegistry(FullPath.FromPath(packageFile), "left-pad");
        var scopedRegistry = NpmPackageSourceResolver.ResolveRegistry(FullPath.FromPath(packageFile), "@contoso/pkg");

        Assert.Equal("https://quoted.registry/", defaultRegistry.ToString());
        Assert.Equal("https://scoped.registry/", scopedRegistry.ToString());
    }

    [Fact]
    public async Task NpmPackageSourceResolver_InheritsDefaultRegistryFromParent()
    {
        await using var tempDir = TemporaryDirectory.Create();
        var srcDirectory = tempDir.GetFullPath("src");
        var nestedDirectory = srcDirectory / "nested";
        Directory.CreateDirectory(nestedDirectory);

        var packageFile = nestedDirectory / "package.json";
        await File.WriteAllTextAsync(packageFile, "{}", XunitCancellationToken);
        await File.WriteAllTextAsync(tempDir.CreateEmptyFile(".npmrc"), "registry=https://root.registry/", XunitCancellationToken);

        var registry = NpmPackageSourceResolver.ResolveRegistry(FullPath.FromPath(packageFile), "left-pad");

        Assert.Equal("https://root.registry/", registry.ToString());
    }

    [Fact]
    public void NpmVersioningStrategy_GetUpdateReferenceText_PreservesPrefix()
    {
        var strategy = NpmVersioningStrategy.Instance;

        Assert.Equal("^2.0.0", strategy.GetUpdateReferenceText("^1.0.0", "2.0.0"));
        Assert.Equal("~2.0.0", strategy.GetUpdateReferenceText("~1.0.0", "2.0.0"));
        Assert.Equal("2.0.0", strategy.GetUpdateReferenceText("1.0.0", "2.0.0"));

        // A partial range keeps its prefix and gains the full version, like 'npm update --save'
        Assert.Equal("^1.9.4", strategy.GetUpdateReferenceText("^1.1", "1.9.4"));
        Assert.Equal("~7.7.4", strategy.GetUpdateReferenceText("~7", "7.7.4"));
    }

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("^1.1", true)]
    [InlineData("~7", true)]
    [InlineData(">=2.4", true)]
    [InlineData("v1.2", true)]
    [InlineData("1.x", false)]
    [InlineData("*", false)]
    [InlineData("latest", false)]
    [InlineData("", false)]
    public void NpmVersioningStrategy_IsSupportedVersion(string version, bool expectedResult)
    {
        Assert.Equal(expectedResult, NpmVersioningStrategy.Instance.IsSupportedVersion(version));
    }

    [Theory]
    [InlineData("^1.1", "1.9.4", true)]
    [InlineData("~7", "7.7.4", true)]
    [InlineData("^1.1", "1.1.0", false)]
    [InlineData("^2", "1.9.4", false)]
    public void NpmVersioningStrategy_IsCompatibleVersion(string currentVersion, string candidateVersion, bool expectedResult)
    {
        Assert.Equal(expectedResult, NpmVersioningStrategy.Instance.IsCompatibleVersion(currentVersion, candidateVersion));
    }

    [Theory]
    [InlineData("1.0.0-alpine", "1.0.1-alpine", true)]
    [InlineData("1.0.0-alpine", "1.0.1-slim", false)]
    [InlineData("1.0.0", "1.0.1-alpine", false)]
    [InlineData("1.0.0", "1.1.0", true)]
    // Two- and one-component tags are the common shape for official images
    [InlineData("1.27", "1.31", true)]
    [InlineData("9.0", "10.0", true)]
    [InlineData("20-alpine", "24-alpine", true)]
    [InlineData("20-alpine", "24-slim", false)]
    // The candidate is written back as the tag, so the number of components must not change
    [InlineData("1.27", "1.31.4", false)]
    [InlineData("1.27.1", "1.31", false)]
    [InlineData("20-alpine", "24.1-alpine", false)]
    // Still not a version
    [InlineData("latest", "1.0.0", false)]
    [InlineData("1.0.0", "latest", false)]
    public void DockerVersioningStrategy_IsCompatibleVersion(string currentVersion, string candidateVersion, bool expectedResult)
    {
        var strategy = DockerVersioningStrategy.Instance;

        Assert.Equal(expectedResult, strategy.IsCompatibleVersion(currentVersion, candidateVersion));
    }

    [Theory]
    [InlineData("1.0.0", "1.1.0", -1)]
    [InlineData("1.1.0", "1.0.0", 1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    public void NuGetVersioningStrategy_CompareVersions(string x, string y, int expectedSign)
    {
        var strategy = NuGetVersioningStrategy.Instance;

        var result = strategy.CompareVersions(x, y);
        Assert.Equal(expectedSign, Math.Sign(result));
    }

    [Theory]
    [InlineData("v1.0.0", true)]
    [InlineData("V1.0.0", true)]
    [InlineData("1.0.0", true)]
    [InlineData("latest", false)]
    public void SemanticVersioningStrategy_PrefixAllowed_IsSupportedVersion(string version, bool expectedResult)
    {
        var strategy = SemanticVersioningStrategy.PrefixAllowed;

        Assert.Equal(expectedResult, strategy.IsSupportedVersion(version));
    }

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("v1.0.0", false)]
    [InlineData("V1.0.0", false)]
    [InlineData("latest", false)]
    public void SemanticVersioningStrategy_Strict_IsSupportedVersion(string version, bool expectedResult)
    {
        var strategy = SemanticVersioningStrategy.Strict;

        Assert.Equal(expectedResult, strategy.IsSupportedVersion(version));
    }

    [Theory]
    [InlineData("1", "2", true)]
    [InlineData("1.0", "2.0", true)]
    [InlineData("1.0.0", "2.0.0", true)]
    [InlineData("v1", "v2", true)]
    [InlineData("v1.0", "v2.0", true)]
    [InlineData("v1.0.0", "v2.0.0", true)]
    [InlineData("v1.0", "v2", false)]
    [InlineData("v1", "v2.0.0", false)]
    [InlineData("1.0", "2", false)]
    [InlineData("1", "2.0", false)]
    [InlineData("1", "v2", false)]
    public void GitHubActionsVersioningStrategy_IsCompatibleVersion(string currentVersion, string candidateVersion, bool expectedResult)
    {
        var strategy = GitHubActionsVersioningStrategy.Instance;

        Assert.Equal(expectedResult, strategy.IsCompatibleVersion(currentVersion, candidateVersion));
    }

    [Theory]
    // GitHub sends absolute URLs and several relations in one header
    [InlineData("<https://api.github.com/repositories/1/tags?page=2>; rel=\"next\", <https://api.github.com/repositories/1/tags?page=9>; rel=\"last\"", "https://api.github.com/repositories/1/tags?page=2")]
    // Docker registries send a relative URL that has to be resolved against the request
    [InlineData("</v2/library/nginx/tags/list?n=100&last=1.27>; rel=\"next\"", "https://registry-1.docker.io/v2/library/nginx/tags/list?n=100&last=1.27")]
    // The last page only advertises relations other than 'next'
    [InlineData("<https://api.github.com/repositories/1/tags?page=1>; rel=\"prev\", <https://api.github.com/repositories/1/tags?page=1>; rel=\"first\"", null)]
    public void LinkHeader_TryGetNextPageUri(string headerValue, string? expected)
    {
        using var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("Link", headerValue);

        var result = LinkHeader.TryGetNextPageUri(response, new Uri("https://registry-1.docker.io/v2/library/nginx/tags/list"));

        Assert.Equal(expected, result?.ToString());
    }

    [Fact]
    public void LinkHeader_TryGetNextPageUri_NoHeader()
    {
        using var response = new HttpResponseMessage();

        Assert.Null(LinkHeader.TryGetNextPageUri(response, new Uri("https://registry-1.docker.io/v2/library/nginx/tags/list")));
    }

    private static async Task<IReadOnlyList<Dependency>> ScanDependencies(TemporaryDirectory temporaryDirectory)
    {
        var deps = (await DependencyScanner.ScanDirectoryAsync(temporaryDirectory.FullPath, options: null, XunitCancellationToken)).ToList();
        return deps.OrderBy(dep => dep.VersionLocation!.FilePath, StringComparer.Ordinal).ToArray();
    }
}
