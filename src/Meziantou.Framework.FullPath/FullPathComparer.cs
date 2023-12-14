using System.Runtime.Versioning;

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

    // You can configure case-sensitivity per folder or system-wide on Windows, so the comparer is not the perfect way to compare path.
    // However, this is an edge case so this class won't support it at the moment.
    private static FullPathComparer GetDefaultComparer() => GetComparer(ignoreCase: IsWindows());

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

    [SupportedOSPlatformGuard("windows")]
    private static bool IsWindows()
    {
#if NET5_0_OR_GREATER
        return OperatingSystem.IsWindows();
#elif NETCOREAPP3_1 || NETSTANDARD2_0
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#elif NET472
        return Environment.OSVersion.Platform == PlatformID.Win32NT;
#else
#error Platform notsupported
#endif
    }
}
