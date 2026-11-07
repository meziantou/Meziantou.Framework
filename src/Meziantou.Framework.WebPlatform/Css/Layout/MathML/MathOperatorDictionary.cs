namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The boolean properties of an operator (https://w3c.github.io/mathml-core/#dictionary-based-attributes).</summary>
[Flags]
internal enum MathOperatorFlags : byte
{
    None = 0,
    Stretchy = 1,
    Symmetric = 2,
    LargeOp = 4,
    MovableLimits = 8,
}

/// <summary>The form of an embellished operator (https://w3c.github.io/mathml-core/#dfn-form).</summary>
internal enum MathOperatorForm : byte
{
    Infix,
    Prefix,
    Postfix,
}

/// <summary>
/// The operator dictionary of MathML Core (https://w3c.github.io/mathml-core/#operator-dictionary): the category of an
/// operator from its content and its form, and the properties of the categories. The tables are generated from the
/// specification (<c>MathOperatorDictionary.g.cs</c>).
/// </summary>
internal static partial class MathOperatorDictionary
{
    /// <summary>The lspace of a category, in em.</summary>
    public static double GetLSpace(MathOperatorCategory category) => CategoryLSpace[(int)category];

    /// <summary>The rspace of a category, in em.</summary>
    public static double GetRSpace(MathOperatorCategory category) => CategoryRSpace[(int)category];

    /// <summary>The boolean properties of a category.</summary>
    public static MathOperatorFlags GetFlags(MathOperatorCategory category) => (MathOperatorFlags)CategoryFlags[(int)category];

    /// <summary>
    /// The algorithm to determine the category of an operator (https://w3c.github.io/mathml-core/#dfn-algorithm-to-determine-the-category-of-an-operator).
    /// </summary>
    public static MathOperatorCategory GetCategory(ReadOnlySpan<char> content, MathOperatorForm form)
    {
        // "If Content as an UTF-16 string does not have length or 1 or 2 then exit with category Default"
        int character;
        if (content.Length == 1)
        {
            character = content[0];
            if (character is >= 0x0320 and <= 0x03FF)
                return MathOperatorCategory.Default;
        }
        else if (content.Length == 2)
        {
            if (char.IsSurrogatePair(content[0], content[1]))
            {
                // U+1EEF0 ARABIC MATHEMATICAL OPERATOR MEEM WITH HAH WITH TATWEEL, U+1EEF1 ARABIC MATHEMATICAL OPERATOR HAH WITH DAL
                var codePoint = char.ConvertToUtf32(content[0], content[1]);
                return codePoint is 0x1EEF0 or 0x1EEF1 && form == MathOperatorForm.Postfix ? MathOperatorCategory.I : MathOperatorCategory.Default;
            }

            if (content[1] is '̸' or '⃒')
            {
                character = content[0];
            }
            else
            {
                var index = Array.IndexOf(TwoAsciiChars, content.ToString());
                if (index < 0)
                    return MathOperatorCategory.Default;

                character = 0x0320 + index;
            }
        }
        else
        {
            return MathOperatorCategory.Default;
        }

        if (form == MathOperatorForm.Infix && character is 0x007C or 0x223C)
            return MathOperatorCategory.ForceDefault;

        // The ranges are sorted by (form, first character): binary search of the last range that starts at or before the key
        var key = ((int)form << 20) | character;
        var starts = KeyRangeStarts;
        var low = 0;
        var high = starts.Length - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            if (starts[middle] <= key)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found >= 0 && key - starts[found] <= KeyRangeLengths[found] ? (MathOperatorCategory)KeyRangeCategories[found] : MathOperatorCategory.Default;
    }

    /// <summary>Whether the intrinsic stretch axis of a character is inline (https://w3c.github.io/mathml-core/#dfn-intrinsic-stretch-axis).</summary>
    public static bool HasInlineStretchAxis(int codePoint) => InlineStretchAxisCharacters.BinarySearch(codePoint) >= 0;
}

/// <summary>The italic mappings of <c>text-transform: math-auto</c> (https://w3c.github.io/mathml-core/#the-math-auto-transform).</summary>
internal static partial class MathItalicMapping
{
    /// <summary>Returns the italic character of a character, or the character itself when it has no italic mapping.</summary>
    public static int GetItalic(int codePoint)
    {
        var index = Originals.BinarySearch(codePoint);
        return index >= 0 ? Italics[index] : codePoint;
    }
}
