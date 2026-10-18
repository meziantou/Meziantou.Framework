namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>Locates the golden corpus (<c>tests/Meziantou.Framework.Imaging.Fixtures</c>) in the repository.</summary>
public static class FixtureRoot
{
    /// <summary>The name of the manifest file at the root of the corpus.</summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>The name of the JSON schema file at the root of the corpus.</summary>
    public const string SchemaFileName = "manifest.schema.json";

    /// <summary>Gets the absolute path of the corpus.</summary>
    /// <returns>The corpus directory.</returns>
    /// <exception cref="DirectoryNotFoundException">The corpus does not exist in the repository.</exception>
    public static FullPath GetDirectory()
    {
        var path = RepositoryPaths.Root / "tests" / "Meziantou.Framework.Imaging.Fixtures";
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"The fixture directory '{path}' does not exist.");

        return path;
    }
}
