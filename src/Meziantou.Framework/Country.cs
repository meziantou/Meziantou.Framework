namespace Meziantou.Framework;

/// <summary>
/// Provides utilities for working with countries and region information.
/// </summary>
/// <example>
/// <code>
/// var region = new RegionInfo("US");
/// string flag = Country.GetUnicodeFlag(region); // Returns "🇺🇸"
/// string flag2 = Country.GetUnicodeFlag("FR"); // Returns "🇫🇷"
/// </code>
/// </example>
public static class Country
{
    /// <summary>Gets the Unicode flag emoji for the specified region.</summary>
    public static string GetUnicodeFlag(RegionInfo region)
    {
        ArgumentNullException.ThrowIfNull(region);

        return GetUnicodeFlag(region.TwoLetterISORegionName);
    }

    /// <summary>Gets the Unicode flag emoji for the specified two-letter ISO region code.</summary>
    public static string GetUnicodeFlag(string twoLetterISORegionName)
    {
        ArgumentNullException.ThrowIfNull(twoLetterISORegionName);

        const int RegionalIndicatorSymbolLetterA = 0x1F1E6; // https://tools.meziantou.net/char-info?Text=%5CU1F1E6
        const int LatinCapitalLetterA = 0x0041;             // https://tools.meziantou.net/char-info?Text=A
        const int LatinSmallLetterA = 0x0061;               // https://tools.meziantou.net/char-info?Text=a

        const int UppercaseDiff = RegionalIndicatorSymbolLetterA - LatinCapitalLetterA;
        const int LowercaseDiff = RegionalIndicatorSymbolLetterA - LatinSmallLetterA;

        Rune rune1;
        Rune rune2;

        if (twoLetterISORegionName[0] is >= 'a' and <= 'z')
        {
            rune1 = new Rune(twoLetterISORegionName[0] + LowercaseDiff);
        }
        else
        {
            rune1 = new Rune(twoLetterISORegionName[0] + UppercaseDiff);
        }

        if (twoLetterISORegionName[1] is >= 'a' and <= 'z')
        {
            rune2 = new Rune(twoLetterISORegionName[1] + LowercaseDiff);
        }
        else
        {
            rune2 = new Rune(twoLetterISORegionName[1] + UppercaseDiff);
        }

        // RegionalIndicatorSymbolLetterA is always a surrogate, so we can assume the length is 4
        return string.Create(length: 4, state: (rune1, rune2), (span, state) =>
        {
            rune1.EncodeToUtf16(span);
            rune2.EncodeToUtf16(span[2..]);
        });
    }
}
