using System.Globalization;

namespace Meziantou.Framework;

/// <summary>Compares chunks with the comparison options of a <see cref="TextDiffOptions"/>.</summary>
internal sealed class ChunkComparer : IEqualityComparer<string>
{
    private const CompareOptions SupportedCompareOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreSymbols | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;

    private static readonly ChunkComparer OrdinalTrimming = new(compareInfo: null, CompareOptions.None, StringComparison.Ordinal, trim: true);
    private static readonly ChunkComparer OrdinalIgnoreCaseTrimming = new(compareInfo: null, CompareOptions.None, StringComparison.OrdinalIgnoreCase, trim: true);

    private readonly CompareInfo? _compareInfo;
    private readonly CompareOptions _compareOptions;
    private readonly StringComparison _comparison;
    private readonly bool _trim;

    private ChunkComparer(CompareInfo? compareInfo, CompareOptions compareOptions, StringComparison comparison, bool trim)
    {
        _compareInfo = compareInfo;
        _compareOptions = compareOptions;
        _comparison = comparison;
        _trim = trim;
    }

    internal static bool IsSupported(CompareOptions options) => (options & ~SupportedCompareOptions) is CompareOptions.None;

    internal static IEqualityComparer<string> Create(TextDiffOptions options)
    {
        if (options.Culture is null && options.CompareOptions is CompareOptions.None)
        {
            if (!options.IgnoreWhitespace)
                return options.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

            return options.IgnoreCase ? OrdinalIgnoreCaseTrimming : OrdinalTrimming;
        }

        var compareInfo = (options.Culture ?? CultureInfo.InvariantCulture).CompareInfo;
        var compareOptions = options.IgnoreCase ? options.CompareOptions | CompareOptions.IgnoreCase : options.CompareOptions;
        return new ChunkComparer(compareInfo, compareOptions, StringComparison.Ordinal, options.IgnoreWhitespace);
    }

    public bool Equals(string? x, string? y)
    {
        if (x is null)
            return y is null;

        if (y is null)
            return false;

        var left = _trim ? x.AsSpan().Trim() : x.AsSpan();
        var right = _trim ? y.AsSpan().Trim() : y.AsSpan();
        if (_compareInfo is null)
            return left.Equals(right, _comparison);

        return _compareInfo.Compare(left, right, _compareOptions) is 0;
    }

    public int GetHashCode(string obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var value = _trim ? obj.AsSpan().Trim() : obj.AsSpan();
        if (_compareInfo is null)
            return string.GetHashCode(value, _comparison);

        return _compareInfo.GetHashCode(value, _compareOptions);
    }
}
