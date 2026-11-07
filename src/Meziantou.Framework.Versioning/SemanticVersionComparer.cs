namespace Meziantou.Framework.Versioning;

internal sealed class SemanticVersionComparer : IComparer<SemanticVersion>, IEqualityComparer<SemanticVersion>
{
    public static SemanticVersionComparer Instance { get; } = new SemanticVersionComparer();

    public int Compare(SemanticVersion? x, SemanticVersion? y)
    {
        if (ReferenceEquals(x, y))
            return 0;

        if (x == null)
            return -1;

        if (y == null)
            return 1;

        var result = x.Major.CompareTo(y.Major);
        if (result != 0)
            return result;

        result = x.Minor.CompareTo(y.Minor);
        if (result != 0)
            return result;

        result = x.Patch.CompareTo(y.Patch);
        if (result != 0)
            return result;

        if (!x.IsPrerelease && !y.IsPrerelease)
            return 0;

        if (x.IsPrerelease && !y.IsPrerelease)
            return -1;

        if (!x.IsPrerelease && y.IsPrerelease)
            return 1;

        for (var i = 0; i < x.PrereleaseLabels.Count && i < y.PrereleaseLabels.Count; i++)
        {
            var left = x.PrereleaseLabels[i];
            var right = y.PrereleaseLabels[i];

            var isLeftNumber = IsNumericIdentifier(left);
            var isRightNumber = IsNumericIdentifier(right);

            if (isLeftNumber && isRightNumber)
            {
                // Numeric identifiers cannot carry a leading zero, so the one with more digits is
                // the larger number and identifiers of equal length compare correctly as text.
                // Comparing this way is exact for identifiers of any length, whereas parsing to a
                // fixed-width integer is not.
                result = left.Length.CompareTo(right.Length);
                if (result != 0)
                    return result;

                result = StringComparer.Ordinal.Compare(left, right);
                if (result != 0)
                    return result;
            }
            else
            {
                if (isLeftNumber)
                    return -1;

                if (isRightNumber)
                    return 1;

                result = StringComparer.Ordinal.Compare(left, right);
                if (result != 0)
                    return result;
            }
        }

        if (x.PrereleaseLabels.Count > y.PrereleaseLabels.Count)
            return 1;

        if (x.PrereleaseLabels.Count < y.PrereleaseLabels.Count)
            return -1;

        return 0;
    }

    private static bool IsNumericIdentifier(string identifier)
    {
        return identifier.Length > 0 && !identifier.AsSpan().ContainsAnyExceptInRange('0', '9');
    }

    public bool Equals(SemanticVersion? x, SemanticVersion? y)
    {
        return Compare(x, y) == 0;
    }

    public int GetHashCode(SemanticVersion? obj) => obj is null ? 0 : HashCode.Combine(obj.Major, obj.Minor, obj.Patch, obj.PrereleaseLabels.Count);
}
