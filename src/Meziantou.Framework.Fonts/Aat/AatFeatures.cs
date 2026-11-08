using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// The settings of the features of Apple Advanced Typography that the OpenType features select: a feature type and
/// selector of the Font Feature Registry (Apple TrueType Reference Manual, "Font Feature Registry") for each OpenType
/// feature that has an equivalent.
/// </summary>
/// <remarks>
/// No specification pairs the two registries. The pairs are the ones the reference shaper uses, read from what it
/// selects in a font that has a chain for every setting of every type (a feature it does not pair, such as
/// <c>salt</c>, selects nothing): the selectors of the registry whose names match the OpenType features, with a few
/// choices that names do not make. A feature that is off selects the "off" selector of a type whose settings come in
/// pairs, the first selector of the other types, and selectors that do not exist for the number spacing (4), the
/// number case (2), the text spacing (7) and the character shape (16), so that nothing is selected.
/// </remarks>
internal static class AatFeatures
{
    private const int LigaturesType = 1;
    private const int LetterCaseType = 3;
    private const int VerticalSubstitutionType = 4;
    private const int NumberSpacingType = 6;
    private const int VerticalPositionType = 10;
    private const int FractionsType = 11;
    private const int TypographicExtrasType = 14;
    private const int MathematicalExtrasType = 15;
    private const int CharacterAlternativesType = 17;
    private const int StyleOptionsType = 19;
    private const int CharacterShapeType = 20;
    private const int NumberCaseType = 21;
    private const int TextSpacingType = 22;
    private const int TransliterationType = 23;
    private const int RubyKanaType = 28;
    private const int ItalicCjkRomanType = 32;
    private const int CaseSensitiveLayoutType = 33;
    private const int AlternateKanaType = 34;
    private const int StylisticAlternativesType = 35;
    private const int ContextualAlternatesType = 36;
    private const int LowerCaseType = 37;
    private const int UpperCaseType = 38;

    // A type the registry does not list, which the fonts of Apple systems have for the historical forms.
    private const int HistoricalType = 40;

    private static readonly Dictionary<uint, Mapping> Mappings = CreateMappings();

    /// <summary>Gets the setting an OpenType feature selects, and the one it selects when it is off.</summary>
    public static bool TryGetMapping(uint tag, out Mapping mapping) => Mappings.TryGetValue(tag, out mapping);

