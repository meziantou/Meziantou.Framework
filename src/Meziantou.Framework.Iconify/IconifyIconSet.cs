namespace Meziantou.Framework;

/// <summary>
/// Represents an icon set loaded from a file in the IconifyJSON format, using <see cref="Iconify.Load(string)"/>.
/// </summary>
public sealed class IconifyIconSet
{
    private readonly Dictionary<string, IconifyIcon> _icons;

    internal IconifyIconSet(string prefix, IconifyIconSetInfo? info, IReadOnlyList<IconifyIcon> icons)
    {
        Prefix = prefix;
        Info = info;
        Icons = icons;
        _icons = new Dictionary<string, IconifyIcon>(icons.Count, StringComparer.Ordinal);
        foreach (var icon in icons)
        {
            _icons[icon.Name] = icon;
        }
    }

    /// <summary>Gets the prefix of the icon set, such as <c>mdi</c>.</summary>
    public string Prefix { get; }

    /// <summary>Gets the description of the icon set, or <see langword="null"/> when the file has no <c>info</c> property.</summary>
    public IconifyIconSetInfo? Info { get; }

    /// <summary>Gets the icons and the aliases of the icon set, including the hidden icons.</summary>
    public IReadOnlyList<IconifyIcon> Icons { get; }

    /// <summary>Gets the number of icons and aliases in the icon set.</summary>
    public int Count => Icons.Count;

    /// <summary>Gets the icon or the alias with the specified name. The comparison is case-sensitive.</summary>
    /// <param name="name">The name of the icon, such as <c>account-circle</c>.</param>
    /// <exception cref="KeyNotFoundException">The icon set has no icon with this name.</exception>
    public IconifyIcon this[string name]
    {
        get
        {
            if (TryGetIcon(name, out var icon))
                return icon;

            throw new KeyNotFoundException($"The icon set '{Prefix}' has no icon named '{name}'.");
        }
    }

    /// <summary>Gets the icon or the alias with the specified name. The comparison is case-sensitive.</summary>
    /// <param name="name">The name of the icon, such as <c>account-circle</c>.</param>
    /// <param name="icon">The icon, when it exists.</param>
    /// <returns><see langword="true"/> when the icon set has an icon with this name; otherwise, <see langword="false"/>.</returns>
    public bool TryGetIcon(string name, [NotNullWhen(true)] out IconifyIcon? icon)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _icons.TryGetValue(name, out icon);
    }

    /// <summary>Determines whether the icon set has an icon or an alias with the specified name. The comparison is case-sensitive.</summary>
    /// <param name="name">The name of the icon, such as <c>account-circle</c>.</param>
    /// <returns><see langword="true"/> when the icon set has an icon with this name; otherwise, <see langword="false"/>.</returns>
    public bool ContainsIcon(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _icons.ContainsKey(name);
    }
}
