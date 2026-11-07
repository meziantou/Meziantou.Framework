namespace Meziantou.Framework.WebPlatform.Wpt;

internal static class WptPaths
{
    public static string Root { get; } = FullPath.FromPath(AppContext.BaseDirectory).FindRequiredGitRepositoryRoot();

    public static string SpecData(params string[] path) => Path.Combine([Root, "tests", "Meziantou.Framework.WebPlatform.SpecData", .. path]);

    /// <summary>The root of the web-platform-tests checkout. Run 'eng/webplatform/fetch-tests.sh' to download it.</summary>
    public static string Wpt { get; } = Path.Combine(Root, "tests", "Meziantou.Framework.WebPlatform.External", "wpt");

    public static string Expectations { get; } = Path.Combine(Root, "tests", "Meziantou.Framework.WebPlatform.Wpt", "expectations");
}
