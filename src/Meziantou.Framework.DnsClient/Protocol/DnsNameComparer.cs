namespace Meziantou.Framework.DnsClient.Protocol;

/// <summary>
/// Comparison of domain names in presentation format, done on the underlying wire octets.
/// </summary>
/// <remarks>
/// Names are compared octet-wise after downcasing A-Z, per RFC 4034 section 6.1 and RFC 4343. A string comparison
/// cannot stand in for this: <see cref="StringComparison.OrdinalIgnoreCase"/> folds to uppercase and therefore orders
/// every character between <c>Z</c> (0x5A) and <c>a</c> (0x61) — most importantly <c>_</c> (0x5F), used by
/// <c>_dmarc</c>, <c>_domainkey</c>, <c>_tcp</c> and DANE names — on the wrong side of the letters.
/// </remarks>
internal static class DnsNameComparer
{
    /// <summary>Determines whether two domain names are equal, ignoring ASCII case and a trailing dot.</summary>
    public static bool Equals(string? left, string? right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left is null || right is null)
            return false;

        return Compare(left, right) is 0;
    }

    /// <summary>Compares two domain names in DNSSEC canonical order (RFC 4034 section 6.1): label by label, from the rightmost.</summary>
    public static int Compare(string left, string right)
    {
        var leftLabels = SplitLabels(left);
        var rightLabels = SplitLabels(right);

        var leftIndex = leftLabels.Length - 1;
        var rightIndex = rightLabels.Length - 1;

        while (leftIndex >= 0 && rightIndex >= 0)
        {
            var comparison = CompareLabels(leftLabels[leftIndex], rightLabels[rightIndex]);
            if (comparison != 0)
                return comparison;

            leftIndex--;
            rightIndex--;
        }

        if (leftIndex == rightIndex)
            return 0;

        return leftIndex < rightIndex ? -1 : 1;
    }

    /// <summary>Compares two labels in presentation format as downcased wire octets.</summary>
    public static int CompareLabels(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        Span<byte> leftBytes = stackalloc byte[DnsName.MaxLabelLength];
        Span<byte> rightBytes = stackalloc byte[DnsName.MaxLabelLength];

        var leftLength = DnsName.DecodeLabel(left, leftBytes);
        var rightLength = DnsName.DecodeLabel(right, rightBytes);

        ToLowerAscii(leftBytes[..leftLength]);
        ToLowerAscii(rightBytes[..rightLength]);

        return leftBytes[..leftLength].SequenceCompareTo(rightBytes[..rightLength]);
    }

    /// <summary>Splits a name into its labels, ignoring escaped dots and a trailing dot.</summary>
    public static string[] SplitLabels(string name)
    {
        var span = name.AsSpan();
        if (DnsName.EndsWithUnescapedDot(span))
        {
            span = span[..^1];
        }

        if (span.IsEmpty)
            return [];

        var count = 0;
        foreach (var _ in DnsName.EnumerateLabels(span))
        {
            count++;
        }

        var labels = new string[count];
        var index = 0;
        foreach (var label in DnsName.EnumerateLabels(span))
        {
            labels[index++] = label.ToString();
        }

        return labels;
    }

    private static void ToLowerAscii(Span<byte> value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] is >= (byte)'A' and <= (byte)'Z')
            {
                value[i] += 'a' - 'A';
            }
        }
    }
}
