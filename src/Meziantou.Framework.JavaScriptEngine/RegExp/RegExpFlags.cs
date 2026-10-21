namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The flags of a regular expression (https://tc39.es/ecma262/#sec-regexpinitialize).</summary>
[Flags]
internal enum RegExpFlags
{
    None = 0,

    /// <summary><c>d</c>: the match result has an <c>indices</c> property.</summary>
    HasIndices = 1 << 0,

    /// <summary><c>g</c></summary>
    Global = 1 << 1,

    /// <summary><c>i</c></summary>
    IgnoreCase = 1 << 2,

    /// <summary><c>m</c></summary>
    Multiline = 1 << 3,

    /// <summary><c>s</c></summary>
    DotAll = 1 << 4,

    /// <summary><c>u</c></summary>
    Unicode = 1 << 5,

    /// <summary><c>v</c></summary>
    UnicodeSets = 1 << 6,

    /// <summary><c>y</c></summary>
    Sticky = 1 << 7,
}

internal static class RegExpFlagsExtensions
{
    /// <summary>Parses the flags of RegExpInitialize: only "dgimsuvy", each at most once, and not both "u" and "v".</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out RegExpFlags flags)
    {
        flags = RegExpFlags.None;
        foreach (var c in text)
        {
            var flag = FromChar(c);
            if (flag == RegExpFlags.None || (flags & flag) != 0)
                return false;

            flags |= flag;
        }

        return (flags & (RegExpFlags.Unicode | RegExpFlags.UnicodeSets)) != (RegExpFlags.Unicode | RegExpFlags.UnicodeSets);
    }

    public static RegExpFlags FromChar(char c)
    {
        return c switch
        {
            'd' => RegExpFlags.HasIndices,
            'g' => RegExpFlags.Global,
            'i' => RegExpFlags.IgnoreCase,
            'm' => RegExpFlags.Multiline,
            's' => RegExpFlags.DotAll,
            'u' => RegExpFlags.Unicode,
            'v' => RegExpFlags.UnicodeSets,
            'y' => RegExpFlags.Sticky,
            _ => RegExpFlags.None,
        };
    }

    /// <summary>Gets whether the "u" or the "v" flag is set (HasEitherUnicodeFlag).</summary>
    public static bool HasEitherUnicodeFlag(this RegExpFlags flags) => (flags & (RegExpFlags.Unicode | RegExpFlags.UnicodeSets)) != 0;
}
