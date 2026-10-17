namespace Meziantou.Framework;

public static partial class UnicodeScripts
{
    /// <summary>Gets the script of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The script, or <see cref="UnicodeScript.Unknown"/> when the code point has no assigned script.</returns>
    public static UnicodeScript GetScript(int codePoint) => (UnicodeScript)GetScriptValue(codePoint);

    /// <summary>Gets the script of a rune.</summary>
    /// <param name="rune">The rune.</param>
    /// <returns>The script, or <see cref="UnicodeScript.Unknown"/> when the rune has no assigned script.</returns>
    public static UnicodeScript GetScript(Rune rune) => GetScript(rune.Value);

    /// <summary>Gets the greatest number of scripts <see cref="GetScriptExtensions(int, Span{UnicodeScript})"/> writes for a code point.</summary>
    public static int MaxScriptExtensionCount => MaxExtensionCount;

    /// <summary>Gets the Script_Extensions of a code point: the scripts it is used with.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <param name="destination">The buffer that receives the scripts, in increasing numeric value.</param>
    /// <returns>The number of scripts written. It is 1, the script of <see cref="GetScript(int)"/>, for a code point that has no explicit Script_Extensions value.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small. <see cref="MaxScriptExtensionCount"/> elements are always enough.</exception>
    /// <seealso href="https://www.unicode.org/reports/tr24/" />
    public static int GetScriptExtensions(int codePoint, Span<UnicodeScript> destination)
    {
        var raw = GetScriptExtensionsRaw(codePoint);
        if (raw.IsEmpty)
        {
            if (destination.IsEmpty)
                throw new ArgumentException("The destination is too small", nameof(destination));

            destination[0] = GetScript(codePoint);
            return 1;
        }

        var count = raw.Length / 2;
        if (destination.Length < count)
            throw new ArgumentException("The destination is too small", nameof(destination));

        for (var i = 0; i < count; i++)
        {
            destination[i] = (UnicodeScript)(raw[i * 2] | (raw[(i * 2) + 1] << 8));
        }

        return count;
    }

    /// <summary>Determines whether a script is one of the Script_Extensions of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <param name="script">The script.</param>
    /// <returns><see langword="true"/> when the code point is used with the script.</returns>
    public static bool HasScriptExtension(int codePoint, UnicodeScript script)
    {
        var raw = GetScriptExtensionsRaw(codePoint);
        if (raw.IsEmpty)
            return GetScript(codePoint) == script;

        for (var i = 0; i < raw.Length; i += 2)
        {
            if ((raw[i] | (raw[i + 1] << 8)) == (int)script)
                return true;
        }

        return false;
    }

    /// <summary>Gets the four-letter ISO 15924 code of a script, such as <c>Latn</c>.</summary>
    /// <param name="script">The script.</param>
    /// <returns>The code; <c>Zzzz</c> for <see cref="UnicodeScript.Unknown"/> and for a value that is not defined.</returns>
    public static string GetIso15924Code(UnicodeScript script) => GetIso15924CodeCore(script) ?? "Zzzz";
}
