namespace Meziantou.Framework.Geo.Maps.Tool;

/// <summary>A region of the bundles: the world, a continent, or a country of a continent, with its ISO 3166-1 alpha-2 codes.</summary>
/// <param name="Id">The identifier of the region on the command line, in lowercase words joined by hyphens, such as <c>north-america</c>.</param>
/// <param name="ExtentExclusions">The ISO codes of the members whose borders do not extend the extent of the region.</param>
/// <param name="Extent">The extent of a region that has no border to give it, such as Antarctica.</param>
internal sealed record Region(string Id, string? Parent, string Name, string[] Iso, bool IsWorld = false, string[]? ExtentExclusions = null, GeoBounds? Extent = null)
{
    /// <summary>Gets the name of the folder of the region, in PascalCase, such as <c>World</c>, <c>NorthAmerica</c> or <c>Switzerland</c>.</summary>
    public string Key => Regions.GetKey(Name);

    public bool IsContinent => !IsWorld && Parent is null;
}
