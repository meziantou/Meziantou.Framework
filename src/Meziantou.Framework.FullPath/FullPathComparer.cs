using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

public sealed class FullPathComparer : IComparer<FullPath>, IEqualityComparer<FullPath>
{
    public static FullPathComparer Default { get; }
    public static FullPathComparer CaseSensitive { get; } = new FullPathComparer(caseSensitive: true);
    public static FullPathComparer CaseInsensitive { get; } = new FullPathComparer(caseSensitive: false);

    static FullPathComparer()
    {
        Default = GetDefaultComparer();
    }

    // You can configure case-sensitivity per forlder or system-wide on Windows, so the comparer is not the perfect way to compare path.
    // However, this is an edge case so this class won't support it at the moment.
    private static FullPathComparer GetDefaultComparer() => GetComparer(ignoreCase: RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

    internal static FullPathComparer GetComparer(bool ignoreCase) => ignoreCase ? CaseInsensitive : CaseSensitive;

    private readonly StringComparer _stringComparer;

    private FullPathComparer(bool caseSensitive)
    {
        _stringComparer = caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        IsCaseSensitive = caseSensitive;
    }

    public bool IsCaseSensitive { get; }

    public int Compare(FullPath x, FullPath y) => _stringComparer.Compare(x._value, y._value);

    public bool Equals(FullPath x, FullPath y) => _stringComparer.Equals(x._value, y._value);

    public int GetHashCode(FullPath obj)
    {
        if (obj._value is null)
            return 0;

        return _stringComparer.GetHashCode(obj._value);
    }
}
