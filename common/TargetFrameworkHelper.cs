namespace TestUtilities;

internal static class TargetFrameworkHelper
{
        public static string GetTargetFrameworkMoniker() =>
#if NET8_0
        $"net8.0";
#elif NET9_0
        $"net9.0";
#elif NET10_0
        $"net10.0";
#elif NET11_0
            $"net11.0";
#else
#error Version not supported
#endif

        public static string GetMicrosoftNetCoreAppRefVersion() =>
#if NET8_0
        $"8.0.0";
#elif NET9_0
        $"9.0.0";
#elif NET10_0
        $"10.0.0";
#elif NET11_0
        $"11.0.0-preview.4.26230.115";
#else
#error Version not supported
#endif
}
