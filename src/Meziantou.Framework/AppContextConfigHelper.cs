namespace Meziantou.Framework;

internal static class AppContextConfigHelper
{
    internal static bool GetBooleanConfig(string configName, bool defaultValue) =>
        AppContext.TryGetSwitch(configName, out var value) ? value : defaultValue;

    internal static bool GetBooleanConfig(string switchName, string envVariable, bool defaultValue = false)
    {
        if (!AppContext.TryGetSwitch(switchName, out var ret))
        {
            var switchValue = Environment.GetEnvironmentVariable(envVariable);
            ret = switchValue is not null ? (switchValue.Equals("true", StringComparison.OrdinalIgnoreCase) || switchValue.Equals("1", StringComparison.Ordinal)) : defaultValue;
        }
        
        return ret;
    }
}
