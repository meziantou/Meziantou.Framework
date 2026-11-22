namespace Meziantou.Framework.Csv.Serialization;

internal static class CsvSerializerFeatureSwitches
{
    internal const string ReflectionSwitchName = "Meziantou.Framework.Csv.Serialization.CsvSerializer.IsReflectionEnabledByDefault";

    // The guards read CsvSerializer.IsReflectionEnabledByDefault, which is the feature switch the trimmer substitutes
    public static bool IsReflectionEnabledByDefault
        => !AppContext.TryGetSwitch(ReflectionSwitchName, out var enabled) || enabled;
}
