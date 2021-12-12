namespace Meziantou.Framework;

internal static class GlobalizationMode
{
    private static partial class Settings
    {
        internal static bool Invariant { get; } = AppContextConfigHelper.GetBooleanConfig("System.Globalization.Invariant", "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT");
        internal static bool PredefinedCulturesOnly { get; } = AppContextConfigHelper.GetBooleanConfig("System.Globalization.PredefinedCulturesOnly", "DOTNET_SYSTEM_GLOBALIZATION_PREDEFINED_CULTURES_ONLY", GlobalizationMode.Invariant);
    }

    public static bool Invariant => Settings.Invariant;
    public static bool PredefinedCulturesOnly => Settings.PredefinedCulturesOnly;
}
