using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Generates the font part of the corpus (the <c>fonts</c> section of the manifest and the fonts/ directory): font files
/// under an allowed license, and the values independent font readers give for them.
/// </summary>
/// <remarks>
/// <para>
/// The synthetic fonts are built by Common/FontBuilder.cs from the descriptions of Common/FontSamples.cs, both shared
/// with the tests and without any dependency on the library. The third-party fonts come from a directory given by
/// --fonts; each file is checked against its pinned SHA-256 before it is used, then subset by <c>hb-subset</c> when its
/// license allows a modified copy (no Reserved Font Name).
/// </para>
/// <para>
/// The reference values (names, metrics, character map, advances, outlines) are read by the HarfBuzz utilities of the
/// pinned meziantou/prebuilt release with two independent implementations: HarfBuzz's own font functions (<c>ot</c>) and
/// FreeType (<c>ft</c>). A value is recorded only when both agree; a disagreement stops the generator. The character
/// map is the join of <c>hb-info --list-unicodes</c> and <c>--list-glyphs</c>, so that it does not go through shaping.
/// </para>
/// <para>
/// The WOFF forms are written by FontBuilder (nothing else in the pinned tools writes WOFF 1.0) and the WOFF2 forms by
/// <c>woff2_compress</c>. Each one is read back through FreeType (<c>--face-loader=ft</c>), the only reader of the pinned
/// tools that opens these containers, and must give the advances and the outlines of the original.
/// </para>
/// </remarks>
internal static partial class FontCorpus
{
    public const string ScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/FontCorpus.cs";
    private const string WriterPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/Common/FontWoff2Writer.cs";
    private const string SamplesPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/Common/FontSamples.cs";
    private const string PinnedHarfBuzz = "14.6.0";
    private const string PinnedWoff2 = "1.0.2+fb9c337";

    private static readonly string[] MetricTags = ["hasc", "hdsc", "hlgp", "xhgt", "cpht", "unds", "undo", "strs", "stro"];

    // The tables whose presence is recorded as a feature of the font.
    private static readonly string[] NotableTables =
    [
        "CFF ", "CFF2", "glyf", "kern", "GDEF", "GSUB", "GPOS", "BASE", "fvar", "avar", "gvar", "HVAR", "MVAR", "STAT", "COLR", "CPAL", "sbix", "CBDT", "CBLC", "SVG ",
    ];

    private static readonly SyntheticFont[] SyntheticFonts =
    [
        new("fonts/synthetic-boxes", "fonts/synthetic/boxes.ttf", static () => FontSamples.Boxes().Build(), nameof(FontSamples.Boxes), "ttf", 1),
        new("fonts/synthetic-curves", "fonts/synthetic/curves.ttf", static () => FontSamples.Curves().Build(), nameof(FontSamples.Curves), "ttf", 1),
        new("fonts/synthetic-deltas", "fonts/synthetic/deltas.ttf", static () => FontSamples.Deltas().Build(), nameof(FontSamples.Deltas), "ttf", 1),
        new("fonts/synthetic-charstrings", "fonts/synthetic/charstrings.otf", static () => FontSamples.Charstrings().Build(), nameof(FontSamples.Charstrings), "otf", 1),
        new("fonts/synthetic-cid", "fonts/synthetic/cid.otf", static () => FontSamples.CidKeyed().Build(), nameof(FontSamples.CidKeyed), "otf", 1),
        new("fonts/synthetic-collection", "fonts/synthetic/collection.ttc", static () => FontBuilder.WriteCollection([FontSamples.Boxes().BuildTables(), FontSamples.Curves().BuildTables()]), nameof(FontSamples.Boxes) + " and " + nameof(FontSamples.Curves), "ttc", 2),
        new("fonts/synthetic-variable", "fonts/synthetic/variable.ttf", static () => FontSamples.Variable().Build(), nameof(FontSamples.Variable), "ttf", 1),
        new("fonts/synthetic-variable-phantom", "fonts/synthetic/variable.phantom.ttf", static () => FontSamples.Variable(withAdvanceVariations: false).Build(), nameof(FontSamples.Variable) + " without HVAR", "ttf", 1),
        new("fonts/synthetic-variable-charstrings", "fonts/synthetic/variable-charstrings.otf", static () => FontSamples.VariableCharstrings().Build(), nameof(FontSamples.VariableCharstrings), "otf", 1),
    ];

    // The instances of the variable fonts whose advances, outlines and metrics are recorded, in the syntax of the
    // reference tools. The default instance is always recorded.
    private static readonly string[] SyntheticInstances = ["wght=900", "wght=100", "wght=650", "wght=775", "wdth=200", "wdth=50", "wdth=125", "wght=900,wdth=200", "wght=775,wdth=150", "wght=250,wdth=75", "wght=1000,wdth=10"];

    private static readonly Dictionary<string, string[]> Instances = new(StringComparer.Ordinal)
    {
        ["fonts/synthetic-variable"] = SyntheticInstances,
        ["fonts/synthetic-variable-phantom"] = SyntheticInstances,
        ["fonts/synthetic-variable-charstrings"] = ["wght=900", "wght=100", "wght=650", "wght=250", "wght=525"],
        ["fonts/noto-sans-variable"] = ["wght=900", "wght=100,wdth=62.5", "wght=555,wdth=77", "wght=650,wdth=81.25", "wght=250"],
        ["fonts/noto-sans-cjk-jp-variable"] = ["wght=400", "wght=900", "wght=555", "wght=250"],
        ["fonts/trt-avar"] = ["TEST=100", "TEST=150", "TEST=200", "TEST=250", "TEST=300", "TEST=350", "TEST=400", "TEST=450", "TEST=500", "TEST=550", "TEST=600", "TEST=650", "TEST=700", "TEST=750", "TEST=800", "TEST=850", "TEST=900"],
        ["fonts/trt-gvar-four"] = ["wght=150", "wght=200", "wght=250", "wght=300", "wght=350", "wght=400", "wght=450", "cntr=100", "wght=600,cntr=40"],
        ["fonts/trt-gvar-nine"] = ["TEST=-1.0", "TEST=-0.5", "TEST=0.25", "TEST=0.5", "TEST=0.75", "TEST=1.0"],
        ["fonts/trt-gvar-composite"] = ["slnt=-15", "slnt=-11.25", "slnt=-7.5", "slnt=-3"],
        ["fonts/trt-hvar-one"] = ["wght=200", "wght=400", "wght=600", "wght=800", "wght=1000"],
        ["fonts/trt-hvar-two"] = ["wght=200", "wght=400", "wght=600", "wght=800", "wght=1000", "cntr=100", "wght=500,cntr=50"],
        ["fonts/trt-rvrn"] = ["wght=400", "wght=650", "wght=900", "opsz=10", "opsz=30,wght=500"],
        ["fonts/trt-rvrn-cff2"] = ["wght=400", "wght=650", "wght=900", "opsz=10", "opsz=30,wght=500"],
        ["fonts/trt-cff2"] = ["wght=0", "wght=100", "wght=200", "wght=300", "wght=400", "wght=500", "wght=600", "wght=700", "wght=800", "wght=900", "xxxx=100", "wght=350,xxxx=50"],
        ["fonts/trt-gpos-four"] = ["wght=100", "wght=600", "wght=900"],
    };

