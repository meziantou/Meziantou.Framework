// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Tomlyn.Serialization;

internal static class TomlSerializerFeatureSwitches
{
    internal const string ReflectionSwitchName = "Tomlyn.TomlSerializer.IsReflectionEnabledByDefault";

    // This property is stubbed by ILLink.Substitutions.xml when the feature switch is disabled.
    [FeatureSwitchDefinition(ReflectionSwitchName)]
    public static bool IsReflectionEnabledByDefault
        => !AppContext.TryGetSwitch(ReflectionSwitchName, out var enabled) || enabled;

    public static readonly bool IsReflectionEnabledByDefaultCalculated = IsReflectionEnabledByDefault;
}

