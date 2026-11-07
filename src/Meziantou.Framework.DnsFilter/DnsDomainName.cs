using System.Globalization;

namespace Meziantou.Framework.DnsFilter;

/// <summary>
/// The single definition of how a domain name is normalized, used for both rule patterns and
/// queried names so the two can never drift apart.
/// </summary>
internal static class DnsDomainName
{
    private const int MaxLabelLength = 63;
    private const int MaxNameLength = 253;

    private static readonly IdnMapping IdnMapping = new() { AllowUnassigned = true, UseStd3AsciiRules = false };

    /// <summary>
    /// Trims, strips the root label, lowercases, and converts internationalized names to their
    /// A-label (punycode) form, which is what DNS actually carries on the wire.
    /// </summary>
    /// <returns><see langword="false"/> when the value is not usable as a domain name.</returns>
    public static bool TryNormalize(string value, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;

        var trimmed = value.AsSpan().Trim().TrimEnd('.');
        if (trimmed.IsEmpty)
            return false;

        var candidate = trimmed.ToString();
        if (!Ascii.IsValid(candidate))
        {
            try
            {
                candidate = IdnMapping.GetAscii(candidate);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        candidate = candidate.ToLowerInvariant();
        if (!IsValid(candidate))
            return false;

        normalized = candidate;
        return true;
    }

    /// <summary>
    /// Checks that a normalized value is shaped like a domain name. This is deliberately permissive
    /// about characters real blocklists use (underscores, digits) and strict about the ones that
    /// indicate the value is not a domain at all — most importantly whitespace, which is what a
    /// mis-detected hosts line (<c>0.0.0.0 ads.example.com</c>) would carry.
    /// </summary>
    private static bool IsValid(string value)
    {
        if (value.Length is 0 or > MaxNameLength)
            return false;

        var labelLength = 0;
        foreach (var c in value)
        {
            if (c is '.')
            {
                if (labelLength is 0)
                    return false;

                labelLength = 0;
                continue;
            }

            if (!IsAllowedInLabel(c))
                return false;

            if (++labelLength > MaxLabelLength)
                return false;
        }

        return labelLength is not 0;
    }

    private static bool IsAllowedInLabel(char c)
    {
        return char.IsAsciiLetterOrDigit(c) || c is '-' or '_';
    }
}