    // The fonts of the text-rendering-tests suite that are used, with the SHA-256 of each file.
    private static readonly Dictionary<string, string> TextRenderingHashes = new(StringComparer.Ordinal)
    {
        ["TestAVAR.ttf"] = "5deb587e7abe1b175d740dfded516b93ebb4a6cb7ea9c65b784d3f4498d2c7cf",
        ["TestGVARFour.ttf"] = "a749ef97d87e7bb6ab211e1e9ac262c8ab9bb0fb7e2cb7b2e9647dc78fe5676f",
        ["TestGVARNine.ttf"] = "fa23bfa9bac087a09029802e83c424f09714a94cb547caac098da9d6285e4fbd",
        ["TestGVAR-Composite-0.ttf"] = "5be59bda5b4326b5c62f5b04a8d70fdc2a1a39ede56abb29385ab4b0f068ca95",
        ["TestHVAROne.otf"] = "2682f90cb31d6e5b0dd9e57b8638e297f4e542f3a30a80e620698ae12c6dfdfb",
        ["TestHVARTwo.ttf"] = "dba292b3000989b54c6b61e2ace37addf32132a8dbd3e69215f02cdf09d90bed",
        ["TestRVRN.ttf"] = "3afdfa8efd4a1fdf2a79b98f9a832456d1d168236fbd22da0a60b3492e144d88",
        ["TestRVRN-CFF2.otf"] = "9734b58ae11a8a9a1a5759fe8d79f0856a29e155cfe891aa88e67d08620554b2",
        ["AdobeVFPrototype-Subset.otf"] = "0f7e3b59c2c44ba3683bd636b4f24541ec405f9132b4d37eef1baa78ebb559db",
        ["TestGPOSFour.ttf"] = "281f3412f17191e65fb4f29c60521e745c8d248bea091909a13d7c96c2392795",
    };

    private static readonly ExternalFont[] ExternalFonts =
    [
        new(
            "fonts/noto-sans-regular",
            "fonts/noto-sans/NotoSans-Regular.subset.ttf",
            "NotoSans-Regular.ttf",
            "f3961a9cde016d41a4879aecda1474d3a36d6bf54fa0e4643de029cc2248b0e8",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSans/unhinted/ttf/NotoSans-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/latin-greek-cyrillic)",
            "OFL-1.1",
            ReservedFontName: null,

            // Basic Latin, Latin-1, a few Latin Extended letters, combining marks, punctuation, the euro sign and the
            // fi and fl ligatures.
            Unicodes: "20-7E,A0-FF,131,152,153,160,161,178,17D,17E,2C6,2DC,300-304,308,30A,327,2010-2014,2018-201E,2020-2022,2026,2030,2039,203A,20AC,2122,FB01,FB02"),
        new(
            "fonts/noto-sans-regular-cff",
            "fonts/noto-sans-cff/NotoSans-Regular.subset.otf",
            "NotoSans-Regular.otf",
            "bfb44de2c2031c391ff856943fe8f105ebb5d725cb9a04d493d211d5bbd5c21b",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSans/unhinted/otf/NotoSans-Regular.otf",
            "The Noto Project Authors (https://github.com/notofonts/latin-greek-cyrillic)",
            "OFL-1.1",
            ReservedFontName: null,

            // The characters of the TrueType flavor of the same font.
            Unicodes: "20-7E,A0-FF,131,152,153,160,161,178,17D,17E,2C6,2DC,300-304,308,30A,327,2010-2014,2018-201E,2020-2022,2026,2030,2039,203A,20AC,2122,FB01,FB02"),
        new(
            "fonts/noto-sans-cjk-jp-regular",
            "fonts/noto-sans-cjk/NotoSansCJKjp-Regular.subset.otf",
            "NotoSansCJKjp-Regular.otf",
            "68a3fc98800b2a27b371f2fb79991daf3633bd89309d4ffaa6946fd587f375b5",
            "https://github.com/notofonts/noto-cjk/blob/f8d157532fbfaeda587e826d4cd5b21a49186f7c/Sans/OTF/Japanese/NotoSansCJKjp-Regular.otf",
            "Adobe (https://github.com/notofonts/noto-cjk)",
            "OFL-1.1",
            ReservedFontName: null,

            // A CID-keyed font: Basic Latin, CJK punctuation, the kana, a few ideographs and fullwidth forms, which
            // are in different font DICTs. The hints are kept: they are part of what a charstring reader must skip.
            Unicodes: "20-7E,3001,3002,300C,300D,3041-3096,30A1-30FA,30FC,4E00-4E1F,4EAC,56FD,65E5,6771,672C,8A9E,FF01-FF5E",
            KeepHinting: true),
        new(
            "fonts/noto-sans-variable",
            "fonts/noto-sans-variable/NotoSans-Variable.subset.ttf",
            "NotoSans[wdth,wght].ttf",
            "511cc04b442ffcc3b24d65c2cc780bdffaf482b20ba0aebdc8f27b37017ff0f5",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSans/unhinted/variable-ttf/NotoSans%5Bwdth%2Cwght%5D.ttf",
            "The Noto Project Authors (https://github.com/notofonts/latin-greek-cyrillic)",
            "OFL-1.1",
            ReservedFontName: null,

            // A variable font with TrueType outlines and two axes, the weight and the width: Basic Latin, a few
            // accented letters, combining marks and the fi ligature. The subset keeps every variation table.
            Unicodes: "20-7E,C0,C1,C5,C9,D1,E0,E1,E5,E9,F1,FC,300-304,308,30A,327,2010-2014,2044,20AC,FB01,FB02"),
        new(
            "fonts/noto-sans-cjk-jp-variable",
            "fonts/noto-sans-cjk-variable/NotoSansCJKjp-Variable.subset.otf",
            "NotoSansCJKjp-VF.otf",
            "ab2728702f90d2ae900309f299dc3c2b075010888a1a8a67fbd5b4c6aff713a0",
            "https://github.com/notofonts/noto-cjk/blob/f8d157532fbfaeda587e826d4cd5b21a49186f7c/Sans/Variable/OTF/NotoSansCJKjp-VF.otf",
            "Adobe (https://github.com/notofonts/noto-cjk)",
            "OFL-1.1",
            ReservedFontName: null,

            // A variable font with CFF2 outlines and several font DICTs: Basic Latin, CJK punctuation, a few kana and
            // ideographs. The hints are kept: they are blended too, and a charstring reader must skip them.
            Unicodes: "20-7E,3001,3002,300C,300D,3042,3044,3046,304B,304C,306E,30A2,30AD,30B9,30C6,30C8,30FC,4E00-4E0F,4EAC,56FD,65E5,6771,672C,8A9E",
            KeepHinting: true),

