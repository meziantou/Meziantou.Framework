using System.IO;
using System.Linq;
using System.Text.Json;
using Meziantou.Framework;
using Meziantou.Framework.DependencyScanning;
using Meziantou.Framework.DependencyScanning.Tool;
using NuGet.Versioning;

namespace Meziantou.Framework.DependencyScanning.Tool.Tests;

[Collection("Tool")] // Ensure tests run sequentially
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

    private static async Task<IReadOnlyList<Dependency>> ScanDependencies(TemporaryDirectory temporaryDirectory)
    {
        var deps = (await DependencyScanner.ScanDirectoryAsync(temporaryDirectory.FullPath, options: null, XunitCancellationToken)).ToList();
        return deps.OrderBy(dep => dep.VersionLocation.FilePath, System.StringComparer.Ordinal).ToArray();
    }
}
