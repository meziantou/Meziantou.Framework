namespace TestUtilities;

internal static class TargetFrameworkHelper
{
    public static string GetTargetFrameworkMoniker() =>
#if NET10_0
        $"net10.0";
#elif NET11_0
        $"net11.0";
#else
#error Version not supported
#endif

    public static string GetMicrosoftNetCoreAppRefVersion() =>
#if NET10_0
        $"10.0.0";
#elif NET11_0
        $"11.0.0-rc.1.26425.128";
#else
#error Version not supported
#endif
}
