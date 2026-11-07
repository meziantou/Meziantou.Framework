namespace Meziantou.Framework.Imaging.TestHarness;

/// <summary>
/// Locates the files of the repository the tests read. They are found from the Git repository root, not from the paths of
/// the source files: the deterministic builds of CI map the source paths to <c>/_/</c>.
/// </summary>
public static class RepositoryPaths
{
    /// <summary>Gets the root of the Git repository that contains the test assemblies.</summary>
    public static FullPath Root { get; } = FullPath.FromPath(AppContext.BaseDirectory).FindRequiredGitRepositoryRoot();
}
