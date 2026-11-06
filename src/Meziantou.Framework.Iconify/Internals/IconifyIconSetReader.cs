namespace Meziantou.Framework.Internals;

/// <summary>
/// Reads an icon set in the IconifyJSON format and resolves its aliases, following the <c>getIconData</c> function of @iconify/utils.
/// </summary>
internal static class IconifyIconSetReader
{
    private const double DefaultSize = 16;

    public static IconifyIconSetData Read(string json)
    {
        if (IconifyJsonParser.Parse(json) is not Dictionary<string, object?> root)
            throw new FormatException("The icon set must be a JSON object.");

        if (!root.TryGetValue("prefix", out var prefixValue) || prefixValue is not string prefix || prefix.Length == 0)
            throw new FormatException("The icon set must have a non-empty 'prefix' property.");

        if (!root.TryGetValue("icons", out var iconsValue) || iconsValue is not Dictionary<string, object?> icons)
            throw new FormatException("The icon set must have an 'icons' object.");

        var aliases = root.TryGetValue("aliases", out var aliasesValue) && aliasesValue is Dictionary<string, object?> aliasesObject ? aliasesObject : null;
        var defaults = new IconProperties();
        ReadProperties(root, ref defaults, readTransformations: false);

        var result = new List<IconifyIconData>(icons.Count + (aliases?.Count ?? 0));
        foreach (var entry in icons)
        {
            if (entry.Value is not Dictionary<string, object?> icon)
                throw new FormatException($"Icon '{entry.Key}' must be a JSON object.");

            if (!icon.TryGetValue("body", out var bodyValue) || bodyValue is not string)
                throw new FormatException($"Icon '{entry.Key}' must have a 'body' string.");

            result.Add(Resolve(entry.Key, icon, parentChain: null, defaults));
        }

        if (aliases is not null)
        {
            foreach (var entry in aliases)
            {
                if (icons.ContainsKey(entry.Key))
                    continue;

                var chain = GetAliasChain(entry.Key, icons, aliases);
                if (chain is null)
                    continue;

                result.Add(Resolve(entry.Key, chain[chain.Count - 1], chain, defaults));
            }
        }

        return new IconifyIconSetData(prefix, ReadInfo(root), result);
    }

    /// <summary>Returns the alias and its ancestors, from the alias to the icon, or null when the chain is broken or has a cycle.</summary>
    private static List<Dictionary<string, object?>>? GetAliasChain(string name, Dictionary<string, object?> icons, Dictionary<string, object?> aliases)
    {
        var chain = new List<Dictionary<string, object?>>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = name;
        while (true)
        {
            if (icons.TryGetValue(current, out var iconValue))
            {
                if (iconValue is not Dictionary<string, object?> icon || !icon.TryGetValue("body", out var body) || body is not string)
                    return null;

                chain.Add(icon);
                return chain;
            }

            if (!visited.Add(current))
                return null;

            if (!aliases.TryGetValue(current, out var aliasValue) || aliasValue is not Dictionary<string, object?> alias)
                return null;

            if (!alias.TryGetValue("parent", out var parentValue) || parentValue is not string parent)
                return null;

            chain.Add(alias);
            current = parent;
        }
    }

    private static IconifyIconData Resolve(string name, Dictionary<string, object?> icon, List<Dictionary<string, object?>>? parentChain, IconProperties defaults)
    {
        var properties = new IconProperties();
        if (parentChain is null)
        {
            ReadProperties(icon, ref properties, readTransformations: true);
        }
        else
        {
            // The nearest value wins for the box and the visibility, transformations are combined
            foreach (var item in parentChain)
            {
                ReadProperties(item, ref properties, readTransformations: true);
            }
        }

        var body = (string)icon["body"]!;
        return IconifySvgBuilder.ApplyTransformations(
            name,
            body,
            properties.Left ?? defaults.Left ?? 0,
            properties.Top ?? defaults.Top ?? 0,
            properties.Width ?? defaults.Width ?? DefaultSize,
            properties.Height ?? defaults.Height ?? DefaultSize,
            properties.Rotate,
            properties.HFlip,
            properties.VFlip,
            properties.Hidden ?? false);
    }

    private static void ReadProperties(Dictionary<string, object?> value, ref IconProperties properties, bool readTransformations)
    {
        properties.Left ??= GetNumber(value, "left");
        properties.Top ??= GetNumber(value, "top");
        properties.Width ??= GetNumber(value, "width");
        properties.Height ??= GetNumber(value, "height");
        if (!readTransformations)
            return;

        properties.Hidden ??= GetBoolean(value, "hidden");
        if (GetNumber(value, "rotate") is { } rotate)
        {
            properties.Rotate = (properties.Rotate + (int)rotate) % 4;
        }

        if (GetBoolean(value, "hFlip") is true)
        {
            properties.HFlip = !properties.HFlip;
        }

        if (GetBoolean(value, "vFlip") is true)
        {
            properties.VFlip = !properties.VFlip;
        }
    }

    private static IconifyIconSetInfoData? ReadInfo(Dictionary<string, object?> root)
    {
        if (!root.TryGetValue("info", out var infoValue) || infoValue is not Dictionary<string, object?> info)
            return null;

        var result = new IconifyIconSetInfoData
        {
            Name = GetString(info, "name"),
            Version = GetString(info, "version"),
            Category = GetString(info, "category"),
            Palette = GetBoolean(info, "palette"),
        };

        if (GetNumber(info, "total") is { } total)
        {
            result.Total = (int)total;
        }

        if (info.TryGetValue("author", out var authorValue))
        {
            if (authorValue is Dictionary<string, object?> author)
            {
                result.AuthorName = GetString(author, "name");
                result.AuthorUrl = GetString(author, "url");
            }
            else if (authorValue is string authorName)
            {
                result.AuthorName = authorName;
            }
        }

        if (info.TryGetValue("license", out var licenseValue))
        {
            if (licenseValue is Dictionary<string, object?> license)
            {
                result.LicenseTitle = GetString(license, "title");
                result.LicenseSpdx = GetString(license, "spdx");
                result.LicenseUrl = GetString(license, "url");
            }
            else if (licenseValue is string licenseTitle)
            {
                result.LicenseTitle = licenseTitle;
            }
        }

        return result;
    }

    private static string? GetString(Dictionary<string, object?> value, string name) => value.TryGetValue(name, out var result) ? result as string : null;

    private static double? GetNumber(Dictionary<string, object?> value, string name) => value.TryGetValue(name, out var result) && result is double number ? number : null;

    private static bool? GetBoolean(Dictionary<string, object?> value, string name) => value.TryGetValue(name, out var result) && result is bool boolean ? boolean : null;

    private struct IconProperties
    {
        public double? Left;
        public double? Top;
        public double? Width;
        public double? Height;
        public bool? Hidden;
        public int Rotate;
        public bool HFlip;
        public bool VFlip;
    }
}
