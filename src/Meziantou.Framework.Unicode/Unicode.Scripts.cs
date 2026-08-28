namespace Meziantou.Framework;

public static partial class Unicode
{
    // UTS #39 augments a script set with the composite ISO 15924 codes Hanb, Jpan and Kore.
    // Those are not Unicode Script property values, so they get bit positions above the enum.
    private const int HanWithBopomofoBit = UnicodeScripts.ScriptCount;
    private const int JapaneseBit = UnicodeScripts.ScriptCount + 1;
    private const int KoreanBit = UnicodeScripts.ScriptCount + 2;
    private const int ScriptWordCount = ((UnicodeScripts.ScriptCount + 3) + 63) / 64;

    /// <summary>Determines whether a string is written in a single script, as defined by Unicode Technical Standard #39.</summary>
    /// <param name="value">The text to inspect.</param>
    /// <returns><see langword="true"/> when the resolved script set is not empty; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// A string is single-script when the augmented <c>Script_Extensions</c> sets of all its
    /// characters have a script in common. Characters whose script is <c>Common</c> or
    /// <c>Inherited</c> — digits, punctuation, combining marks — match any script and never make a
    /// string mixed on their own.
    /// <para>
    /// The empty string is single-script. Han, Hiragana, Katakana, Hangul and Bopomofo are augmented
    /// so that Japanese and Korean text, which legitimately mixes scripts, is not reported as mixed.
    /// </para>
    /// </remarks>
    /// <seealso href="https://unicode.org/reports/tr39/" />
    public static bool IsSingleScript(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        Span<ulong> resolved = stackalloc ulong[ScriptWordCount];
        Span<ulong> current = stackalloc ulong[ScriptWordCount];
        resolved.Fill(ulong.MaxValue);

        foreach (var rune in value.EnumerateRunes())
        {
            current.Clear();
            if (!TryGetAugmentedScriptSet(rune, current))
                continue;

            var empty = true;
            for (var i = 0; i < resolved.Length; i++)
            {
                resolved[i] &= current[i];
                if (resolved[i] != 0)
                {
                    empty = false;
                }
            }

            if (empty)
                return false;
        }

        return true;
    }

    /// <summary>Determines whether a string mixes scripts in a way that Unicode Technical Standard #39 considers suspicious.</summary>
    /// <param name="value">The text to inspect.</param>
    /// <returns><see langword="true"/> when the string is not single-script; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// Mixed-script text is the signal that distinguishes a homograph attack from two unrelated
    /// words: <c>paypal</c> spelled with Cyrillic look-alikes is mixed-script, while ordinary text
    /// in any one writing system is not.
    /// </remarks>
    /// <seealso href="https://unicode.org/reports/tr39/" />
    public static bool IsMixedScript(string value) => !IsSingleScript(value);

    /// <summary>Builds the augmented script set of a rune, or reports that it matches any script.</summary>
    /// <returns><see langword="false"/> when the rune is script-neutral and should be skipped.</returns>
    private static bool TryGetAugmentedScriptSet(Rune rune, Span<ulong> destination)
    {
        var raw = UnicodeScripts.GetScriptExtensionsRaw(rune.Value);
        if (raw.IsEmpty)
        {
            var script = UnicodeScripts.GetScript(rune);
            if (script is UnicodeScript.Common or UnicodeScript.Inherited or UnicodeScript.Unknown)
                return false;

            SetScript(destination, (int)script);
        }
        else
        {
            for (var i = 0; i < raw.Length; i += 2)
            {
                var id = raw[i] | (raw[i + 1] << 8);
                if (id == (int)UnicodeScript.Common || id == (int)UnicodeScript.Inherited)
                    return false;

                SetScript(destination, id);
            }
        }

        Augment(destination);
        return true;
    }

    private static void Augment(Span<ulong> set)
    {
        if (HasScript(set, (int)UnicodeScript.Han))
        {
            SetScript(set, HanWithBopomofoBit);
            SetScript(set, JapaneseBit);
            SetScript(set, KoreanBit);
        }

        if (HasScript(set, (int)UnicodeScript.Hiragana) || HasScript(set, (int)UnicodeScript.Katakana))
        {
            SetScript(set, JapaneseBit);
        }

        if (HasScript(set, (int)UnicodeScript.Hangul))
        {
            SetScript(set, KoreanBit);
        }

        if (HasScript(set, (int)UnicodeScript.Bopomofo))
        {
            SetScript(set, HanWithBopomofoBit);
        }
    }

    private static void SetScript(Span<ulong> set, int scriptId) => set[scriptId >> 6] |= 1UL << (scriptId & 63);

    private static bool HasScript(ReadOnlySpan<ulong> set, int scriptId) => (set[scriptId >> 6] & (1UL << (scriptId & 63))) != 0;
}
