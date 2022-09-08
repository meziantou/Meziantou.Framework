﻿using System.Globalization;
using System.Resources;

namespace Meziantou.Framework;

internal sealed class ResxLocalizationProvider : ILocalizationProvider
{
    private static readonly ResourceManager ResourceManager = new ResourceManager("Meziantou.Framework.RelativeDates", typeof(RelativeDates).Assembly);

    public static ILocalizationProvider Instance { get; } = new ResxLocalizationProvider();

    public string GetString(string name, CultureInfo? culture)
    {
        return ResourceManager.GetString(name, culture) ?? "";
    }
}
