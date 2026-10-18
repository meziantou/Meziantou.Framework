namespace Meziantou.Framework.WebPlatform.Tests;

internal static class TestPaths
{
    public static string Root { get; } = FindRoot();

    public static string SpecData(params string[] path) => Path.Combine([Root, "tests", "Meziantou.Framework.WebPlatform.SpecData", .. path]);

    /// <summary>Path of an external test suite. Run 'eng/webplatform/fetch-tests.sh' to download them.</summary>
    public static string External(params string[] path)
    {
        var result = Path.Combine([Root, "tests", "Meziantou.Framework.WebPlatform.External", .. path]);
        if (!File.Exists(result) && !Directory.Exists(result))
            throw new InvalidOperationException($"'{result}' does not exist. Run 'eng/webplatform/fetch-tests.sh' to download the external test suites.");

        return result;
    }

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
