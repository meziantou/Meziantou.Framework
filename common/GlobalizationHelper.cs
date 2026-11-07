namespace Meziantou.Framework;

internal static class GlobalizationHelper
{
    /// <summary>
    /// Determines whether the application runs in globalization-invariant mode.
    /// </summary>
    /// <returns><see langword="true"/> when invariant globalization mode is enabled; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// This mirrors how the runtime resolves the setting: the <c>System.Globalization.Invariant</c> switch, which is set by the
    /// <c>InvariantGlobalization</c> MSBuild property, takes precedence over the <c>DOTNET_SYSTEM_GLOBALIZATION_INVARIANT</c>
    /// environment variable.
    /// </remarks>
    public static bool IsGlobalizationInvariant()
    {
        if (AppContext.TryGetSwitch("System.Globalization.Invariant", out var isEnabled))
            return isEnabled;

        // The environment variable is read by the runtime but is not surfaced as an AppContext switch
        var value = Environment.GetEnvironmentVariable("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT");
        return value is "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
