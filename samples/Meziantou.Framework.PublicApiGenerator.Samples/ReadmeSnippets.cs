namespace Meziantou.Framework.PublicApiGenerator.Samples;

/// <summary>The examples of src/Meziantou.Framework.PublicApiGenerator/readme.md that need the builds of a library for several target frameworks.</summary>
public static class ReadmeSnippets
{
    public static IReadOnlyList<PublicApiFile> GenerateMultiTarget()
    {
        #region PublicApi_GenerateMultiTarget
        var files = PublicApi.Generate(
            assemblySources:
            [
                "/path/to/MyLibrary.netstandard2.0.dll",
                "/path/to/MyLibrary.net10.0.dll",
            ]);
        #endregion

        return files;
    }

    public static IReadOnlyList<PublicApiFile> GenerateMultiTargetWithMonikers()
    {
        #region PublicApi_GenerateMultiTargetWithMonikers
        var files = PublicApi.Generate(
            assemblySources:
            [
                new AssemblySource("/path/to/MyLibrary.netstandard2.0.dll", ".NETStandard,Version=v2.0"),
                new AssemblySource("/path/to/MyLibrary.net10.0.dll", ".NETCoreApp,Version=v10.0"),
            ]);
        #endregion

        return files;
    }

    public static void Aggregate()
    {
        #region PublicApi_Aggregate
        var aggregate = PublicApi.Aggregate(
        [
            PublicApi.ReadAssembly("/path/to/netstandard2.0/MyLibrary.dll", new PublicApiReadOptions { TargetFramework = "netstandard2.0" }),
            PublicApi.ReadAssembly("/path/to/net10.0/MyLibrary.dll", new PublicApiReadOptions { TargetFramework = "net10.0" }),
        ]);

        foreach (var type in aggregate.Types)
        {
            foreach (var member in type.Members)
            {
                if (!member.IsAvailableInAllTargetFrameworks)
                    Console.WriteLine($"{member.DocumentationId} is only available in {string.Join(", ", member.TargetFrameworks)}");

                if (member.Differences != PublicApiSymbolDifferences.None) // e.g. Signature, Nullability, Constraints, Attributes, Inheritance
                {
                    foreach (var variant in member.Variants)
                        Console.WriteLine($"{string.Join(", ", variant.TargetFrameworks)}: {PublicApiFormatter.Format(variant.Symbol)}");
                }
            }
        }
        #endregion
    }
}
