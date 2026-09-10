namespace Meziantou.Framework.Tds.Protocol;

/// <summary>
/// The single collation the server advertises, SQL_Latin1_General_CP1_CI_AS. Clients encode every non-Unicode
/// character value with its code page, so the server has to decode them with the same one.
/// </summary>
internal static class TdsCollation
{
    /// <summary>The COLLATION structure sent in the login response and in every character column.</summary>
    public static ReadOnlySpan<byte> Default => [0x09, 0x04, 0xD0, 0x00, 0x34];

    /// <summary>
    /// Code page 1252 matches ISO-8859-1 outside 0x80-0x9F, so only that range needs a table. The five
    /// positions the code page leaves undefined map to the matching control character, which is what code
    /// page 1252 does on Windows as well.
    /// </summary>
    private static ReadOnlySpan<char> CodePage1252HighRange =>
        "€\u0081‚ƒ„…†‡ˆ‰Š‹Œ\u008DŽ\u008F" +
        "\u0090‘’“”•–—˜™š›œ\u009DžŸ";

    /// <summary>Decodes a value a client sent for a non-Unicode character type.</summary>
    public static string GetString(ReadOnlySpan<byte> value)
    {
        var characters = new char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            characters[i] = current is >= 0x80 and <= 0x9F ? CodePage1252HighRange[current - 0x80] : (char)current;
        }

        return new string(characters);
    }
}
