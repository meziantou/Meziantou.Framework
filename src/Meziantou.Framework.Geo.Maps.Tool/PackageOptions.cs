namespace Meziantou.Framework.Geo.Maps.Tool;

/// <summary>The options of the NuGet packages of the bundles.</summary>
/// <param name="OutputDirectory">The folder of the packages.</param>
/// <param name="IdPrefix">The prefix of the identifiers, followed by a dot and the key of the region.</param>
/// <param name="Version">The version of the packages.</param>
/// <param name="Authors">The authors of the packages.</param>
/// <param name="Content">How the packages ship the maps.</param>
internal sealed record PackageOptions(FullPath OutputDirectory, string IdPrefix, string Version, string Authors, PackageContent Content = PackageContent.Assembly);
