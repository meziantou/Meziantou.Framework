using System.IO;
using System.Linq;
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

    private static async Task<IReadOnlyList<Dependency>> ScanDependencies(TemporaryDirectory temporaryDirectory)
    {
        var deps = (await DependencyScanner.ScanDirectoryAsync(temporaryDirectory.FullPath, options: null, XunitCancellationToken)).ToList();
        return deps.OrderBy(dep => dep.VersionLocation!.FilePath, System.StringComparer.Ordinal).ToArray();
    }
}
