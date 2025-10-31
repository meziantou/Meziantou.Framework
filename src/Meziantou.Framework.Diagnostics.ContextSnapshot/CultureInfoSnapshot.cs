namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of culture information at a specific point in time.</summary>
public sealed class CultureInfoSnapshot
{
    internal static CultureInfoSnapshot? Get(CultureInfo? culture) => culture is null ? null : new CultureInfoSnapshot(culture);

    internal CultureInfoSnapshot(CultureInfo culture)
    {
        Name = culture.Name;
        IsReadOnly = culture.IsReadOnly;
    }

    /// <summary>Gets the name of the culture.</summary>
    public string Name { get; }
    /// <summary>Gets a value indicating whether the culture is read-only.</summary>
    public bool IsReadOnly { get; }
}