    private static Dictionary<uint, Mapping> CreateMappings()
    {
        var mappings = new Dictionary<uint, Mapping>
        {
            [ShapingFeature.ToTag("aalt")] = new(CharacterAlternativesType, 1, 0, SelectorIsValue: true),
            [ShapingFeature.ToTag("afrc")] = new(FractionsType, 1, 0),
            [ShapingFeature.ToTag("c2pc")] = new(UpperCaseType, 2, 0),
            [ShapingFeature.ToTag("c2sc")] = new(UpperCaseType, 1, 0),
            [ShapingFeature.ToTag("calt")] = new(ContextualAlternatesType, 0, 1),
            [ShapingFeature.ToTag("case")] = new(CaseSensitiveLayoutType, 0, 1),
            [ShapingFeature.ToTag("clig")] = new(LigaturesType, 18, 19),
            [ShapingFeature.ToTag("cpsp")] = new(CaseSensitiveLayoutType, 2, 3),
            [ShapingFeature.ToTag("cswh")] = new(ContextualAlternatesType, 4, 5),
            [ShapingFeature.ToTag("dlig")] = new(LigaturesType, 4, 5),
            [ShapingFeature.ToTag("expt")] = new(CharacterShapeType, 10, 16),
            [ShapingFeature.ToTag("frac")] = new(FractionsType, 2, 0),
            [ShapingFeature.ToTag("fwid")] = new(TextSpacingType, 1, 7),
            [ShapingFeature.ToTag("halt")] = new(TextSpacingType, 6, 7),
            [ShapingFeature.ToTag("hist")] = new(HistoricalType, 0, 1),
            [ShapingFeature.ToTag("hkna")] = new(AlternateKanaType, 0, 1),
            [ShapingFeature.ToTag("hlig")] = new(LigaturesType, 20, 21),
            [ShapingFeature.ToTag("hngl")] = new(TransliterationType, 1, 0),
            [ShapingFeature.ToTag("hojo")] = new(CharacterShapeType, 12, 16),
            [ShapingFeature.ToTag("hwid")] = new(TextSpacingType, 2, 7),
            [ShapingFeature.ToTag("ital")] = new(ItalicCjkRomanType, 2, 3),
            [ShapingFeature.ToTag("jp04")] = new(CharacterShapeType, 11, 16),
            [ShapingFeature.ToTag("jp78")] = new(CharacterShapeType, 2, 16),
            [ShapingFeature.ToTag("jp83")] = new(CharacterShapeType, 3, 16),
            [ShapingFeature.ToTag("jp90")] = new(CharacterShapeType, 4, 16),
            [ShapingFeature.ToTag("liga")] = new(LigaturesType, 2, 3),
            [ShapingFeature.ToTag("lnum")] = new(NumberCaseType, 1, 2),
            [ShapingFeature.ToTag("mgrk")] = new(MathematicalExtrasType, 10, 11),
            [ShapingFeature.ToTag("nlck")] = new(CharacterShapeType, 13, 16),
            [ShapingFeature.ToTag("onum")] = new(NumberCaseType, 0, 2),
            [ShapingFeature.ToTag("ordn")] = new(VerticalPositionType, 3, 0),
            [ShapingFeature.ToTag("palt")] = new(TextSpacingType, 5, 7),
            [ShapingFeature.ToTag("pcap")] = new(LowerCaseType, 2, 0),
            [ShapingFeature.ToTag("pkna")] = new(TextSpacingType, 0, 7),
            [ShapingFeature.ToTag("pnum")] = new(NumberSpacingType, 1, 4),
            [ShapingFeature.ToTag("pwid")] = new(TextSpacingType, 0, 7),
            [ShapingFeature.ToTag("qwid")] = new(TextSpacingType, 4, 7),
            [ShapingFeature.ToTag("rlig")] = new(LigaturesType, 0, 1),
            [ShapingFeature.ToTag("ruby")] = new(RubyKanaType, 2, 3),
            [ShapingFeature.ToTag("sinf")] = new(VerticalPositionType, 4, 0),
            [ShapingFeature.ToTag("smcp")] = new(LowerCaseType, 1, 0, LegacyType: LetterCaseType, LegacySelector: 3),
            [ShapingFeature.ToTag("smpl")] = new(CharacterShapeType, 1, 16),
            [ShapingFeature.ToTag("subs")] = new(VerticalPositionType, 2, 0),
            [ShapingFeature.ToTag("sups")] = new(VerticalPositionType, 1, 0),
            [ShapingFeature.ToTag("swsh")] = new(ContextualAlternatesType, 2, 3),
            [ShapingFeature.ToTag("titl")] = new(StyleOptionsType, 4, 0),
            [ShapingFeature.ToTag("tnam")] = new(CharacterShapeType, 14, 16),
            [ShapingFeature.ToTag("tnum")] = new(NumberSpacingType, 0, 4),
            [ShapingFeature.ToTag("trad")] = new(CharacterShapeType, 0, 16),
            [ShapingFeature.ToTag("twid")] = new(TextSpacingType, 3, 7),
            [ShapingFeature.ToTag("unic")] = new(LetterCaseType, 14, 15),
            [ShapingFeature.ToTag("valt")] = new(TextSpacingType, 5, 7),
            [ShapingFeature.ToTag("vert")] = new(VerticalSubstitutionType, 0, 1),
            [ShapingFeature.ToTag("vhal")] = new(TextSpacingType, 6, 7),
            [ShapingFeature.ToTag("vkna")] = new(AlternateKanaType, 2, 3),
            [ShapingFeature.ToTag("vpal")] = new(TextSpacingType, 5, 7),
            [ShapingFeature.ToTag("vrt2")] = new(VerticalSubstitutionType, 0, 1),
            [ShapingFeature.ToTag("vrtr")] = new(VerticalSubstitutionType, 2, 3),
            [ShapingFeature.ToTag("zero")] = new(TypographicExtrasType, 4, 5),
        };

        for (var i = 1; i <= 20; i++)
        {
            mappings[ShapingFeature.ToTag("ss" + i.ToString("00", CultureInfo.InvariantCulture))] = new(StylisticAlternativesType, 2 * i, (2 * i) + 1);
        }

        return mappings;
    }

    /// <summary>The setting of an AAT feature an OpenType feature selects.</summary>
    /// <param name="Type">The feature type.</param>
    /// <param name="Selector">The selector of the feature when it is on.</param>
    /// <param name="OffSelector">The selector of the feature when it is off.</param>
    /// <param name="SelectorIsValue">The value of the OpenType feature is the selector (the alternates of <c>aalt</c>).</param>
    /// <param name="LegacyType">A deprecated feature type whose setting is selected too, or 0.</param>
    /// <param name="LegacySelector">The selector of the deprecated type when the feature is on.</param>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct Mapping(int Type, int Selector, int OffSelector, bool SelectorIsValue = false, int LegacyType = 0, int LegacySelector = 0);
}
