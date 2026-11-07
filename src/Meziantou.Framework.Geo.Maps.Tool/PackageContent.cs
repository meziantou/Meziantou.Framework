namespace Meziantou.Framework.Geo.Maps.Tool;

/// <summary>How the NuGet package of a bundle ships its map.</summary>
internal enum PackageContent
{
    /// <summary>The map is embedded in an assembly that the .NET SDK builds, and opened with <c>Map.Region_&lt;Key&gt;</c>.</summary>
    Assembly,

    /// <summary>The map files are copied to the <c>maps</c> folder next to the application, where <see cref="MapCatalog.Default"/> reads them.</summary>
    Files,
}
