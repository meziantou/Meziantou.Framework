using System.Globalization;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

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
