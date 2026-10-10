namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>Locates the golden corpus copied next to the test assemblies (<c>tests/Meziantou.Framework.Imaging.Fixtures</c> is copied to <c>&lt;output&gt;/Fixtures</c>).</summary>
public static class FixtureRoot
{
    /// <summary>The name of the manifest file at the root of the corpus.</summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>The name of the JSON schema file at the root of the corpus.</summary>
    public const string SchemaFileName = "manifest.schema.json";

    /// <summary>Gets the absolute path of the corpus copied to the test output directory.</summary>
    /// <returns>The corpus directory.</returns>
    /// <exception cref="DirectoryNotFoundException">The corpus was not copied (the test project does not include tests/Meziantou.Framework.Imaging.Fixtures).</exception>
    public static FullPath GetDirectory()
    {
        var path = FullPath.FromPath(AppContext.BaseDirectory) / "Fixtures";
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"The fixture directory '{path}' does not exist. The test project must copy tests/Meziantou.Framework.Imaging.Fixtures to its output directory.");

        return path;
    }
}
