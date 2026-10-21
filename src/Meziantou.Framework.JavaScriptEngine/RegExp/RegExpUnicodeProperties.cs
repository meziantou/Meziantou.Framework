using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The sets of the property escapes <c>\p{...}</c> (https://tc39.es/ecma262/#sec-compiletocharset): the names of the
/// tables of the specification, compared exactly, and the code points of the Unicode Character Database.
/// </summary>
/// <remarks>The returned sets are shared: they must not be modified.</remarks>
internal static class RegExpUnicodeProperties
{
    private const int SpecialAny = -1;
    private const int SpecialAscii = -2;
    private const int SpecialAssigned = -3;
    private const int GroupKeyBase = 0x40000000;

    // Table 66: the binary properties and their aliases
    private static readonly FrozenDictionary<string, int> BinaryProperties = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["ASCII"] = SpecialAscii,
        ["ASCII_Hex_Digit"] = (int)UnicodeBinaryProperty.ASCIIHexDigit,
        ["AHex"] = (int)UnicodeBinaryProperty.ASCIIHexDigit,
        ["Alphabetic"] = (int)UnicodeBinaryProperty.Alphabetic,
        ["Alpha"] = (int)UnicodeBinaryProperty.Alphabetic,
        ["Any"] = SpecialAny,
        ["Assigned"] = SpecialAssigned,
        ["Bidi_Control"] = (int)UnicodeBinaryProperty.BidiControl,
        ["Bidi_C"] = (int)UnicodeBinaryProperty.BidiControl,
        ["Bidi_Mirrored"] = (int)UnicodeBinaryProperty.BidiMirrored,
        ["Bidi_M"] = (int)UnicodeBinaryProperty.BidiMirrored,
        ["Case_Ignorable"] = (int)UnicodeBinaryProperty.CaseIgnorable,
        ["CI"] = (int)UnicodeBinaryProperty.CaseIgnorable,
        ["Cased"] = (int)UnicodeBinaryProperty.Cased,
        ["Changes_When_Casefolded"] = (int)UnicodeBinaryProperty.ChangesWhenCasefolded,
        ["CWCF"] = (int)UnicodeBinaryProperty.ChangesWhenCasefolded,
        ["Changes_When_Casemapped"] = (int)UnicodeBinaryProperty.ChangesWhenCasemapped,
        ["CWCM"] = (int)UnicodeBinaryProperty.ChangesWhenCasemapped,
        ["Changes_When_Lowercased"] = (int)UnicodeBinaryProperty.ChangesWhenLowercased,
        ["CWL"] = (int)UnicodeBinaryProperty.ChangesWhenLowercased,
        ["Changes_When_NFKC_Casefolded"] = (int)UnicodeBinaryProperty.ChangesWhenNFKCCasefolded,
        ["CWKCF"] = (int)UnicodeBinaryProperty.ChangesWhenNFKCCasefolded,
        ["Changes_When_Titlecased"] = (int)UnicodeBinaryProperty.ChangesWhenTitlecased,
        ["CWT"] = (int)UnicodeBinaryProperty.ChangesWhenTitlecased,
        ["Changes_When_Uppercased"] = (int)UnicodeBinaryProperty.ChangesWhenUppercased,
        ["CWU"] = (int)UnicodeBinaryProperty.ChangesWhenUppercased,
        ["Dash"] = (int)UnicodeBinaryProperty.Dash,
        ["Default_Ignorable_Code_Point"] = (int)UnicodeBinaryProperty.DefaultIgnorableCodePoint,
        ["DI"] = (int)UnicodeBinaryProperty.DefaultIgnorableCodePoint,
        ["Deprecated"] = (int)UnicodeBinaryProperty.Deprecated,
        ["Dep"] = (int)UnicodeBinaryProperty.Deprecated,
        ["Diacritic"] = (int)UnicodeBinaryProperty.Diacritic,
        ["Dia"] = (int)UnicodeBinaryProperty.Diacritic,
        ["Emoji"] = (int)UnicodeBinaryProperty.Emoji,
        ["Emoji_Component"] = (int)UnicodeBinaryProperty.EmojiComponent,
        ["EComp"] = (int)UnicodeBinaryProperty.EmojiComponent,
        ["Emoji_Modifier"] = (int)UnicodeBinaryProperty.EmojiModifier,
        ["EMod"] = (int)UnicodeBinaryProperty.EmojiModifier,
        ["Emoji_Modifier_Base"] = (int)UnicodeBinaryProperty.EmojiModifierBase,
        ["EBase"] = (int)UnicodeBinaryProperty.EmojiModifierBase,
        ["Emoji_Presentation"] = (int)UnicodeBinaryProperty.EmojiPresentation,
        ["EPres"] = (int)UnicodeBinaryProperty.EmojiPresentation,
        ["Extended_Pictographic"] = (int)UnicodeBinaryProperty.ExtendedPictographic,
        ["ExtPict"] = (int)UnicodeBinaryProperty.ExtendedPictographic,
        ["Extender"] = (int)UnicodeBinaryProperty.Extender,
        ["Ext"] = (int)UnicodeBinaryProperty.Extender,
        ["Grapheme_Base"] = (int)UnicodeBinaryProperty.GraphemeBase,
        ["Gr_Base"] = (int)UnicodeBinaryProperty.GraphemeBase,
        ["Grapheme_Extend"] = (int)UnicodeBinaryProperty.GraphemeExtend,
        ["Gr_Ext"] = (int)UnicodeBinaryProperty.GraphemeExtend,
        ["Hex_Digit"] = (int)UnicodeBinaryProperty.HexDigit,
        ["Hex"] = (int)UnicodeBinaryProperty.HexDigit,
        ["IDS_Binary_Operator"] = (int)UnicodeBinaryProperty.IDSBinaryOperator,
        ["IDSB"] = (int)UnicodeBinaryProperty.IDSBinaryOperator,
        ["IDS_Trinary_Operator"] = (int)UnicodeBinaryProperty.IDSTrinaryOperator,
        ["IDST"] = (int)UnicodeBinaryProperty.IDSTrinaryOperator,
        ["ID_Continue"] = (int)UnicodeBinaryProperty.IDContinue,
        ["IDC"] = (int)UnicodeBinaryProperty.IDContinue,
        ["ID_Start"] = (int)UnicodeBinaryProperty.IDStart,
        ["IDS"] = (int)UnicodeBinaryProperty.IDStart,
        ["Ideographic"] = (int)UnicodeBinaryProperty.Ideographic,
        ["Ideo"] = (int)UnicodeBinaryProperty.Ideographic,
        ["Join_Control"] = (int)UnicodeBinaryProperty.JoinControl,
        ["Join_C"] = (int)UnicodeBinaryProperty.JoinControl,
        ["Logical_Order_Exception"] = (int)UnicodeBinaryProperty.LogicalOrderException,
        ["LOE"] = (int)UnicodeBinaryProperty.LogicalOrderException,
        ["Lowercase"] = (int)UnicodeBinaryProperty.Lowercase,
        ["Lower"] = (int)UnicodeBinaryProperty.Lowercase,
        ["Math"] = (int)UnicodeBinaryProperty.Math,
        ["Noncharacter_Code_Point"] = (int)UnicodeBinaryProperty.NoncharacterCodePoint,
        ["NChar"] = (int)UnicodeBinaryProperty.NoncharacterCodePoint,
        ["Pattern_Syntax"] = (int)UnicodeBinaryProperty.PatternSyntax,
        ["Pat_Syn"] = (int)UnicodeBinaryProperty.PatternSyntax,
        ["Pattern_White_Space"] = (int)UnicodeBinaryProperty.PatternWhiteSpace,
        ["Pat_WS"] = (int)UnicodeBinaryProperty.PatternWhiteSpace,
        ["Quotation_Mark"] = (int)UnicodeBinaryProperty.QuotationMark,
        ["QMark"] = (int)UnicodeBinaryProperty.QuotationMark,
        ["Radical"] = (int)UnicodeBinaryProperty.Radical,
        ["Regional_Indicator"] = (int)UnicodeBinaryProperty.RegionalIndicator,
        ["RI"] = (int)UnicodeBinaryProperty.RegionalIndicator,
        ["Sentence_Terminal"] = (int)UnicodeBinaryProperty.SentenceTerminal,
        ["STerm"] = (int)UnicodeBinaryProperty.SentenceTerminal,
        ["Soft_Dotted"] = (int)UnicodeBinaryProperty.SoftDotted,
        ["SD"] = (int)UnicodeBinaryProperty.SoftDotted,
        ["Terminal_Punctuation"] = (int)UnicodeBinaryProperty.TerminalPunctuation,
        ["Term"] = (int)UnicodeBinaryProperty.TerminalPunctuation,
        ["Unified_Ideograph"] = (int)UnicodeBinaryProperty.UnifiedIdeograph,
        ["UIdeo"] = (int)UnicodeBinaryProperty.UnifiedIdeograph,
        ["Uppercase"] = (int)UnicodeBinaryProperty.Uppercase,
        ["Upper"] = (int)UnicodeBinaryProperty.Uppercase,
        ["Variation_Selector"] = (int)UnicodeBinaryProperty.VariationSelector,
        ["VS"] = (int)UnicodeBinaryProperty.VariationSelector,
        ["White_Space"] = (int)UnicodeBinaryProperty.WhiteSpace,
        ["space"] = (int)UnicodeBinaryProperty.WhiteSpace,
        ["XID_Continue"] = (int)UnicodeBinaryProperty.XIDContinue,
        ["XIDC"] = (int)UnicodeBinaryProperty.XIDContinue,
        ["XID_Start"] = (int)UnicodeBinaryProperty.XIDStart,
        ["XIDS"] = (int)UnicodeBinaryProperty.XIDStart,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // The values and aliases of General_Category in PropertyValueAliases.txt, as a bit set of UnicodeCategory values
    private static readonly FrozenDictionary<string, int> GeneralCategories = CreateGeneralCategories();

    // Table 67: the binary properties of strings
    private static readonly FrozenDictionary<string, UnicodeEmojiSequenceProperty[]> StringProperties = new Dictionary<string, UnicodeEmojiSequenceProperty[]>(StringComparer.Ordinal)
    {
        ["Basic_Emoji"] = [UnicodeEmojiSequenceProperty.BasicEmoji],
        ["Emoji_Keycap_Sequence"] = [UnicodeEmojiSequenceProperty.EmojiKeycapSequence],
        ["RGI_Emoji_Modifier_Sequence"] = [UnicodeEmojiSequenceProperty.RgiEmojiModifierSequence],
        ["RGI_Emoji_Flag_Sequence"] = [UnicodeEmojiSequenceProperty.RgiEmojiFlagSequence],
        ["RGI_Emoji_Tag_Sequence"] = [UnicodeEmojiSequenceProperty.RgiEmojiTagSequence],
        ["RGI_Emoji_ZWJ_Sequence"] = [UnicodeEmojiSequenceProperty.RgiEmojiZwjSequence],
        ["RGI_Emoji"] =
        [
            UnicodeEmojiSequenceProperty.BasicEmoji,
            UnicodeEmojiSequenceProperty.EmojiKeycapSequence,
            UnicodeEmojiSequenceProperty.RgiEmojiModifierSequence,
            UnicodeEmojiSequenceProperty.RgiEmojiFlagSequence,
            UnicodeEmojiSequenceProperty.RgiEmojiTagSequence,
            UnicodeEmojiSequenceProperty.RgiEmojiZwjSequence,
        ],
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<int, CodePointSet> BinaryCache = new();
    private static readonly ConcurrentDictionary<string, (CodePointSet Singles, string[] Strings)> StringCache = new(StringComparer.Ordinal);
    private static CodePointSet[]? s_generalCategories;
    private static Dictionary<UnicodeScript, CodePointSet>? s_scripts;
    private static Dictionary<UnicodeScript, CodePointSet>? s_scriptExtensions;

    /// <summary>The kind of a property escape.</summary>
    public enum PropertyKind
    {
        Unknown,
        GeneralCategory,
        Other,
        Strings,
    }

    /// <summary>Resolves the text between the braces of <c>\p{...}</c>.</summary>
    /// <param name="text">The text, such as <c>L</c>, <c>Script=Latin</c> or <c>RGI_Emoji</c>.</param>
    /// <param name="unicodeSets">Whether the pattern has the "v" flag, which allows the properties of strings.</param>
    /// <param name="set">The code points.</param>
    /// <param name="strings">The strings of more than one code point of a property of strings.</param>
    public static PropertyKind Resolve(string text, bool unicodeSets, out CodePointSet set, out string[] strings)
    {
        strings = [];
        set = new CodePointSet();
        var separator = text.IndexOf('=', StringComparison.Ordinal);
        if (separator >= 0)
        {
            var name = text[..separator];
            var value = text[(separator + 1)..];
            switch (name)
            {
                case "General_Category" or "gc":
                    if (GeneralCategories.TryGetValue(value, out var categories))
                    {
                        set = GetGeneralCategory(categories);
                        return PropertyKind.Other;
                    }

                    return PropertyKind.Unknown;

                case "Script" or "sc":
                    if (UnicodeScripts.TryParseScript(value, out var script))
                    {
                        set = GetScript(script, extensions: false);
                        return PropertyKind.Other;
                    }

                    return PropertyKind.Unknown;

                case "Script_Extensions" or "scx":
                    if (UnicodeScripts.TryParseScript(value, out script))
                    {
                        set = GetScript(script, extensions: true);
                        return PropertyKind.Other;
                    }

                    return PropertyKind.Unknown;

                default:
                    return PropertyKind.Unknown;
            }
        }

        if (GeneralCategories.TryGetValue(text, out var lone))
        {
            set = GetGeneralCategory(lone);
            return PropertyKind.GeneralCategory;
        }

        if (BinaryProperties.TryGetValue(text, out var property))
        {
            set = GetBinaryProperty(property);
            return PropertyKind.Other;
        }

        if (unicodeSets && StringProperties.TryGetValue(text, out var sequences))
        {
            (set, strings) = StringCache.GetOrAdd(text, _ => CreateStringProperty(sequences));
            return PropertyKind.Strings;
        }

        return PropertyKind.Unknown;
    }

    private static CodePointSet GetBinaryProperty(int property)
    {
        return BinaryCache.GetOrAdd(property, p =>
        {
            switch (p)
            {
                case SpecialAny:
                    return CodePointSet.Range(0, CodePointSet.MaxCodePoint);
                case SpecialAscii:
                    return CodePointSet.Range(0, 0x7F);
                case SpecialAssigned:
                    return GetGeneralCategory(1 << (int)UnicodeCategory.OtherNotAssigned).Complement(CodePointSet.MaxCodePoint);
                default:
                    var result = new CodePointSet();
                    foreach (var range in UnicodeProperties.GetBinaryPropertyRanges((UnicodeBinaryProperty)p))
                    {
                        result.AddRange(range.Start, range.End);
                    }

                    _ = result.RangeCount;
                    return result;
            }
        });
    }

    private static CodePointSet GetGeneralCategory(int categories)
    {
        var sets = s_generalCategories ?? CreateGeneralCategorySets();
        var single = -1;
        for (var i = 0; i < sets.Length; i++)
        {
            if ((categories & (1 << i)) != 0)
            {
                if (single >= 0)
                {
                    // A group of categories (L, LC, M...)
                    // The key of a group is above the values of the binary properties
                    return BinaryCache.GetOrAdd(GroupKeyBase + categories, key =>
                    {
                        var bits = key - GroupKeyBase;
                        var result = new CodePointSet();
                        for (var j = 0; j < sets.Length; j++)
                        {
                            if ((bits & (1 << j)) != 0)
                            {
                                result.AddSet(sets[j]);
                            }
                        }

                        _ = result.RangeCount;
                        return result;
                    });
                }

                single = i;
            }
        }

        return sets[single];
    }

    private static CodePointSet[] CreateGeneralCategorySets()
    {
        var sets = new CodePointSet[30];
        for (var i = 0; i < sets.Length; i++)
        {
            sets[i] = new CodePointSet();
        }

        var start = 0;
        var current = UnicodeProperties.GetGeneralCategory(0);
        for (var c = 1; c <= CodePointSet.MaxCodePoint + 1; c++)
        {
            var category = c <= CodePointSet.MaxCodePoint ? UnicodeProperties.GetGeneralCategory(c) : (UnicodeCategory)(-1);
            if (category != current)
            {
                sets[(int)current].AddRange(start, c - 1);
                start = c;
                current = category;
            }
        }

        foreach (var set in sets)
        {
            _ = set.RangeCount;
        }

        Volatile.Write(ref s_generalCategories, sets);
        return sets;
    }

    private static CodePointSet GetScript(UnicodeScript script, bool extensions)
    {
        var scripts = extensions ? s_scriptExtensions ?? CreateScriptSets(extensions: true) : s_scripts ?? CreateScriptSets(extensions: false);
        return scripts.TryGetValue(script, out var set) ? set : new CodePointSet();
    }

    private static Dictionary<UnicodeScript, CodePointSet> CreateScriptSets(bool extensions)
    {
        var result = new Dictionary<UnicodeScript, CodePointSet>();
        Span<UnicodeScript> buffer = stackalloc UnicodeScript[UnicodeScripts.MaxScriptExtensionCount];
        for (var c = 0; c <= CodePointSet.MaxCodePoint; c++)
        {
            if (extensions)
            {
                var count = UnicodeScripts.GetScriptExtensions(c, buffer);
                for (var i = 0; i < count; i++)
                {
                    GetOrCreate(result, buffer[i]).Add(c);
                }
            }
            else
            {
                GetOrCreate(result, UnicodeScripts.GetScript(c)).Add(c);
            }
        }

        foreach (var set in result.Values)
        {
            _ = set.RangeCount;
        }

        if (extensions)
        {
            Volatile.Write(ref s_scriptExtensions, result);
        }
        else
        {
            Volatile.Write(ref s_scripts, result);
        }

        return result;

        static CodePointSet GetOrCreate(Dictionary<UnicodeScript, CodePointSet> sets, UnicodeScript script)
        {
            if (!sets.TryGetValue(script, out var set))
            {
                set = new CodePointSet();
                sets.Add(script, set);
            }

            return set;
        }
    }

    private static (CodePointSet Singles, string[] Strings) CreateStringProperty(UnicodeEmojiSequenceProperty[] properties)
    {
        var singles = new CodePointSet();
        var strings = new List<string>();
        foreach (var property in properties)
        {
            foreach (var sequence in UnicodeEmoji.GetSequences(property))
            {
                if (sequence.Length == 1 || (sequence.Length == 2 && char.IsSurrogatePair(sequence[0], sequence[1])))
                {
                    singles.Add(char.ConvertToUtf32(sequence, 0));
                }
                else
                {
                    strings.Add(sequence);
                }
            }
        }

        _ = singles.RangeCount;
        return (singles, [.. strings]);
    }

    private static FrozenDictionary<string, int> CreateGeneralCategories()
    {
        static int Bits(params ReadOnlySpan<UnicodeCategory> categories)
        {
            var result = 0;
            foreach (var category in categories)
            {
                result |= 1 << (int)category;
            }

            return result;
        }

        var cased = Bits(UnicodeCategory.UppercaseLetter, UnicodeCategory.LowercaseLetter, UnicodeCategory.TitlecaseLetter);
        var letter = cased | Bits(UnicodeCategory.ModifierLetter, UnicodeCategory.OtherLetter);
        var mark = Bits(UnicodeCategory.NonSpacingMark, UnicodeCategory.SpacingCombiningMark, UnicodeCategory.EnclosingMark);
        var number = Bits(UnicodeCategory.DecimalDigitNumber, UnicodeCategory.LetterNumber, UnicodeCategory.OtherNumber);
        var punctuation = Bits(UnicodeCategory.ConnectorPunctuation, UnicodeCategory.DashPunctuation, UnicodeCategory.OpenPunctuation, UnicodeCategory.ClosePunctuation, UnicodeCategory.InitialQuotePunctuation, UnicodeCategory.FinalQuotePunctuation, UnicodeCategory.OtherPunctuation);
        var symbol = Bits(UnicodeCategory.MathSymbol, UnicodeCategory.CurrencySymbol, UnicodeCategory.ModifierSymbol, UnicodeCategory.OtherSymbol);
        var separator = Bits(UnicodeCategory.SpaceSeparator, UnicodeCategory.LineSeparator, UnicodeCategory.ParagraphSeparator);
        var other = Bits(UnicodeCategory.Control, UnicodeCategory.Format, UnicodeCategory.OtherNotAssigned, UnicodeCategory.PrivateUse, UnicodeCategory.Surrogate);

        return new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["C"] = other,
            ["Other"] = other,
            ["Cc"] = Bits(UnicodeCategory.Control),
            ["Control"] = Bits(UnicodeCategory.Control),
            ["cntrl"] = Bits(UnicodeCategory.Control),
            ["Cf"] = Bits(UnicodeCategory.Format),
            ["Format"] = Bits(UnicodeCategory.Format),
            ["Cn"] = Bits(UnicodeCategory.OtherNotAssigned),
            ["Unassigned"] = Bits(UnicodeCategory.OtherNotAssigned),
            ["Co"] = Bits(UnicodeCategory.PrivateUse),
            ["Private_Use"] = Bits(UnicodeCategory.PrivateUse),
            ["Cs"] = Bits(UnicodeCategory.Surrogate),
            ["Surrogate"] = Bits(UnicodeCategory.Surrogate),
            ["L"] = letter,
            ["Letter"] = letter,
            ["LC"] = cased,
            ["Cased_Letter"] = cased,
            ["Ll"] = Bits(UnicodeCategory.LowercaseLetter),
            ["Lowercase_Letter"] = Bits(UnicodeCategory.LowercaseLetter),
            ["Lm"] = Bits(UnicodeCategory.ModifierLetter),
            ["Modifier_Letter"] = Bits(UnicodeCategory.ModifierLetter),
            ["Lo"] = Bits(UnicodeCategory.OtherLetter),
            ["Other_Letter"] = Bits(UnicodeCategory.OtherLetter),
            ["Lt"] = Bits(UnicodeCategory.TitlecaseLetter),
            ["Titlecase_Letter"] = Bits(UnicodeCategory.TitlecaseLetter),
            ["Lu"] = Bits(UnicodeCategory.UppercaseLetter),
            ["Uppercase_Letter"] = Bits(UnicodeCategory.UppercaseLetter),
            ["M"] = mark,
            ["Mark"] = mark,
            ["Combining_Mark"] = mark,
            ["Mc"] = Bits(UnicodeCategory.SpacingCombiningMark),
            ["Spacing_Mark"] = Bits(UnicodeCategory.SpacingCombiningMark),
            ["Me"] = Bits(UnicodeCategory.EnclosingMark),
            ["Enclosing_Mark"] = Bits(UnicodeCategory.EnclosingMark),
            ["Mn"] = Bits(UnicodeCategory.NonSpacingMark),
            ["Nonspacing_Mark"] = Bits(UnicodeCategory.NonSpacingMark),
            ["N"] = number,
            ["Number"] = number,
            ["Nd"] = Bits(UnicodeCategory.DecimalDigitNumber),
            ["Decimal_Number"] = Bits(UnicodeCategory.DecimalDigitNumber),
            ["digit"] = Bits(UnicodeCategory.DecimalDigitNumber),
            ["Nl"] = Bits(UnicodeCategory.LetterNumber),
            ["Letter_Number"] = Bits(UnicodeCategory.LetterNumber),
            ["No"] = Bits(UnicodeCategory.OtherNumber),
            ["Other_Number"] = Bits(UnicodeCategory.OtherNumber),
            ["P"] = punctuation,
            ["Punctuation"] = punctuation,
            ["punct"] = punctuation,
            ["Pc"] = Bits(UnicodeCategory.ConnectorPunctuation),
            ["Connector_Punctuation"] = Bits(UnicodeCategory.ConnectorPunctuation),
            ["Pd"] = Bits(UnicodeCategory.DashPunctuation),
            ["Dash_Punctuation"] = Bits(UnicodeCategory.DashPunctuation),
            ["Pe"] = Bits(UnicodeCategory.ClosePunctuation),
            ["Close_Punctuation"] = Bits(UnicodeCategory.ClosePunctuation),
            ["Pf"] = Bits(UnicodeCategory.FinalQuotePunctuation),
            ["Final_Punctuation"] = Bits(UnicodeCategory.FinalQuotePunctuation),
            ["Pi"] = Bits(UnicodeCategory.InitialQuotePunctuation),
            ["Initial_Punctuation"] = Bits(UnicodeCategory.InitialQuotePunctuation),
            ["Po"] = Bits(UnicodeCategory.OtherPunctuation),
            ["Other_Punctuation"] = Bits(UnicodeCategory.OtherPunctuation),
            ["Ps"] = Bits(UnicodeCategory.OpenPunctuation),
            ["Open_Punctuation"] = Bits(UnicodeCategory.OpenPunctuation),
            ["S"] = symbol,
            ["Symbol"] = symbol,
            ["Sc"] = Bits(UnicodeCategory.CurrencySymbol),
            ["Currency_Symbol"] = Bits(UnicodeCategory.CurrencySymbol),
            ["Sk"] = Bits(UnicodeCategory.ModifierSymbol),
            ["Modifier_Symbol"] = Bits(UnicodeCategory.ModifierSymbol),
            ["Sm"] = Bits(UnicodeCategory.MathSymbol),
            ["Math_Symbol"] = Bits(UnicodeCategory.MathSymbol),
            ["So"] = Bits(UnicodeCategory.OtherSymbol),
            ["Other_Symbol"] = Bits(UnicodeCategory.OtherSymbol),
            ["Z"] = separator,
            ["Separator"] = separator,
            ["Zl"] = Bits(UnicodeCategory.LineSeparator),
            ["Line_Separator"] = Bits(UnicodeCategory.LineSeparator),
            ["Zp"] = Bits(UnicodeCategory.ParagraphSeparator),
            ["Paragraph_Separator"] = Bits(UnicodeCategory.ParagraphSeparator),
            ["Zs"] = Bits(UnicodeCategory.SpaceSeparator),
            ["Space_Separator"] = Bits(UnicodeCategory.SpaceSeparator),
        }.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
