using System;
using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Toml.Serialization;

internal static class TomlSerializerFeatureSwitches
{
    internal const string ReflectionSwitchName = "Meziantou.Framework.Toml.TomlSerializer.IsReflectionEnabledByDefault";

    // The guards read TomlSerializer.IsReflectionEnabledByDefault, which is the feature switch the trimmer substitutes
    public static bool IsReflectionEnabledByDefault
        => !AppContext.TryGetSwitch(ReflectionSwitchName, out var enabled) || enabled;
}

