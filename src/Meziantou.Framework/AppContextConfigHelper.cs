namespace Meziantou.Framework;

internal static class AppContextConfigHelper
{
    internal static bool GetBooleanConfig(string configName, bool defaultValue) =>
        AppContext.TryGetSwitch(configName, out bool value) ? value : defaultValue;

    internal static bool GetBooleanConfig(string switchName, string envVariable, bool defaultValue = false)
    {
        if (!AppContext.TryGetSwitch(switchName, out var ret))
        {
            var switchValue = Environment.GetEnvironmentVariable(envVariable);
            ret = switchValue != null ? (IsTrueStringIgnoreCase(switchValue) || switchValue.Equals("1", StringComparison.Ordinal)) : defaultValue;
        }

        return ret;
    }

    private static bool IsTrueStringIgnoreCase(ReadOnlySpan<char> value)
    {
        return value.Length == 4 &&
                (value[0] == 't' || value[0] == 'T') &&
                (value[1] == 'r' || value[1] == 'R') &&
                (value[2] == 'u' || value[2] == 'U') &&
                (value[3] == 'e' || value[3] == 'E');
    }
}