        // Fonts of the text-rendering-tests suite of the Unicode Consortium, as the HarfBuzz project distributes them:
        // each one was made for one feature of a font format. They are stored as they are. Only the fonts whose own
        // license is one the corpus accepts are used.
        TextRenderingFont("trt-avar", "TestAVAR.ttf", "OFL-1.1", "Unicode, Inc."),
        TextRenderingFont("trt-gvar-four", "TestGVARFour.ttf", "OFL-1.1", "Unicode, Inc."),
        TextRenderingFont("trt-gvar-nine", "TestGVARNine.ttf", "Apache-2.0", "Unicode, Inc. (text-rendering-tests)"),
        TextRenderingFont("trt-gvar-composite", "TestGVAR-Composite-0.ttf", "CC0-1.0", "Jens Kutilek (a subset of the Normschrift font, dedicated to the public domain)"),
        TextRenderingFont("trt-hvar-one", "TestHVAROne.otf", "OFL-1.1", "Unicode, Inc."),
        TextRenderingFont("trt-hvar-two", "TestHVARTwo.ttf", "OFL-1.1", "Unicode, Inc."),
        TextRenderingFont("trt-rvrn", "TestRVRN.ttf", "OFL-1.1", "Unicode, Inc."),
        TextRenderingFont("trt-rvrn-cff2", "TestRVRN-CFF2.otf", "OFL-1.1", "Unicode, Inc."),
        TextRenderingFont("trt-cff2", "AdobeVFPrototype-Subset.otf", "OFL-1.1", "Adobe Systems Incorporated"),
        TextRenderingFont("trt-gpos-four", "TestGPOSFour.ttf", "OFL-1.1", "Unicode, Inc."),
    ];

    private static ExternalFont TextRenderingFont(string id, string fileName, string license, string author)
    {
        return new ExternalFont(
            "fonts/" + id,
            "fonts/text-rendering-tests/" + fileName,
            "text-rendering-tests/" + fileName,
            TextRenderingHashes[fileName],
            $"https://github.com/harfbuzz/harfbuzz/blob/{AotsCommit}/test/shape/data/text-rendering-tests/fonts/{fileName}",
            author,
            license,
            ReservedFontName: null,
            Unicodes: null);
    }

    // The fonts that are also stored in the other containers.
    // The Annotated OpenType Specification tests of Adobe, as the HarfBuzz project distributes them: small fonts and, for
    // each one, texts with the glyphs and the positions the specification gives. The directory 'aots' of the fonts
    // directory is test/shape/data/aots of a checkout of HarfBuzz at this commit; only the fonts, the tests and the
    // license are stored.
    private const string AotsCommit = "c7a7457b7385f33178e8cf87615ca077a810bbe7";
    private const string AotsSha256 = "934e916b1b32a88c673b0307865aff1ad22052f6d74619c5e2ca14ea545e92c3";

    // The texts shaped with each font by the reference shaper, as (text, features). The texts are chosen for what
    // shaping does to them: kerning, ligatures, marks that compose or are attached, characters without visible form,
    // spaces and hyphens the font does not have, fractions, features turned on and off.
    private static readonly ShapingText[] LatinShapingCases =
    [
        ("Hello, World!", null),
        ("AVATAR To Ty. V, W; LT Yo r, f.", null),
        ("AVATAR To Ty. V, W; LT Yo r, f.", "-kern"),
        ("office fi fl ffi \uFB01 \uFB02", null),
        ("office fi fl ffi", "-liga"),
        ("e\u0301 a\u0308\u0304 o\u0327\u0301 A\u030A \u00E9 \u00C5", null),
        ("x\u0301\u0327 q\u0308 q\u0327\u0308 i\u0301 j\u0302", null),
        ("e\u0301\u0302\u0303 e\u0303\u0302\u0301", null),
        ("\u0301abc \u0308", null),
        ("a\u200Db a\u200Cb f\u200Ci f\u200Di f\u200Bi a\u00ADb \uFEFFa", null),
        ("1\u20442 12\u204434 a\u2044b 3\u2044", null),
        ("1\u20442 12\u204434", "-frac"),
        ("a\u00A0b\u2002c\u2003d\u2009e\u202Ff\u2007g\u2008h\u200Ai\u205Fj\u3000k", null),
        ("non\u2011breaking \u2010 - \u2013 \u2014", null),
        ("A\uFE00B\uFE0F \u2122\uFE0E", null),
        ("\u4E00x\u4E01 \uFFFD \U0001F600z", null),
        ("\u00C0 \u00C9 \u00D1 \u00FC \u0160 \u017E \u0152 \u0131 \u20AC", null),
        ("The quick brown fox jumps over the lazy dog. 0123456789", null),
        ("T\u0308o V\u0301A A\u0327V", null),
        ("ffi", "liga=0,kern=0"),
        ("H\u2060e\u034Fl\u061Clo", null),
    ];

    private static readonly ShapingText[] CjkShapingCases =
    [
        ("\u65E5\u672C\u8A9E\u306E\u30C6\u30AD\u30B9\u30C8\u3002", null),
        ("\u304B\u3099\u304D\u309A \u30AB\u3099\u30CF\u309A \u304C\u3071", null),
        ("ABC abc 123 To AV", null),
        ("\u300C\u65E5\u672C\u300D\u3001\u6771\u4EAC\u3002\uFF21\uFF22\uFF11", null),
        ("\u4E00\uFE00\u4E01\U000E0100", null),
        ("ABC abc 123 To AV", "-kern,palt"),
    ];

    private static readonly ShapingText[] BoxesShapingCases =
    [
        ("ABA BA AAB", null),
        ("ABA BA AAB", "-kern"),
        ("A\u200BB A\u00ADB", null),
        ("A\u00A0B\u2003C\u2009D", null),
        ("\u00C1 H-\u2010\u2011", null),
        ("AZB", null),
    ];

    // Texts shaped with instances of the variable fonts: the advances, the kerning and the marks of the positioning
    // table vary, and a font can replace a glyph in a part of its variation space.
    private static readonly ShapingText[] VariableLatinShapingCases =
    [
        ("AVATAR To Ty. V, W; LT Yo r, f.", null),
        new("AVATAR To Ty. V, W; LT Yo r, f.", null, "wght=900"),
        new("AVATAR To Ty. V, W; LT Yo r, f.", null, "wght=100,wdth=62.5"),
        new("AVATAR To Ty. V, W; LT Yo r, f.", "-kern", "wght=555,wdth=77"),
        new("e\u0301 a\u0308\u0304 o\u0327\u0301 A\u030A x\u0301\u0327 q\u0308", null, "wght=900"),
        new("e\u0301 a\u0308\u0304 o\u0327\u0301 A\u030A x\u0301\u0327 q\u0308", null, "wght=250,wdth=81.25"),
        new("office fi fl ffi 1\u20442", null, "wght=650,wdth=70"),
        new("The quick brown fox jumps over the lazy dog. 0123456789", null, "wght=333,wdth=91"),
    ];

    private static readonly ShapingText[] VariableCjkShapingCases =
    [
        new("\u65E5\u672C\u8A9E\u306E\u30C6\u30AD\u30B9\u30C8\u3002 ABC To AV", null, "wght=900"),
        new("\u65E5\u672C\u8A9E\u306E\u30C6\u30AD\u30B9\u30C8\u3002 ABC To AV", null, "wght=555"),
        new("ABC abc 123 To AV", "-kern,palt", "wght=700"),
    ];

    private static readonly ShapingText[] VariableBoxesShapingCases =
    [
        ("AB CDEFGH", null),
        new("AB CDEFGH", null, "wght=900"),
        new("AB CDEFGH", null, "wght=100,wdth=200"),
        new("AB CDEFGH", null, "wght=775,wdth=50"),
        new("AB CDEFGH", null, "wght=650,wdth=150"),
    ];

    // A font that replaces glyphs in parts of its variation space (the 'rvrn' feature and the feature variations of
    // its substitution table). The characters are presentation forms: the shaper does not have to join them.
    private static readonly ShapingText[] RvrnShapingCases =
    [
        ("\uFE97\uFEEB\uFEF0 \uFE96\uFEEC\uFEEA \uFE98", null),
        new("\uFE97\uFEEB\uFEF0 \uFE96\uFEEC\uFEEA \uFE98", null, "wght=400"),
        new("\uFE97\uFEEB\uFEF0 \uFE96\uFEEC\uFEEA \uFE98", null, "wght=650"),
        new("\uFE97\uFEEB\uFEF0 \uFE96\uFEEC\uFEEA \uFE98", null, "wght=900"),
        new("\uFE97\uFEEB\uFEF0 \uFE96\uFEEC\uFEEA \uFE98", null, "opsz=10"),
        new("\uFE97\uFEEB\uFEF0 \uFE96\uFEEC\uFEEA \uFE98", null, "opsz=30,wght=500"),
        new("\uFE97\uFEEB\uFEF0 \uFE96\uFEEC\uFEEA \uFE98", "-rvrn", "wght=900"),
    ];

    private static readonly ShapingText[] MarkVariationShapingCases =
    [
        new("\u0634\u0652", null, "wght=100"),
        new("\u0634\u0652", null, "wght=300"),
        new("\u0634\u0652", null, "wght=600"),
        new("\u0634\u0652", null, "wght=700"),
        new("\u0634\u0652", null, "wght=900"),
    ];

    private static readonly Dictionary<string, ShapingText[]> ShapingCases = new(StringComparer.Ordinal)
    {
        ["fonts/noto-sans-variable"] = VariableLatinShapingCases,
        ["fonts/noto-sans-cjk-jp-variable"] = VariableCjkShapingCases,
        ["fonts/synthetic-variable"] = VariableBoxesShapingCases,
        ["fonts/synthetic-variable-phantom"] = VariableBoxesShapingCases,
        ["fonts/noto-sans-regular"] = LatinShapingCases,
        ["fonts/noto-sans-regular-cff"] = LatinShapingCases,
        ["fonts/noto-sans-cjk-jp-regular"] = CjkShapingCases,
        ["fonts/synthetic-boxes"] = BoxesShapingCases,
        ["fonts/trt-rvrn"] = RvrnShapingCases,
        ["fonts/trt-rvrn-cff2"] = RvrnShapingCases,
        ["fonts/trt-gpos-four"] = MarkVariationShapingCases,
    };

    // The WOFF2 files are written by the reference encoder, except the variants: it does not transform the horizontal
    // metrics of these fonts, and it chooses how each point is encoded.
    private static readonly Container[] Containers =
    [
        new("fonts/synthetic-boxes", "woff"),
        new("fonts/synthetic-boxes", "woff2"),
        new("fonts/synthetic-boxes", "woff2", "metrics", static () => FontWoff2Writer.Write(FontSamples.Boxes(), new FontWoff2Options { HorizontalMetricsFlags = 3 }), "the glyphs and the horizontal metrics transformed"),
        new("fonts/synthetic-curves", "woff2"),
        new("fonts/synthetic-collection", "woff2"),
        new("fonts/synthetic-deltas", "woff"),
        new("fonts/synthetic-deltas", "woff2"),
        new("fonts/synthetic-deltas", "woff2", "boxes", static () => FontWoff2Writer.Write(FontSamples.Deltas(), new FontWoff2Options { ExplicitBoundingBoxes = true, HorizontalMetricsFlags = 1 }), "every bounding box stored, the horizontal metrics transformed"),
        new("fonts/synthetic-charstrings", "woff"),
        new("fonts/synthetic-charstrings", "woff2"),
        new("fonts/noto-sans-cjk-jp-regular", "woff"),
        new("fonts/noto-sans-regular-cff", "woff2"),
        new("fonts/noto-sans-regular", "woff"),
        new("fonts/noto-sans-regular", "woff2"),
    ];

    public static int Run(GeneratorOptions options)
    {
        var tools = new Tools();
        if (tools.HarfBuzzVersion != PinnedHarfBuzz && !options.AcceptToolVersions)
            throw new FatalException($"The HarfBuzz utilities are version '{tools.HarfBuzzVersion}' but the corpus is pinned to {PinnedHarfBuzz}. Review the differences, then rerun with --accept-tool-versions.");

        if (tools.Woff2Version != PinnedWoff2 && !options.AcceptToolVersions)
            throw new FatalException($"The woff2 tools are version '{tools.Woff2Version}' but the corpus is pinned to {PinnedWoff2}. Review the differences, then rerun with --accept-tool-versions.");

        var fontsDirectory = FullPath.FromPath(options.Fonts!);
        return CorpusDriver.Run(options, "mfi-font-corpus-", output => Generate(output, fontsDirectory, tools),
            (count, files, bytes) => $"{count} fonts, {files} files, {bytes} bytes",
            "The committed font corpus is reproducible.");
    }

    private static int Generate(FullPath outDir, FullPath fontsDirectory, Tools tools)
    {
        // Every other part of the corpus is kept as committed: this generator owns the fonts only.
        var manifest = CorpusDriver.LoadCommittedManifest();
        CorpusDriver.CopyCommittedFiles(outDir, CorpusDriver.Fixtures(manifest));
        CorpusDriver.CopyCommittedAv1Vectors(outDir, manifest);
        CorpusDriver.PreserveColorSections(outDir, manifest);

        var work = FullPath.FromPath(Directory.CreateTempSubdirectory("mfi-font-work-").FullName);
        try
        {
            var entries = new List<Obj>();
            var files = new Dictionary<string, (string Path, byte[] Data, Obj Provenance, int FaceCount)>(StringComparer.Ordinal);
            foreach (var font in SyntheticFonts)
            {
                var data = font.Build();
                var provenance = new Obj
                {
                    ["origin"] = "generated",
                    ["license"] = "CC0-1.0",
                    ["author"] = "Meziantou.Framework contributors",
                    ["generator"] = $"{SamplesPath} ({font.Function})",
                    ["tools"] = new List<string> { ToolSet.RuntimeLabel },
                };
                files.Add(font.Id, (font.Path, data, provenance, font.FaceCount));
                entries.Add(CreateEntry(outDir, work, tools, font.Id, font.Path, data, provenance, font.Flavor, font.FaceCount, "none", reservedFontName: null, original: null, withVectors: true));
            }

            foreach (var font in ExternalFonts)
            {
                var source = File.ReadAllBytes(fontsDirectory / font.FileName);
                var hash = Bytes.Sha256Hex(source);
                if (hash != font.Sha256)
                    throw new FatalException($"{font.FileName} has sha256 {hash} but {font.Sha256} is pinned ({font.Source}).");

                var data = source;
                var modification = "none";
                var provenance = new Obj
                {
                    ["origin"] = "external",
                    ["license"] = font.License,
                    ["author"] = font.Author,
                    ["source"] = font.Source,
                };
                if (font.Unicodes is not null)
                {
                    if (font.ReservedFontName is not null)
                        throw new FatalException($"{font.FileName} has a Reserved Font Name: it cannot be subset.");

                    // Every layout feature is kept, with the glyphs it needs; the hinting instructions are dropped.
                    var input = work / font.FileName;
                    var output = work / ("subset-" + font.FileName);
                    File.WriteAllBytes(input, source);
                    string[] arguments = font.KeepHinting
                        ? ["--unicodes=" + font.Unicodes, "--layout-features=*", "--notdef-outline"]
                        : ["--unicodes=" + font.Unicodes, "--layout-features=*", "--no-hinting", "--notdef-outline"];
                    Proc.Run([tools.HbSubset, .. arguments, "--output-file=" + output, input]);
                    data = File.ReadAllBytes(output);
                    modification = "subset";
                    provenance["tools"] = new List<string> { tools.HarfBuzzLabel("hb-subset") };
                    provenance["commands"] = new List<string> { $"hb-subset {string.Join(' ', arguments)} --output-file={Path.GetFileName(font.Path)} {font.FileName}" };
                }

                files.Add(font.Id, (font.Path, data, provenance, 1));
                entries.Add(CreateEntry(outDir, work, tools, font.Id, font.Path, data, provenance, GetFlavor(data), 1, modification, font.ReservedFontName, original: null, withVectors: true));
            }

            foreach (var container in Containers)
            {
                var (originalId, flavor) = (container.Original, container.Flavor);
                var (originalPath, originalData, originalProvenance, faceCount) = files[originalId];
                var path = Path.ChangeExtension(originalPath, (container.Variant is null ? "." : "." + container.Variant + ".") + flavor);
                var provenance = new Obj();
                foreach (var (key, value) in originalProvenance)
                {
                    if (key is not ("tools" or "commands" or "generator"))
                    {
                        provenance[key] = value;
                    }
                }

                byte[] data;
                if (container.Build is not null)
                {
                    data = container.Build();
                    provenance["generator"] = $"{WriterPath} ({container.Description})";
                    provenance["tools"] = new List<string> { ToolSet.RuntimeLabel };
                }
                else if (flavor == "woff")
                {
                    data = FontBuilder.WriteWoff(originalData);
                    provenance["generator"] = $"{ScriptPath} (FontBuilder.WriteWoff of {originalPath})";
                    provenance["tools"] = new List<string> { ToolSet.RuntimeLabel };
                }
                else
                {
                    var input = work / ("container-" + Path.GetFileName(originalPath));
                    File.WriteAllBytes(input, originalData);
                    Proc.Run([tools.Woff2Compress, input]);
                    data = File.ReadAllBytes(input.ChangeExtension(".woff2"));
                    provenance["generator"] = $"{ScriptPath} (woff2_compress of {originalPath})";
                    provenance["tools"] = new List<string> { tools.Woff2Label };
                    provenance["commands"] = new List<string> { "woff2_compress " + Path.GetFileName(originalPath) };
                }

                if ((string)originalProvenance["origin"]! == "external")
                {
                    // The validator requires a generator for generated files only; the origin stays the one of the font.
                    provenance.Remove("generator");
                }

                CheckContainer(work, tools, originalData, data, flavor, faceCount);
                entries.Add(CreateEntry(outDir, work, tools, originalId + "-" + flavor + (container.Variant is null ? "" : "-" + container.Variant), path, data, provenance, flavor, faceCount, "container", reservedFontName: null, originalId, withVectors: false));
            }

            var duplicate = entries.GroupBy(entry => (string)((Obj)entry["file"]!)["path"]!, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
            if (duplicate is not null)
                throw new FatalException($"Two fonts of the corpus are written to {duplicate.Key}.");

            manifest[CorpusDriver.FontSuitesKey] = new List<object?> { CopyAots(outDir, fontsDirectory / "aots") };
            manifest["schemaVersion"] = CorpusDriver.SchemaVersion;
            manifest[CorpusDriver.FontsKey] = entries.OrderBy(entry => (string)entry["id"]!, StringComparer.Ordinal).Cast<object?>().ToList();
            CorpusDriver.WriteManifest(outDir, manifest);
            return entries.Count;
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    /// <summary>Copies the fonts, the tests and the license of the AOTS suite, and describes them.</summary>
    private static Obj CopyAots(FullPath outDir, FullPath source)
    {
        const string Directory = "font-suites/aots";
        if (!System.IO.Directory.Exists(source / "fonts") || !System.IO.Directory.Exists(source / "tests"))
            throw new FatalException($"The directory {source} is not test/shape/data/aots of a checkout of HarfBuzz at the commit {AotsCommit}.");

        var target = outDir / Directory;
        foreach (var file in System.IO.Directory.EnumerateFiles(source / "fonts", "*.otf").Concat(System.IO.Directory.EnumerateFiles(source / "tests", "*.tests")).Append(source / "COPYING"))
        {
            var destination = target / FullPath.FromPath(file).MakePathRelativeTo(source);
            destination.CreateParentDirectory();
            File.Copy(file, destination);
        }

        var hash = ComputeDirectoryHash(target, out var fileCount);
        if (hash != AotsSha256)
            throw new FatalException($"The AOTS suite has the hash {hash} ({fileCount} files) but {AotsSha256} is pinned for the commit {AotsCommit}.");

        return new Obj
        {
            ["id"] = Directory,
            ["directory"] = Directory,
            ["provenance"] = new Obj
            {
                ["origin"] = "external",
                ["license"] = "Apache-2.0",
                ["author"] = "Adobe Systems Incorporated (Annotated OpenType Specification), as distributed by the HarfBuzz project",
                ["source"] = $"https://github.com/harfbuzz/harfbuzz/tree/{AotsCommit}/test/shape/data/aots",
            },
            ["fileCount"] = fileCount,
            ["sha256"] = hash,
            ["description"] = "Fonts that each exercise one format of the cmap, class definition, coverage, GSUB or GPOS tables, and tests: a font, the options of hb-shape, characters, and the expected glyphs and positions.",
        };
    }

    /// <summary>
    /// Computes the hash that identifies the files of a suite: the SHA-256 of, for each file by ordinal order of its
    /// relative path, the path, a line feed, the hexadecimal SHA-256 of its content, a line feed.
    /// </summary>
    private static string ComputeDirectoryHash(FullPath directory, out int fileCount)
    {
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(file => FullPath.FromPath(file).MakePathRelativeTo(directory).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
        fileCount = files.Count;
        var listing = new StringBuilder();
        foreach (var file in files)
        {
            listing.Append(file).Append('\n').Append(Bytes.Sha256Hex(File.ReadAllBytes(directory / file))).Append('\n');
        }

        return Bytes.Sha256Hex(Encoding.UTF8.GetBytes(listing.ToString()));
    }

    private static Obj CreateEntry(FullPath outDir, FullPath work, Tools tools, string id, string path, byte[] data, Obj provenance, string flavor, int faceCount, string modification, string? reservedFontName, string? original, bool withVectors)
    {
        var target = outDir / path;
        target.CreateParentDirectory();
        File.WriteAllBytes(target, data);

        var entry = new Obj
        {
            ["id"] = id,
            ["file"] = new Obj { ["path"] = path, ["sha256"] = Bytes.Sha256Hex(data), ["role"] = "font" },
            ["provenance"] = provenance,
            ["flavor"] = flavor,
            ["faceCount"] = faceCount,
            ["modification"] = modification,
        };
        if (reservedFontName is not null)
        {
            entry["reservedFontName"] = reservedFontName;
        }

        if (original is not null)
        {
            entry["original"] = original;
        }

        if (withVectors)
        {
            entry["features"] = GetFeatures(data);
            var faces = new List<object?>();
            for (var face = 0; face < faceCount; face++)
            {
                faces.Add(RecordFace(work, tools, data, face, Instances.GetValueOrDefault(id) ?? []));
            }

            var vectors = new Obj
            {
                ["tools"] = new List<string> { tools.HarfBuzzLabel("hb-info"), tools.HarfBuzzLabel("hb-shape") + " (--font-funcs=ot and ft)", tools.HarfBuzzLabel("hb-vector") + " (--font-funcs=ot and ft)" },
                ["faces"] = faces,
            };
            var vectorsPath = "fonts/vectors/" + id["fonts/".Length..] + ".json";
            var vectorsData = Encoding.UTF8.GetBytes(PyJson.Dumps(vectors) + "\n");
            var vectorsTarget = outDir / vectorsPath;
            vectorsTarget.CreateParentDirectory();
            File.WriteAllBytes(vectorsTarget, vectorsData);
            entry["vectors"] = new Obj { ["path"] = vectorsPath, ["sha256"] = Bytes.Sha256Hex(vectorsData), ["role"] = "font-vectors" };
        }

        if (ShapingCases.TryGetValue(id, out var shapingCases))
        {
            var shapingPath = "fonts/shaping/" + id["fonts/".Length..] + ".json";
            var shapingData = Encoding.UTF8.GetBytes(PyJson.Dumps(RecordShaping(work, tools, data, shapingCases)) + "\n");
            var shapingTarget = outDir / shapingPath;
            shapingTarget.CreateParentDirectory();
            File.WriteAllBytes(shapingTarget, shapingData);
            entry["shaping"] = new Obj { ["path"] = shapingPath, ["sha256"] = Bytes.Sha256Hex(shapingData), ["role"] = "shaping-vectors" };
        }

        return entry;
    }

    /// <summary>Records what the two font readers say of a face.</summary>
    private static Obj RecordFace(FullPath work, Tools tools, byte[] data, int face, string[] instances)
    {
        var file = work / "record.font";
        File.WriteAllBytes(file, data);
        string[] faceArguments = ["--face-index=" + face.ToString(CultureInfo.InvariantCulture)];

        var info = ParseKeyValues(Proc.RunText([tools.HbInfo, .. faceArguments, file]));
        var glyphCount = int.Parse(info["Glyph count"], CultureInfo.InvariantCulture);
        var metrics = RecordMetrics(tools, file, faceArguments);

        // The character map without shaping: code point to glyph name, then glyph name to glyph identifier.
        var glyphIds = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in Proc.RunText([tools.HbInfo, .. faceArguments, "--list-glyphs", file]).Split('\n'))
        {
            var parts = line.Split('\t');
            if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                if (!glyphIds.TryAdd(parts[1], id))
                    throw new FatalException($"The glyph name '{parts[1]}' is used twice: the character map cannot be recorded.");
            }
        }

        if (glyphIds.Count != glyphCount)
            throw new FatalException($"hb-info lists {glyphIds.Count} glyphs but the font has {glyphCount}.");

        var characterMap = new Obj();
        foreach (var line in Proc.RunText([tools.HbInfo, .. faceArguments, "--list-unicodes", file]).Split('\n'))
        {
            var parts = line.Split('\t');
            if (parts.Length == 2 && parts[0].StartsWith("U+", StringComparison.Ordinal))
            {
                characterMap[int.Parse(parts[0].AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture).ToString("X4", CultureInfo.InvariantCulture)] = glyphIds[parts[1]];
            }
        }

        if (characterMap.Count != int.Parse(info["Unicode count"], CultureInfo.InvariantCulture))
            throw new FatalException("hb-info does not list as many characters as it counts.");

        // The advances of every glyph, without shaping, from the two font readers.
        var advances = RecordAdvances(tools, file, faceArguments, glyphCount);
        var outlines = new List<string>();
        for (var glyph = 0; glyph < glyphCount; glyph++)
        {
            var text = glyph.ToString(CultureInfo.InvariantCulture);
            var withOt = GetOutline(Proc.RunText([tools.HbVector, .. faceArguments, "--font-funcs=ot", "--glyphs", file, text]));
            var withFt = GetOutline(Proc.RunText([tools.HbVector, .. faceArguments, "--font-funcs=ft", "--glyphs", file, text]));
            if (withOt != withFt)
                throw new FatalException($"The two font readers disagree on the outline of the glyph {glyph}:\n  ot: {withOt}\n  ft: {withFt}");

            outlines.Add(withOt);
        }

        var result = new Obj
        {
            ["family"] = info["Family"],
            ["subfamily"] = info["Subfamily"],
        };
        if (info.TryGetValue("Postscript name", out var postScriptName) && postScriptName.Length > 0)
        {
            result["postScriptName"] = postScriptName;
        }

        result["unitsPerEm"] = int.Parse(info["Units-Per-EM"], CultureInfo.InvariantCulture);
        result["glyphCount"] = glyphCount;
        result["metrics"] = metrics;
        result["characterMap"] = characterMap;
        result["advances"] = advances.Cast<object?>().ToList();
        result["outlines"] = outlines.Cast<object?>().ToList();

        // The instances of a variable font. The advances are whole font units, on which the two readers must agree.
        // The points of an instance are fractions of font units: HarfBuzz keeps them, with the precision of single
        // precision numbers; FreeType rounds the deltas of the offsets of the components, works in 1/64 of a unit for
        // TrueType outlines, and gives whole units for the blended charstrings of a CFF2 table, whose errors add up
        // along a contour. The outlines of HarfBuzz are recorded, to three decimals, once FreeType has drawn the same
        // segments within two units.
        if (instances.Length > 0)
        {
            var records = new List<object?>();
            foreach (var instance in instances)
            {
                string[] arguments = [.. faceArguments, "--variations=" + instance];
                var instanceOutlines = new List<object?>();
                for (var glyph = 0; glyph < glyphCount; glyph++)
                {
                    var text = glyph.ToString(CultureInfo.InvariantCulture);
                    var withOt = GetOutline(Proc.RunText([tools.HbVector, .. arguments, "--font-funcs=ot", "--precision=3", "--glyphs", file, text]));
                    var withFt = GetOutline(Proc.RunText([tools.HbVector, .. arguments, "--font-funcs=ft", "--precision=3", "--glyphs", file, text]));
                    if (!AreClose(withOt, withFt, tolerance: 2.0))
                        throw new FatalException($"The two font readers disagree on the outline of the glyph {glyph} of the instance {instance}:\n  ot: {withOt}\n  ft: {withFt}");

                    instanceOutlines.Add(withOt);
                }

                records.Add(new Obj
                {
                    ["variations"] = instance,
                    ["metrics"] = RecordMetrics(tools, file, arguments),
                    ["advances"] = RecordAdvances(tools, file, arguments, glyphCount).Cast<object?>().ToList(),
                    ["outlines"] = instanceOutlines,
                });
            }

            result["instances"] = records;
        }

        return result;
    }

    private static Obj RecordMetrics(Tools tools, FullPath file, string[] arguments)
    {
        var metrics = new Obj();
        foreach (var line in Proc.RunText([tools.HbInfo, .. arguments, "--list-metrics", file]).Split('\n'))
        {
            // "hasc: horizontal-ascender<tabs>832<tab>": a value marked with * is a fallback the font does not declare.
            var match = MetricRegex().Match(line);
            if (match.Success && MetricTags.Contains(match.Groups["tag"].Value, StringComparer.Ordinal) && !match.Groups["fallback"].Success)
            {
                metrics[match.Groups["tag"].Value] = int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
            }
        }

        return metrics;
    }

    private static List<int> RecordAdvances(Tools tools, FullPath file, string[] arguments, int glyphCount)
    {
        var advances = new List<int>();
        for (var start = 0; start < glyphCount; start += 256)
        {
            var glyphs = "[" + string.Join('|', Enumerable.Range(start, Math.Min(256, glyphCount - start)).Select(id => id.ToString(CultureInfo.InvariantCulture))) + "]";
            var withOt = Proc.RunText([tools.HbShape, .. arguments, "--font-funcs=ot", "--glyphs", "--output-format=json", "--no-glyph-names", file, glyphs]);
            var withFt = Proc.RunText([tools.HbShape, .. arguments, "--font-funcs=ft", "--glyphs", "--output-format=json", "--no-glyph-names", file, glyphs]);
            if (withOt != withFt)
                throw new FatalException($"The two font readers disagree on the advances of the glyphs ({string.Join(' ', arguments)}):\n  ot: {withOt}\n  ft: {withFt}");

            foreach (Match match in AdvanceRegex().Matches(withOt))
            {
                advances.Add(int.Parse(match.Groups["advance"].Value, CultureInfo.InvariantCulture));
            }
        }

        if (advances.Count != glyphCount)
            throw new FatalException($"hb-shape gives {advances.Count} advances for {glyphCount} glyphs.");

        return advances;
    }

    /// <summary>Tells whether two outlines are the same commands with numbers that differ by at most a tolerance.</summary>
    private static bool AreClose(string left, string right, double tolerance)
    {
        var leftNumbers = NumberRegex().Matches(left);
        var rightNumbers = NumberRegex().Matches(right);
        if (leftNumbers.Count != rightNumbers.Count || NumberRegex().Replace(left, "#") != NumberRegex().Replace(right, "#"))
            return false;

        for (var i = 0; i < leftNumbers.Count; i++)
        {
            if (Math.Abs(double.Parse(leftNumbers[i].Value, CultureInfo.InvariantCulture) - double.Parse(rightNumbers[i].Value, CultureInfo.InvariantCulture)) > tolerance)
                return false;
        }

        return true;
    }

    /// <summary>Shapes texts with the OpenType shaper of HarfBuzz and its own font functions, and records the glyphs.</summary>
    private static Obj RecordShaping(FullPath work, Tools tools, byte[] font, ShapingText[] cases)
    {
        var fontFile = work / "shaping.font";
        File.WriteAllBytes(fontFile, font);
        var results = new List<object?>();
        foreach (var (text, features, variations) in cases)
        {
            var codePoints = text.EnumerateRunes().Select(rune => rune.Value.ToString("X4", CultureInfo.InvariantCulture)).ToList();
            List<string> arguments = ["--shapers=ot", "--font-funcs=ot", "--output-format=json", "--no-glyph-names"];
            if (features is not null)
            {
                arguments.Add("--features=" + features);
            }

            if (variations is not null)
            {
                arguments.Add("--variations=" + variations);
            }

            arguments.Add("--unicodes=" + string.Join(',', codePoints.Select(codePoint => "U+" + codePoint)));
            var output = Proc.RunText([tools.HbShape, .. arguments, fontFile]);
            var glyphs = new List<object?>();
            using (var document = JsonDocument.Parse(output))
            {
                foreach (var glyph in document.RootElement.EnumerateArray())
                {
                    glyphs.Add(new List<object?> { glyph.GetProperty("g").GetInt32(), glyph.GetProperty("cl").GetInt32(), glyph.GetProperty("dx").GetInt32(), glyph.GetProperty("dy").GetInt32(), glyph.GetProperty("ax").GetInt32(), glyph.GetProperty("ay").GetInt32() });
                }
            }

            var result = new Obj { ["text"] = string.Join(' ', codePoints) };
            if (features is not null)
            {
                result["features"] = features;
            }

            if (variations is not null)
            {
                result["variations"] = variations;
            }

            result["glyphs"] = glyphs;
            results.Add(result);
        }

        return new Obj
        {
            ["tool"] = tools.HarfBuzzLabel("hb-shape") + " --shapers=ot --font-funcs=ot",
            ["cases"] = results,
        };
    }

    /// <summary>Checks that a container gives, through FreeType, the advances and the outlines of the font it wraps.</summary>
    private static void CheckContainer(FullPath work, Tools tools, byte[] original, byte[] container, string flavor, int faceCount)
    {
        var originalFile = work / "check-original.font";
        var containerFile = work / ("check-container." + flavor);
        File.WriteAllBytes(originalFile, original);
        File.WriteAllBytes(containerFile, container);
        if (flavor == "woff2")
        {
            // The reference decoder accepts the file.
            Proc.Run([tools.Woff2Decompress, containerFile]);
        }

        for (var face = 0; face < faceCount; face++)
        {
            string[] faceArguments = ["--face-index=" + face.ToString(CultureInfo.InvariantCulture)];
            var glyphCount = int.Parse(ParseKeyValues(Proc.RunText([tools.HbInfo, .. faceArguments, originalFile]))["Glyph count"], CultureInfo.InvariantCulture);
            var glyphs = "[" + string.Join('|', Enumerable.Range(0, Math.Min(256, glyphCount)).Select(id => id.ToString(CultureInfo.InvariantCulture))) + "]";
            var expectedAdvances = Proc.RunText([tools.HbShape, .. faceArguments, "--font-funcs=ft", "--glyphs", "--output-format=json", "--no-glyph-names", originalFile, glyphs]);
            var actualAdvances = Proc.RunText([tools.HbShape, .. faceArguments, "--face-loader=ft", "--font-funcs=ft", "--glyphs", "--output-format=json", "--no-glyph-names", containerFile, glyphs]);
            if (expectedAdvances != actualAdvances)
                throw new FatalException($"The {flavor} form does not give the advances of the original font.");

            for (var glyph = 0; glyph < glyphCount; glyph++)
            {
                var text = glyph.ToString(CultureInfo.InvariantCulture);
                var expected = GetOutline(Proc.RunText([tools.HbVector, .. faceArguments, "--font-funcs=ft", "--glyphs", originalFile, text]));
                var actual = GetOutline(Proc.RunText([tools.HbVector, .. faceArguments, "--face-loader=ft", "--font-funcs=ft", "--glyphs", containerFile, text]));
                if (expected != actual)
                    throw new FatalException($"The {flavor} form does not give the outline of the glyph {glyph} of the face {face} of the original font:\n  original: {expected}\n  {flavor}: {actual}");
            }
        }
    }

    private static string GetOutline(string svg)
    {
        // One path per glyph that has an outline; the path data is in font units, with the y axis up.
        var matches = PathRegex().Matches(svg);
        return matches.Count switch
        {
            0 => "",
            1 => matches[0].Groups["data"].Value,
            _ => throw new FatalException("hb-vector drew more than one path for one glyph."),
        };
    }

    private static Dictionary<string, string> ParseKeyValues(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var separator = line.IndexOf(": ", StringComparison.Ordinal);
            if (separator > 0)
            {
                result.TryAdd(line[..separator], line[(separator + 2)..].TrimEnd());
            }
            else if (line.Length > 0 && line[^1] == ':')
            {
                result.TryAdd(line[..^1], "");
            }
        }

        return result;
    }

    private static string GetFlavor(ReadOnlySpan<byte> data)
    {
        return BinaryPrimitives.ReadUInt32BigEndian(data) switch
        {
            0x00010000 or 0x74727565 => "ttf",
            0x4F54544F => "otf",
            0x74746366 => "ttc",
            0x774F4646 => "woff",
            0x774F4632 => "woff2",
            var signature => throw new FatalException($"Unknown font signature 0x{signature:X8}."),
        };
    }

    /// <summary>The feature tags of a font, read from its table directory and from the header of its character map.</summary>
    private static List<string> GetFeatures(ReadOnlySpan<byte> data)
    {
        var features = new SortedSet<string>(StringComparer.Ordinal);
        var faceOffsets = new List<int>();
        if (GetFlavor(data) == "ttc")
        {
            var count = (int)BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
            for (var i = 0; i < count; i++)
            {
                faceOffsets.Add((int)BinaryPrimitives.ReadUInt32BigEndian(data[(12 + (i * 4))..]));
            }

            features.Add("font.container=ttc");
        }
        else
        {
            faceOffsets.Add(0);
        }

        foreach (var faceOffset in faceOffsets)
        {
            var tableCount = BinaryPrimitives.ReadUInt16BigEndian(data[(faceOffset + 4)..]);

            // The format of the glyph locations only means something for a font that has them.
            var hasLocations = false;
            for (var i = 0; i < tableCount; i++)
            {
                hasLocations |= data.Slice(faceOffset + 12 + (i * 16), 4).SequenceEqual("loca"u8);
            }

            for (var i = 0; i < tableCount; i++)
            {
                var record = data.Slice(faceOffset + 12 + (i * 16), 16);
                var tag = Encoding.ASCII.GetString(record[..4]);
                var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
                if (NotableTables.Contains(tag, StringComparer.Ordinal))
                {
                    features.Add("font.table=" + tag.TrimEnd());
                }

                if (tag == "cmap")
                {
                    var cmap = data[offset..];
                    var subtableCount = BinaryPrimitives.ReadUInt16BigEndian(cmap[2..]);
                    for (var j = 0; j < subtableCount; j++)
                    {
                        var subtable = (int)BinaryPrimitives.ReadUInt32BigEndian(cmap[(4 + (j * 8) + 4)..]);
                        features.Add("font.cmap=" + BinaryPrimitives.ReadUInt16BigEndian(cmap[subtable..]).ToString(CultureInfo.InvariantCulture));
                    }
                }
                else if (tag == "head" && hasLocations)
                {
                    features.Add("font.loca=" + (BinaryPrimitives.ReadInt16BigEndian(data[(offset + 50)..]) == 0 ? "short" : "long"));
                }
            }
        }

        return [.. features];
    }

    [GeneratedRegex(@"^(?<tag>[A-Za-z0-9]{4}): \S+\s+(?<value>-?[0-9]+)\s*(?<fallback>\*)?\s*$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex MetricRegex();

    [GeneratedRegex("\"ax\":(?<advance>-?[0-9]+)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex AdvanceRegex();

    [GeneratedRegex("<path d=\"(?<data>[^\"]*)\"", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex PathRegex();

    [GeneratedRegex(@"-?[0-9]+(\.[0-9]+)?", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\(HarfBuzz\) (?<version>\S+)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex HarfBuzzVersionRegex();

    /// <summary>A text to shape, with the features and the instance of a variable font it is shaped with.</summary>
    private sealed record ShapingText(string Text, string? Features, string? Variations = null)
    {
        public static implicit operator ShapingText((string Text, string? Features) value) => new(value.Text, value.Features);
    }

    private sealed record Container(string Original, string Flavor, string? Variant = null, Func<byte[]>? Build = null, string? Description = null);

    private sealed record SyntheticFont(string Id, string Path, Func<byte[]> Build, string Function, string Flavor, int FaceCount);

    private sealed record ExternalFont(string Id, string Path, string FileName, string Sha256, string Source, string Author, string License, string? ReservedFontName, string? Unicodes, bool KeepHinting = false);

    private sealed class Tools
    {
        public Tools()
        {
            HbShape = Download(PrebuiltTools.HbShape);
            HbInfo = Download(PrebuiltTools.HbInfo);
            HbSubset = Download(PrebuiltTools.HbSubset);
            HbVector = Download(PrebuiltTools.HbVector);
            Woff2Compress = Download(PrebuiltTools.Woff2Compress);
            Woff2Decompress = Download(PrebuiltTools.Woff2Decompress);
            HarfBuzzVersion = HarfBuzzVersionRegex().Match(Proc.RunText([HbShape, "--version"])).Groups["version"].Value;

            // The woff2 tools print no version: the one of the pinned release is the only statement there is.
            Woff2Version = PrebuiltTools.Woff2Compress.Version;
        }

        public string HbShape { get; }

        public string HbInfo { get; }

        public string HbSubset { get; }

        public string HbVector { get; }

        public string Woff2Compress { get; }

        public string Woff2Decompress { get; }

        public string HarfBuzzVersion { get; }

        public string Woff2Version { get; }

        public string Woff2Label => "woff2 " + Woff2Version;

        public string HarfBuzzLabel(string tool) => tool + " " + HarfBuzzVersion;

        private static string Download(PrebuiltTool tool) => tool.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
}
