namespace Meziantou.Framework.WebPlatform.Imaging.Tests;

/// <summary>
/// The paths of the files of the tests. They are found from the Git repository root, not from the paths of the source files:
/// the deterministic builds of CI map the source paths to <c>/_/</c>.
/// </summary>
internal static class TestPaths
{
    public static string Root { get; } = FullPath.FromPath(AppContext.BaseDirectory).FindRequiredGitRepositoryRoot();

    public static string ProjectDirectory { get; } = Path.Combine(Root, "tests", "Meziantou.Framework.WebPlatform.Imaging.Tests");

    public static string TestData(string name) => Path.Combine(ProjectDirectory, "TestData", name);
}
