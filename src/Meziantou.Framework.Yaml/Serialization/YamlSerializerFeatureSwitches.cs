namespace Meziantou.Framework.Yaml.Serialization;

internal static class YamlSerializerFeatureSwitches
{
    internal const string ReflectionSwitchName = "Meziantou.Framework.Yaml.YamlSerializer.IsReflectionEnabledByDefault";

    // This property is stubbed by ILLink.Substitutions.xml when the feature switch is disabled.
#if NET10_0_OR_GREATER
    [FeatureSwitchDefinition(ReflectionSwitchName)]
#endif
    public static bool IsReflectionEnabledByDefault
        => AppContext.TryGetSwitch(ReflectionSwitchName, out var enabled) ? enabled : true;

    public static readonly bool IsReflectionEnabledByDefaultCalculated = IsReflectionEnabledByDefault;
}
