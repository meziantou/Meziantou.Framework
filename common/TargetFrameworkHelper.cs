namespace TestUtilities;

internal static class TargetFrameworkHelper
{
    private const int TargetFrameworkMajorVersion =
#if NET8_0
        8
#elif NET9_0
        9
#elif NET10_0
        10
#else
#error Version not supported
#endif
        ;

    public static string GetTargetFrameworkMoniker() => $"net{TargetFrameworkMajorVersion}.0";

    public static string GetMicrosoftNetCoreAppRefVersion() => $"{TargetFrameworkMajorVersion}.0.0";
}
