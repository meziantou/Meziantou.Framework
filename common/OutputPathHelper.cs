using Meziantou.Framework;
namespace TestUtilities;

internal static class OutputPathHelper
{
    public static FullPath GetOutputDirectory(string projectName)
    {
        var mode =
#if DEBUG
        "debug";
#else
        "release";
#endif
        var root = FullPath.CurrentDirectory().FindRequiredGitRepositoryRoot();
        return root / "artifacts" / "bin" / projectName / $"{mode}_{TargetFrameworkHelper.GetTargetFrameworkMoniker()}" / $"{projectName}.dll";
    }
}
