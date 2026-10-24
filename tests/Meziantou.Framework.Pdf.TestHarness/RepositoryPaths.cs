namespace Meziantou.Framework.Pdf.TestHarness;

/// <summary>
/// Locates the files of the repository the tests read. They are found from the Git repository root, not from the paths of
/// the source files: the deterministic builds of CI map the source paths to <c>/_/</c>.
/// </summary>
public static class RepositoryPaths
{
    /// <summary>Gets the root of the Git repository that contains the test assemblies.</summary>
    public static FullPath Root { get; } = FullPath.FromPath(AppContext.BaseDirectory).FindRequiredGitRepositoryRoot();

    /// <summary>Gets the directory of the vendored fixtures (<c>tests/Meziantou.Framework.Pdf.Fixtures</c>).</summary>
    public static FullPath Fixtures => Root / "tests" / "Meziantou.Framework.Pdf.Fixtures";

    /// <summary>Gets the directory the external corpora are fetched to (<c>tests/Meziantou.Framework.Pdf.External</c>, not committed).</summary>
    public static FullPath External => Root / "tests" / "Meziantou.Framework.Pdf.External";

    /// <summary>Gets the directory of the TSV files of the vendored Arlington PDF Model.</summary>
    public static FullPath ArlingtonTsv => Fixtures / "arlington" / "tsv" / "latest";

    /// <summary>Gets the directory of the fonts of the fixtures of Meziantou.Framework.Imaging, which the tests of the embedded fonts share.</summary>
    public static FullPath Fonts => Root / "tests" / "Meziantou.Framework.Imaging.Fixtures" / "fonts";
}
