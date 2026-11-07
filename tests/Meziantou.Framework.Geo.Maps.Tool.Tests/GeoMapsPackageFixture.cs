using TestUtilities;

namespace Meziantou.Framework.Geo.Maps.Tool.Tests;

/// <summary>The packages of Meziantou.Framework.Geo.Maps and Meziantou.Framework.Geo, that the packages of the bundles depend on.</summary>
public sealed class GeoMapsPackageFixture() : NuGetPackageFixture("Meziantou.Framework.Geo.Maps")
{
    protected override string? TargetFrameworks => "net10.0";
}
