namespace Meziantou.Framework.Language.Ini.Internals;

/// <summary>
/// Compares names the way git does: the section name, up to the first <c>.</c>, ignoring case, and the subsection after
/// it, as in <c>remote.origin</c>, with case. A key holds no <c>.</c>, so it is compared ignoring case.
/// </summary>
internal sealed class GitNameComparer : StringComparer
{
    public static GitNameComparer Instance { get; } = new();

    private GitNameComparer()
    {
    }

    public override int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
            return 0;

        if (x is null)
            return -1;

        if (y is null)
            return 1;

        var xSplit = GetSubsectionStart(x);
        var ySplit = GetSubsectionStart(y);
        var result = x.AsSpan(0, xSplit).CompareTo(y.AsSpan(0, ySplit), StringComparison.OrdinalIgnoreCase);
        if (result != 0)
            return result;

        return x.AsSpan(xSplit).CompareTo(y.AsSpan(ySplit), StringComparison.Ordinal);
    }

    public override bool Equals(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
            return true;

        if (x is null || y is null)
            return false;

        var xSplit = GetSubsectionStart(x);
        var ySplit = GetSubsectionStart(y);
        return x.AsSpan(0, xSplit).Equals(y.AsSpan(0, ySplit), StringComparison.OrdinalIgnoreCase)
            && x.AsSpan(xSplit).SequenceEqual(y.AsSpan(ySplit));
    }

    public override int GetHashCode(string obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        var split = GetSubsectionStart(obj);
        return HashCode.Combine(string.GetHashCode(obj.AsSpan(0, split), StringComparison.OrdinalIgnoreCase), string.GetHashCode(obj.AsSpan(split), StringComparison.Ordinal));
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is GitNameComparer;

    public override int GetHashCode() => typeof(GitNameComparer).GetHashCode();

    /// <summary>Gets where the subsection starts: at the first <c>.</c>, which it keeps so that <c>name</c> and <c>name.</c> differ.</summary>
    private static int GetSubsectionStart(string name)
    {
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? name.Length : dot;
    }
}
