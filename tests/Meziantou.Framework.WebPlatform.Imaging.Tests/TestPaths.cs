namespace Meziantou.Framework.WebPlatform.Imaging.Tests;

/// <summary>
/// The paths of the files of the tests. They are found from the repository root, not from the paths of the source files:
/// the deterministic builds of CI map the source paths to <c>/_/</c>.
/// </summary>
internal static class TestPaths
{
    public static string Root { get; } = FindRoot();

    public static string ProjectDirectory { get; } = Path.Combine(Root, "tests", "Meziantou.Framework.WebPlatform.Imaging.Tests");

    public static string TestData(string name) => Path.Combine(ProjectDirectory, "TestData", name);

    private static string FindRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "Meziantou.Framework.slnx")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new InvalidOperationException("Cannot find the repository root");
    }
}
