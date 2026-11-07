using Meziantou.Framework.Geo.Maps.Tool;
using Xunit.Sdk;

namespace Meziantou.Framework.Geo.Maps.Tool.Tests;

/// <summary>Packs a bundle with the tool, and uses the package in an application, as its users do.</summary>
public sealed class GeoMapsPackageTests(GeoMapsPackageFixture fixture, ITestOutputHelper testOutputHelper) : IClassFixture<GeoMapsPackageFixture>
{
    [Fact]
    public async Task Pack_PackageOpensTheMapInAnApplication()
    {
        await using var temp = TemporaryDirectory.Create();
        var (pins, cache) = TestSources.Create(temp.FullPath);
        var packages = temp.FullPath / "packages";
        var console = new ConsoleHelper(testOutputHelper);
        var exitCode = await Program.MainImpl(["pack", "--region", "switzerland", "--package-id-prefix", "Contoso.Maps", "--package-version", "2.0.0", "--package-output", packages, "--pins", pins, "--cache", cache, "--output", temp.FullPath / "maps"], console.ConfigureConsole);
        Assert.Equal(0, exitCode);

        // The application references the package of the bundle, as its users do
        var application = temp.FullPath / "application";
        Directory.CreateDirectory(application);
        File.WriteAllText(application / "global.json", $$"""
            {
              "sdk": {
                "version": "{{fixture.DotnetSdkVersion}}",
                "rollForward": "latestFeature"
              }
            }
            """);
        File.WriteAllText(application / "NuGet.config", $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="{{temp.FullPath / "global-packages"}}" />
              </config>
              <packageSources>
                <clear />
                <add key="maps" value="{{packages}}" />
                <add key="local" value="{{fixture.PackagesDirectory}}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="maps">
                  <package pattern="Contoso.Maps.*" />
                </packageSource>
                <packageSource key="local">
                  <package pattern="Meziantou.Framework.Geo*" />
                </packageSource>
                <packageSource key="nuget.org">
                  <package pattern="*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);

        // The package of the bundle depends on the stable version of Meziantou.Framework.Geo.Maps the tool was built with,
        // which NuGet does not resolve to the prerelease version of the fixture: the application references it
        File.WriteAllText(application / "Application.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Contoso.Maps.Switzerland" Version="2.0.0" />
                <PackageReference Include="Meziantou.Framework.Geo.Maps" Version="{{fixture.PackageVersion}}" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(application / "Program.cs", """
            using Contoso.Maps;
            using Meziantou.Framework.Geo;
            using Meziantou.Framework.Geo.Maps;

            var map = Map.Region_Switzerland;
            Console.WriteLine("Name: " + map.Name);
            Console.WriteLine("Layers: " + string.Join(",", map.Layers));
            Console.WriteLine("Subdivisions: " + string.Join(",", map.Load(MapLayer.StatesProvinces).Select(state => state.Properties["iso"]).Order()));
            Console.WriteLine("Cities around Bern: " + string.Join(",", map.Load(MapLayer.Cities, new GeoBounds(7, 46.5, 8, 47.5)).Select(city => city.Properties["name"])));
            Console.WriteLine("Same map: " + ReferenceEquals(map, Map.Region_Switzerland));
            Console.WriteLine("Attribution: " + map.Attribution);
            Console.WriteLine("Bounds: " + map.Bounds);
            Console.WriteLine("Last modified: " + map.Metadata.LastModified?.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            Console.WriteLine("Sources: " + string.Join(",", map.Metadata.Sources.Select(source => source.Name)));
            Console.WriteLine("Has coastline: " + map.HasLayer(MapLayer.Coastline));
            Console.WriteLine("Cities in the ocean: " + map.Load(MapLayer.Cities, new GeoBounds(0, 0, 1, 1)).Count);
            Console.WriteLine("Maps folder: " + Directory.Exists(Path.Combine(AppContext.BaseDirectory, "maps")));
            """);

        await RunDotNetCommand(application, ["restore", "--disable-build-servers"]);
        var output = await RunDotNetCommand(application, ["run", "--no-restore", "--disable-build-servers", "-nologo"]);
        Assert.Contains("Name: Switzerland", output);
        Assert.Contains("Layers: Borders,StatesProvinces,Cities,Rivers,Lakes,PhysicalRegions", output);
        Assert.Contains("Subdivisions: CH-BE,CH-ZH", output);
        Assert.Contains("Cities around Bern: Bern", output);
        Assert.Contains("Same map: True", output);
        Assert.Contains("Attribution: © OpenStreetMap contributors", output);
        Assert.Contains("Bounds: GeoBounds { West = 6, South = 45, East = 10, North = 48 }", output);
        Assert.Contains("Last modified: 2026-01-02T00:00:00.0000000+00:00", output);
        Assert.Contains("Sources: country-levels,Nominatim,Natural Earth", output);
        Assert.Contains("Has coastline: False", output);
        Assert.Contains("Cities in the ocean: 0", output);
        Assert.Contains("Maps folder: False", output);
    }

    private static async Task<string> RunDotNetCommand(FullPath workingDirectory, IReadOnlyList<string> arguments)
    {
        var result = await ProcessWrapper.Create("dotnet")
            .WithArguments(arguments)
            .WithWorkingDirectory(workingDirectory)
            .WithEnvironmentVariables(env => env.Set("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1"))
            .WithValidation(ProcessValidationMode.None)
            .ExecuteBufferedAsync(XunitCancellationToken);

        var output = string.Join('\n', result.Output);
        if (result.ExitCode != 0)
            throw new XunitException($"Command failed: dotnet {string.Join(' ', arguments)}\nExit code: {result.ExitCode}\nOutput:\n{output}");

        return output;
    }
}
