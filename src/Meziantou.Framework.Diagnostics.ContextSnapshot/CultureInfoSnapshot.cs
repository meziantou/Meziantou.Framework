namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of culture information including the culture name and read-only status.</summary>
public sealed class CultureInfoSnapshot
{
    internal static CultureInfoSnapshot? Get(CultureInfo? culture) => culture is null ? null : new CultureInfoSnapshot(culture);

    internal CultureInfoSnapshot(CultureInfo culture)
    {
        Name = culture.Name;
        IsReadOnly = culture.IsReadOnly;
    }

    public string Name { get; }
    public bool IsReadOnly { get; }
}
