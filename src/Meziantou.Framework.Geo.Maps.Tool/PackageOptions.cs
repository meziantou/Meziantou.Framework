namespace Meziantou.Framework.Geo.Maps.Tool;

/// <summary>The options of the NuGet packages of the bundles.</summary>
/// <param name="OutputDirectory">The folder of the packages.</param>
/// <param name="IdPrefix">The prefix of the identifiers, followed by a dot and the key of the region.</param>
/// <param name="Version">The version of the packages.</param>
/// <param name="Authors">The authors of the packages.</param>
/// <param name="Content">How the packages ship the maps.</param>
/// <param name="Sdk">The MSBuild SDK of the projects of the assemblies, such as <c>Microsoft.NET.Sdk</c>.</param>
/// <param name="TargetFrameworks">The target frameworks of the assemblies, separated by semicolons, or an MSBuild property that gives them.</param>
/// <param name="ProjectDirectory">The folder to write the projects of the assemblies to and keep them, or <see langword="null"/> for a temporary folder, where they ignore the MSBuild files of the folders above them.</param>
internal sealed record PackageOptions(
    FullPath OutputDirectory,
    string IdPrefix,
    string Version,
    string Authors,
    PackageContent Content = PackageContent.Assembly,
    string Sdk = MapProjectWriter.DefaultSdk,
    string TargetFrameworks = MapProjectWriter.DefaultTargetFramework,
    FullPath? ProjectDirectory = null);
