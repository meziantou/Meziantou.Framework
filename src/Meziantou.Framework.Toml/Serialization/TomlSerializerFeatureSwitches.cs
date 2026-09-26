using System;
using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Toml.Serialization;

internal static class TomlSerializerFeatureSwitches
{
    internal const string ReflectionSwitchName = "Meziantou.Framework.Toml.TomlSerializer.IsReflectionEnabledByDefault";

    // This property is stubbed by ILLink.Substitutions.xml when the feature switch is disabled.
    [FeatureSwitchDefinition(ReflectionSwitchName)]
    public static bool IsReflectionEnabledByDefault
        => !AppContext.TryGetSwitch(ReflectionSwitchName, out var enabled) || enabled;

    public static readonly bool IsReflectionEnabledByDefaultCalculated = IsReflectionEnabledByDefault;
}

