using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework;

/// <summary>Provides utilities for working with countries and region information.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Tests/CountryTests.cs" ref:region="Country_GetUnicodeFlag" ref:dedent="true">
/// var region = new RegionInfo("US");
/// string flag = Country.GetUnicodeFlag(region);
/// Assert.Equal("\U0001F1FA\U0001F1F8", flag); // US flag emoji
///
/// string flag2 = Country.GetUnicodeFlag("FR");
/// Assert.Equal("\U0001F1EB\U0001F1F7", flag2); // FR flag emoji
/// </code>
/// </example>
public static class Country
{
    /// <summary>Gets the Unicode flag emoji for the specified region.</summary>
    /// <exception cref="ArgumentException">The two-letter ISO region name of <paramref name="region"/> is not made of two ASCII letters, such as <c>419</c> for <c>es-419</c>.</exception>
    public static string GetUnicodeFlag(RegionInfo region)
    {
        ArgumentNullException.ThrowIfNull(region);

        if (!TryCreateFlag(region.TwoLetterISORegionName, out var result))
            throw new ArgumentException($"The region '{region.Name}' does not have a two-letter ISO region code made of ASCII letters.", nameof(region));

        return result;
    }

    /// <summary>Gets the Unicode flag emoji for the specified two-letter ISO region code.</summary>
    /// <exception cref="ArgumentException"><paramref name="twoLetterISORegionName"/> is not made of two ASCII letters.</exception>
    public static string GetUnicodeFlag(string twoLetterISORegionName)
    {
        ArgumentNullException.ThrowIfNull(twoLetterISORegionName);

        if (!TryCreateFlag(twoLetterISORegionName, out var result))
            throw new ArgumentException("The value must be a two-letter ISO region code made of ASCII letters.", nameof(twoLetterISORegionName));

        return result;
    }

    /// <summary>Tries to get the Unicode flag emoji for the specified region.</summary>
    /// <returns><see langword="true"/> if <paramref name="region"/> is not <see langword="null"/> and its two-letter ISO region name is made of two ASCII letters; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetUnicodeFlag([NotNullWhen(true)] RegionInfo? region, [NotNullWhen(true)] out string? flag)
    {
        return TryCreateFlag(region?.TwoLetterISORegionName, out flag);
    }

    /// <summary>Tries to get the Unicode flag emoji for the specified two-letter ISO region code.</summary>
    /// <returns><see langword="true"/> if <paramref name="twoLetterISORegionName"/> is made of two ASCII letters; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetUnicodeFlag([NotNullWhen(true)] string? twoLetterISORegionName, [NotNullWhen(true)] out string? flag)
    {
        return TryCreateFlag(twoLetterISORegionName, out flag);
    }

    private static bool TryCreateFlag([NotNullWhen(true)] string? twoLetterISORegionName,[NotNullWhen(true)] out string? flag)
    {
        if (twoLetterISORegionName is not { Length: 2 } || !char.IsAsciiLetter(twoLetterISORegionName[0]) || !char.IsAsciiLetter(twoLetterISORegionName[1]))
        {
            flag = null;
            return false;
        }

        // RegionalIndicatorSymbolLetterA is always a surrogate, so the flag is made of 4 UTF-16 code units
        flag = string.Create(length: 4, state: (ToRegionalIndicator(twoLetterISORegionName[0]), ToRegionalIndicator(twoLetterISORegionName[1])), static (span, state) =>
        {
            state.Item1.EncodeToUtf16(span);
            state.Item2.EncodeToUtf16(span[2..]);
        });
        return true;
    }

    private static Rune ToRegionalIndicator(char asciiLetter)
    {
        const int RegionalIndicatorSymbolLetterA = 0x1F1E6; // https://tools.meziantou.net/char-info?Text=%5CU1F1E6

        // Clearing bit 0x20 converts an ASCII lowercase letter to uppercase
        return new Rune(RegionalIndicatorSymbolLetterA + ((asciiLetter & ~0x20) - 'A'));
    }
}
