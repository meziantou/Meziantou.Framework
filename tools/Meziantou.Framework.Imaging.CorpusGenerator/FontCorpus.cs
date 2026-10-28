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
        new("fonts/synthetic-arabic-forms", "fonts/synthetic/arabic-forms.ttf", static () => FontSamples.ArabicForms().Build(), nameof(FontSamples.ArabicForms), "ttf", 1),
        new("fonts/synthetic-thai-forms", "fonts/synthetic/thai-forms.ttf", static () => FontSamples.ThaiForms().Build(), nameof(FontSamples.ThaiForms), "ttf", 1),
        new("fonts/synthetic-thai-forms-mac", "fonts/synthetic/thai-forms.mac.ttf", static () => FontSamples.ThaiForms(useMacCodePoints: true).Build(), nameof(FontSamples.ThaiForms) + " with the code points of the fonts of Mac OS", "ttf", 1),
        new("fonts/synthetic-marks", "fonts/synthetic/marks.ttf", static () => FontSamples.Marks().Build(), nameof(FontSamples.Marks), "ttf", 1),
        new("fonts/synthetic-monochrome-boxes", "fonts/synthetic/monochrome-boxes.ttf", static () => FontSamples.MonochromeBoxes().Build(), nameof(FontSamples.MonochromeBoxes), "ttf", 1),
        new("fonts/synthetic-languages", "fonts/synthetic/languages.ttf", static () => FontSamples.Languages().Build(), nameof(FontSamples.Languages), "ttf", 1),
        new("fonts/synthetic-variable-axes", "fonts/synthetic/variable.axes.ttf", static () => FontSamples.Variable(withAxisVariations: true).Build(), nameof(FontSamples.Variable) + " with version 2 of the avar table", "ttf", 1),
        new("fonts/synthetic-variable-charstrings", "fonts/synthetic/variable-charstrings.otf", static () => FontSamples.VariableCharstrings().Build(), nameof(FontSamples.VariableCharstrings), "otf", 1),

        // The color fonts have no outline vectors: the reference tool draws their glyphs with their paints. They have
        // renders instead.
        new("fonts/synthetic-color-boxes", "fonts/synthetic/color-boxes.ttf", static () => FontSamples.ColorBoxes().Build(), nameof(FontSamples.ColorBoxes), "ttf", 1, HasOutlineVectors: false),
        new("fonts/synthetic-color-transforms", "fonts/synthetic/color-transforms.ttf", static () => FontSamples.ColorTransforms().Build(), nameof(FontSamples.ColorTransforms), "ttf", 1, HasOutlineVectors: false),
        new("fonts/synthetic-color-gradients", "fonts/synthetic/color-gradients.ttf", static () => FontSamples.ColorGradients().Build(), nameof(FontSamples.ColorGradients), "ttf", 1, HasOutlineVectors: false),
        new("fonts/synthetic-color-clips", "fonts/synthetic/color-clips.ttf", static () => FontSamples.ColorClips().Build(), nameof(FontSamples.ColorClips), "ttf", 1, HasOutlineVectors: false),
        new("fonts/synthetic-bitmap-boxes", "fonts/synthetic/bitmap-boxes.ttf", static () => FontSamples.BitmapBoxes().Build(), nameof(FontSamples.BitmapBoxes), "ttf", 1, HasOutlineVectors: false),
        new("fonts/synthetic-color-bitmaps", "fonts/synthetic/color-bitmaps.ttf", static () => FontSamples.BitmapBoxes(useColorBitmapTables: true).Build(), nameof(FontSamples.BitmapBoxes) + " with CBLC and CBDT tables", "ttf", 1, HasOutlineVectors: false),
    ];

    // The instances of the variable fonts whose advances, outlines and metrics are recorded, in the syntax of the
    // reference tools. The default instance is always recorded.
    private static readonly string[] SyntheticInstances = ["wght=900", "wght=100", "wght=650", "wght=775", "wdth=200", "wdth=50", "wdth=125", "wght=900,wdth=200", "wght=775,wdth=150", "wght=250,wdth=75", "wght=1000,wdth=10"];

    private static readonly Dictionary<string, string[]> Instances = new(StringComparer.Ordinal)
    {
        ["fonts/synthetic-variable"] = SyntheticInstances,
        ["fonts/synthetic-variable-phantom"] = SyntheticInstances,

        // Where the deltas of the axes apply fully, partly, not at all, and where their sum leaves the range of an axis.
        ["fonts/synthetic-variable-axes"] = ["wght=900,wdth=200", "wdth=50", "wght=900,wdth=50", "wght=650,wdth=150", "wght=100,wdth=75", "wght=900", "wdth=200", "wght=775,wdth=125"],
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

        new(
            "fonts/noto-naskh-arabic",
            "fonts/noto-naskh-arabic/NotoNaskhArabic-Regular.subset.ttf",
            "NotoNaskhArabic-Regular.ttf",
            "56353fb25c9c1fbcdc6f3faa9f9b5552d4914316dbc811a13e7211570b4c9669",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoNaskhArabic/unhinted/ttf/NotoNaskhArabic-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/arabic)",
            "OFL-1.1",
            ReservedFontName: null,

            // The letters of Arabic, Persian and Urdu, the vowel marks and the marks of the Quran that are reordered,
            // both sets of digits, punctuation, the joiners, and a few presentation forms. The subset keeps the
            // forms, the ligatures and the anchors of these letters.
            Unicodes: "20,21,28,29,2C,2E,30-39,3A,AB,BB,60C,61B,61C,61F,621-63A,640-65F,660-669,66A-66C,670,671,679,67E,686,688,691,698,6A9,6AF,6BA,6BE,6C1,6CC,6D2,6D4,6DC,6E3,6E7,6E8,6F0-6F9,8CA,200C-200F,25CC,FDF2,FE8D,FE8E,FEDF,FEE0,FEFB"),
        new(
            "fonts/noto-nastaliq-urdu",
            "fonts/noto-nastaliq-urdu/NotoNastaliqUrdu-Regular.subset.ttf",
            "NotoNastaliqUrdu-Regular.ttf",
            "6decda3b03f6366dbd75e3a22544d248ca686b582f897fae8a7dc027302812e3",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoNastaliqUrdu/unhinted/ttf/NotoNastaliqUrdu-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/nastaliq)",
            "OFL-1.1",
            ReservedFontName: null,

            // The letters of Urdu, in the hanging style: each word descends to the baseline, a letter attached to
            // the next one (cursive attachment), with the marks on letters that were moved, and glyphs the font
            // inserts to make room for the dots.
            Unicodes: "20,21,2E,30-39,60C,61B,61F,621-63A,640-652,654,655,670,679,67E,686,688,691,698,6A9,6AF,6BA,6BE,6C1,6C3,6CC,6D2,6D4,6F0-6F9,200C,200D"),
        new(
            "fonts/noto-sans-hebrew",
            "fonts/noto-sans-hebrew/NotoSansHebrew-Regular.ttf",
            "NotoSansHebrew-Regular.ttf",
            "04272f5600d0ec816d31d0df73b23aa8d3501ea359ebe820da31c11ffcf00853",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansHebrew/unhinted/ttf/NotoSansHebrew-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/hebrew)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-hebrew-without-positioning",
            "fonts/noto-sans-hebrew/NotoSansHebrew-Regular.without-gpos.ttf",
            "NotoSansHebrew-Regular.ttf",
            "04272f5600d0ec816d31d0df73b23aa8d3501ea359ebe820da31c11ffcf00853",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansHebrew/unhinted/ttf/NotoSansHebrew-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/hebrew)",
            "OFL-1.1",
            ReservedFontName: null,

            // The same font without its positioning table, as a font made before OpenType Layout: a shaper then draws
            // a letter and its point with the precomposed letter, which the font has.
            Unicodes: "*",
            DropTables: "GPOS"),
        new(
            "fonts/noto-sans-syriac",
            "fonts/noto-sans-syriac/NotoSansSyriac-Regular.ttf",
            "NotoSansSyriac-Regular.ttf",
            "1ec0391c549b5e63e4f8d810b4485a64c5551445bfb2bdbf309ce8368bef9d4c",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansSyriac/unhinted/ttf/NotoSansSyriac-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/syriac)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),

        // The fonts of the Indic scripts, stored as they are: each one has the features of the Indic shaping model
        // for its script, under the script tag of the second version of the specifications.
        new(
            "fonts/noto-sans-devanagari",
            "fonts/noto-sans-devanagari/NotoSansDevanagari-Regular.ttf",
            "NotoSansDevanagari-Regular.ttf",
            "216921eded5a97435fa0638deca66496bf51f52fa3467f566deb9938c25a71de",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansDevanagari/unhinted/ttf/NotoSansDevanagari-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/devanagari)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-bengali",
            "fonts/noto-sans-bengali/NotoSansBengali-Regular.ttf",
            "NotoSansBengali-Regular.ttf",
            "7ba979b4d1d9d3c143c826660c7eba610b34f976bdd2e4f178209432d1cd56e4",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansBengali/unhinted/ttf/NotoSansBengali-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/bengali)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-gurmukhi",
            "fonts/noto-sans-gurmukhi/NotoSansGurmukhi-Regular.ttf",
            "NotoSansGurmukhi-Regular.ttf",
            "2454c944fda799d2861f11c03e492c453e33083dd1e8d99bd6baac8de71ad0f0",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansGurmukhi/unhinted/ttf/NotoSansGurmukhi-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/gurmukhi)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-gujarati",
            "fonts/noto-sans-gujarati/NotoSansGujarati-Regular.ttf",
            "NotoSansGujarati-Regular.ttf",
            "18727143b8eadade0a59dca80959ef784bf8d55e07285d1b4096594dbf829f0a",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansGujarati/unhinted/ttf/NotoSansGujarati-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/gujarati)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-oriya",
            "fonts/noto-sans-oriya/NotoSansOriya-Regular.ttf",
            "NotoSansOriya-Regular.ttf",
            "38ac5eae2e1666558b88c63bf584349d6f924cca6b3b8867811546734afeb251",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansOriya/unhinted/ttf/NotoSansOriya-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/oriya)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-tamil",
            "fonts/noto-sans-tamil/NotoSansTamil-Regular.ttf",
            "NotoSansTamil-Regular.ttf",
            "6634d9cc97a726e670df41281dd32b167a6b9b71f2036e19671ff08fdde0c292",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansTamil/unhinted/ttf/NotoSansTamil-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/tamil)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-telugu",
            "fonts/noto-sans-telugu/NotoSansTelugu-Regular.ttf",
            "NotoSansTelugu-Regular.ttf",
            "dcb86a5da09365a2d73d725e429403dca988e6d50c1b1d564579a25dbc18bd29",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansTelugu/unhinted/ttf/NotoSansTelugu-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/telugu)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-kannada",
            "fonts/noto-sans-kannada/NotoSansKannada-Regular.ttf",
            "NotoSansKannada-Regular.ttf",
            "2870274619c2a4abe5d6b11fcb7c4a372eba765b6b7881d459b0eb0a609a68eb",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansKannada/unhinted/ttf/NotoSansKannada-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/kannada)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-malayalam",
            "fonts/noto-sans-malayalam/NotoSansMalayalam-Regular.ttf",
            "NotoSansMalayalam-Regular.ttf",
            "cc39bb1f7d63b582a2aec1abf2469a43b805a3523a27e0a0eee8cd32702541d5",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansMalayalam/unhinted/ttf/NotoSansMalayalam-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/malayalam)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-devanagari-first-version",
            "fonts/noto-sans-devanagari/NotoSansDevanagari-Regular.first-version.ttf",
            "NotoSansDevanagari-Regular.ttf",
            "216921eded5a97435fa0638deca66496bf51f52fa3467f566deb9938c25a71de",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansDevanagari/unhinted/ttf/NotoSansDevanagari-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/devanagari)",
            "OFL-1.1",
            ReservedFontName: null,

            // The same font with the script tag of the first version of the specifications only ("old behavior"): the
            // lookups of the font are then applied to a consonant followed by a halant.
            Unicodes: "*",
            LayoutScripts: "deva,DFLT"),
        new(
            "fonts/noto-sans-malayalam-first-version",
            "fonts/noto-sans-malayalam/NotoSansMalayalam-Regular.first-version.ttf",
            "NotoSansMalayalam-Regular.ttf",
            "cc39bb1f7d63b582a2aec1abf2469a43b805a3523a27e0a0eee8cd32702541d5",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansMalayalam/unhinted/ttf/NotoSansMalayalam-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/malayalam)",
            "OFL-1.1",
            ReservedFontName: null,

            // The same font with the script tag of the first version of the specifications only.
            Unicodes: "*",
            LayoutScripts: "mlym"),
        new(
            "fonts/noto-sans-khmer",
            "fonts/noto-sans-khmer/NotoSansKhmer-Regular.ttf",
            "NotoSansKhmer-Regular.ttf",
            "60c10dbfae33a44f1897cd939789bd173aedb7c7dc0a9af389a737e33cb7e548",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansKhmer/unhinted/ttf/NotoSansKhmer-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/khmer)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-myanmar",
            "fonts/noto-sans-myanmar/NotoSansMyanmar-Regular.ttf",
            "NotoSansMyanmar-Regular.ttf",
            "f4be6ef43871516d8347c5217a2033991b8ecda2dd1200007c2fb431a66d64db",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansMyanmar/unhinted/ttf/NotoSansMyanmar-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/myanmar)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-serif-tibetan",
            "fonts/noto-serif-tibetan/NotoSerifTibetan-Regular.ttf",
            "NotoSerifTibetan-Regular.ttf",
            "7292b2c76cf5c9b81e5362d88e0cb0a16b9d92827f86f945203014b049fd2fc5",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSerifTibetan/unhinted/ttf/NotoSerifTibetan-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/tibetan)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-sinhala",
            "fonts/noto-sans-sinhala/NotoSansSinhala-Regular.ttf",
            "NotoSansSinhala-Regular.ttf",
            "9517c7789971e8d17f55ef61992faf526eb21e7910d2dda82f50d82b95255d0e",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansSinhala/unhinted/ttf/NotoSansSinhala-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/sinhala)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-javanese",
            "fonts/noto-sans-javanese/NotoSansJavanese-Regular.ttf",
            "NotoSansJavanese-Regular.ttf",
            "016f43b042c0b01e29688f71607e5d6fdb0effef6fc7aa3aa8eae6b4427fdca8",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansJavanese/unhinted/ttf/NotoSansJavanese-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/javanese)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-balinese",
            "fonts/noto-sans-balinese/NotoSansBalinese-Regular.ttf",
            "NotoSansBalinese-Regular.ttf",
            "cee58a184e7e00c4332087ac01870a69ac52fd5fa17ea3783ac728c945af1827",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansBalinese/unhinted/ttf/NotoSansBalinese-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/balinese)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-tai-tham",
            "fonts/noto-sans-tai-tham/NotoSansTaiTham-Regular.ttf",
            "NotoSansTaiTham-Regular.ttf",
            "f07591d877c315027d1e8945143c6aed351f43561af1b102364ad9ea70d47d2f",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansTaiTham/unhinted/ttf/NotoSansTaiTham-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/tai-tham)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-nko",
            "fonts/noto-sans-nko/NotoSansNKo-Regular.ttf",
            "NotoSansNKo-Regular.ttf",
            "72107ca6885331147fb0ca4d7a71f96e6b3e989a75d66ef979a96966b28f8526",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansNKo/unhinted/ttf/NotoSansNKo-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/nko)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-mongolian",
            "fonts/noto-sans-mongolian/NotoSansMongolian-Regular.ttf",
            "NotoSansMongolian-Regular.ttf",
            "df8e76675870a47f7dd97ea7e224fcf870713f43ebe6fe4ae6c260d0ee838a0d",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansMongolian/unhinted/ttf/NotoSansMongolian-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/mongolian)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-adlam",
            "fonts/noto-sans-adlam/NotoSansAdlam-Regular.ttf",
            "NotoSansAdlam-Regular.ttf",
            "e723b210127ffd4aa2f467c3d417d0764168296c9457ca42e498c7258956f2ce",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansAdlam/unhinted/ttf/NotoSansAdlam-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/adlam)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-cham",
            "fonts/noto-sans-cham/NotoSansCham-Regular.ttf",
            "NotoSansCham-Regular.ttf",
            "d48a34e02b4096eb3f2e008b74459789540598c2274d65fc53012517d0b08d92",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansCham/unhinted/ttf/NotoSansCham-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/cham)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-newa",
            "fonts/noto-sans-newa/NotoSansNewa-Regular.ttf",
            "NotoSansNewa-Regular.ttf",
            "b772e089e2298380589f8cdea1f53da9e8ad0aa3e2edbaaaeb8704cd5072309c",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansNewa/unhinted/ttf/NotoSansNewa-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/newa)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-kaithi",
            "fonts/noto-sans-kaithi/NotoSansKaithi-Regular.ttf",
            "NotoSansKaithi-Regular.ttf",
            "ba33fd78d18df942260c53df4080254901aa7c3b69678c7571d680bf063a5e2d",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansKaithi/unhinted/ttf/NotoSansKaithi-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/kaithi)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-lepcha",
            "fonts/noto-sans-lepcha/NotoSansLepcha-Regular.ttf",
            "NotoSansLepcha-Regular.ttf",
            "e3f52772d537a0156efa2dbfb7057f154485990df555ef15bd37e5046907dee6",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansLepcha/unhinted/ttf/NotoSansLepcha-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/lepcha)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-duployan",
            "fonts/noto-sans-duployan/NotoSansDuployan-Regular.subset.ttf",
            "NotoSansDuployan-Regular.ttf",
            "969d84fe2f971de6bc7bf9b25698a63019967ca2739e4ec9ddb7e355aebbe0f5",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansDuployan/unhinted/ttf/NotoSansDuployan-Regular.ttf",
            "David Corbett and Google LLC (https://github.com/notofonts/duployan)",
            "OFL-1.1",
            ReservedFontName: null,

            // A few letters of each kind, the marks and the format controls: the font keeps the lookups that connect
            // them, a few thousand subtables, and the glyphs they need.
            Unicodes: "20,2E,41,25CC,200C,200D,1BC00-1BC0B,1BC21,1BC37,1BC3B,1BC40,1BC41,1BC44,1BC46,1BC5A,1BC68,1BC9D-1BCA3"),
        new(
            "fonts/noto-sans-thai",
            "fonts/noto-sans-thai/NotoSansThai-Regular.ttf",
            "NotoSansThai-Regular.ttf",
            "d4303fe9c63ebb72759ca8b6d2040c8ae81689f7d08d7b91c656154382b49313",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansThai/unhinted/ttf/NotoSansThai-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/thai)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-lao",
            "fonts/noto-sans-lao/NotoSansLao-Regular.ttf",
            "NotoSansLao-Regular.ttf",
            "d3976b0ac08702c54999ff42eae295d1597fd73b44e0c57d7f4917524e18afbe",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSansLao/unhinted/ttf/NotoSansLao-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/lao)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: null),
        new(
            "fonts/noto-sans-cjk-kr-regular",
            "fonts/noto-sans-cjk-kr/NotoSansCJKkr-Regular.subset.otf",
            "NotoSansCJKkr-Regular.otf",
            "6bcb2a0703aa137e874fc2dffa85f6c21ba9a67fa329e81b8c801663af7e992a",
            "https://github.com/notofonts/noto-cjk/blob/f8d157532fbfaeda587e826d4cd5b21a49186f7c/Sans/OTF/Korean/NotoSansCJKkr-Regular.otf",
            "Adobe (https://github.com/notofonts/noto-cjk)",
            "OFL-1.1",
            ReservedFontName: null,

            // The letters of modern Hangul and a few of Old Hangul, with the forms the font has for each position
            // of a syllable block, the fillers, the tone marks, and some syllables.
            Unicodes: "20,2E,41,301,302E,302F,3099,25CC,200C,200D,1100-1116,115F,1160-1178,11A7-11C5,11EB,11F0,11F9,A960,A964,D7B0,D7B5,D7CB,D7DD,3131,314F,AC00-AC1C,AC12,AE00,B098,B2E4,D55C",
            KeepHinting: true),
        new(
            "fonts/noto-sans-cjk-kr-letters",
            "fonts/noto-sans-cjk-kr/NotoSansCJKkr-Regular.letters.otf",
            "NotoSansCJKkr-Regular.otf",
            "6bcb2a0703aa137e874fc2dffa85f6c21ba9a67fa329e81b8c801663af7e992a",
            "https://github.com/notofonts/noto-cjk/blob/f8d157532fbfaeda587e826d4cd5b21a49186f7c/Sans/OTF/Korean/NotoSansCJKkr-Regular.otf",
            "Adobe (https://github.com/notofonts/noto-cjk)",
            "OFL-1.1",
            ReservedFontName: null,

            // The same letters without any syllable: every block is drawn with its letters.
            Unicodes: "20,2E,41,301,302E,302F,3099,25CC,200C,200D,1100-1116,115F,1160-1178,11A7-11C5,11EB,11F0,11F9,A960,A964,D7B0,D7B5,D7CB,D7DD,3131,314F",
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

        // Color fonts of the googlefonts/color-fonts project, stored as they are. The test glyphs are built by a
        // script of the project for each paint of the COLR table; the smiley is one emoji of the Twemoji set.
        ColorFont("colr-test-glyphs", "test_glyphs-glyf_colr_1.ttf", "8aa611b1ca97044ac6f13dc982fde29256612f0a5acc6ef47ca541a7a5b99b28", "Apache-2.0", "The googlefonts/color-fonts authors"),
        ColorFont("colr-test-glyphs-variable", "test_glyphs-glyf_colr_1_variable.ttf", "ad575a09d6748aebcb3b90ffd384c64d5c64b5fd9927967e7cd7cc0d70c98d34", "Apache-2.0", "The googlefonts/color-fonts authors"),
        ColorFont("colr-twemoji-smiley", "twemoji_smiley-glyf_colr_1.ttf", "b462e4de616a38979b053a49b0b5a5f2dd72b6d5f55be43c5b1eb812fd438dbc", "CC-BY-4.0", "Twitter, Inc and other contributors (Twemoji), built by the googlefonts/color-fonts project"),
        ColorFont("sbix-twemoji-smiley", "twemoji_smiley-sbix.ttf", "7767316e76003dff2415c0bec6d8972024d8f308349bb576abce7b26b5319c9f", "CC-BY-4.0", "Twitter, Inc and other contributors (Twemoji), built by the googlefonts/color-fonts project"),
        ColorFont("colr-twemoji-smiley-cff2", "twemoji_smiley-cff2_colr_1.otf", "217bfb5a2876029afa9f52affe9810f398ba48574518fc228ee8966640af8ae3", "CC-BY-4.0", "Twitter, Inc and other contributors (Twemoji), built by the googlefonts/color-fonts project"),

        // An emoji font, subset to the characters of each kind of emoji sequence: characters that are emoji or text by
        // default, the skin tone modifiers, people and what joins them, four regional indicators, the keycap, the tags
        // of a subdivision flag. The subset keeps the ligatures of the sequences these characters make.
        new(
            "fonts/noto-color-emoji",
            "fonts/noto-color-emoji/Noto-COLRv1.subset.ttf",
            "Noto-COLRv1.ttf",
            "b8e25ea68db82f9e4d0aee921f4420be2be39887bd5c893a2ad98710531f9d0c",
            "https://github.com/googlefonts/noto-emoji/blob/e20cbc2bbec1926686be9f9bee7d1d2cfa1fea0e/2D/fonts/Noto-COLRv1.ttf",
            "Google Inc. (https://github.com/googlefonts/noto-emoji)",
            "OFL-1.1",
            ReservedFontName: null,
            Unicodes: "20,23,2A,30-39,A9,200D,20E3,2122,263A,2764,261D,2640,FE0E,FE0F,1F1EB,1F1F7,1F1FA,1F1F8,1F308,1F3F3,1F3F4,1F3FB-1F3FF,1F44D,1F466-1F469,1F48B,1F600,1F642,1F680,E0062,E0065,E0067,E006E,E007F",
            HasOutlineVectors: false),
    ];

    // The lines of text the reference renderer draws with the color fonts: every character of the font unless texts
    // are given, a number of characters per line, at a size and with a box of the line that are whole pixels.
    private static readonly Dictionary<string, RenderSet[]> Renders = new(StringComparer.Ordinal)
    {
        ["fonts/synthetic-color-boxes"] = [new(Size: 64, Ascent: 64, Descent: 16, PerLine: 8), new(Size: 64, Ascent: 64, Descent: 16, PerLine: 8, Palette: 1, Foreground: "2060C0")],
        ["fonts/synthetic-color-transforms"] = [new(Size: 64, Ascent: 64, Descent: 16, PerLine: 8, Foreground: "0000FF")],
        ["fonts/synthetic-color-gradients"] = [new(Size: 64, Ascent: 64, Descent: 16, PerLine: 8, Foreground: "0000FF")],
        ["fonts/synthetic-color-clips"] = [new(Size: 64, Ascent: 64, Descent: 16, PerLine: 8)],
        // The bitmap fonts, at the sizes of their strikes: a pixel of an image is then a pixel of the render. The
        // reference renderer draws an image one pixel higher than an outline at the same place (the bottom of an image
        // that is on the baseline is one pixel above it): the renders say so, and the tests draw there. C, which is an
        // outline, is left out of these lines.
        ["fonts/synthetic-bitmap-boxes"] = [new(Size: 16, Ascent: 16, Descent: 4, PerLine: 8, ImageShift: -1, Exclude: ["0043"]), new(Size: 32, Ascent: 32, Descent: 8, PerLine: 8, ImageShift: -1, Exclude: ["0043"])],
        ["fonts/synthetic-color-bitmaps"] = [new(Size: 16, Ascent: 16, Descent: 4, PerLine: 8, ImageShift: -1, Exclude: ["0043"]), new(Size: 32, Ascent: 32, Descent: 8, PerLine: 8, ImageShift: -1, Exclude: ["0043"])],
        ["fonts/sbix-twemoji-smiley"] = [new(Size: 109, Ascent: 109, Descent: 32, PerLine: 8, ImageShift: -1)],
        ["fonts/colr-test-glyphs"] =
        [
            new(Size: 100, Ascent: 100, Descent: 20, PerLine: 8),

            // The glyphs that use the color of the text, with a color that is not black and with one that is translucent.
            new(Size: 100, Ascent: 100, Descent: 20, PerLine: 8, Texts: ["F0B00 F0B01 F0B02 F0B03 F0B04 F0B05 F0B06 F0B07"], Foreground: "D02090"),
            new(Size: 100, Ascent: 100, Descent: 20, PerLine: 8, Texts: ["F0B00 F0B01 F0B02 F0B03 F0B04 F0B05 F0B06 F0B07"], Foreground: "2060C080"),

            // The other palettes of the font, and a small size.
            new(Size: 100, Ascent: 100, Descent: 20, PerLine: 8, Texts: ["F0100 F0200 F0500 F0503 F0A03 F0E00 F0E01 F1000"], Palette: 1),
            new(Size: 100, Ascent: 100, Descent: 20, PerLine: 8, Texts: ["F0100 F0200 F0500 F0503 F0A03 F0E00 F0E01 F1000"], Palette: 2),
            new(Size: 20, Ascent: 20, Descent: 4, PerLine: 32),
        ],
        ["fonts/colr-test-glyphs-variable"] =
        [
            new(Size: 100, Ascent: 100, Descent: 20, PerLine: 8),
            new(Size: 100, Ascent: 100, Descent: 20, PerLine: 8, Variations: "SWPS=45,SWPE=-30,SWC1=0.25,SWC2=-0.25,SWC3=0.5,SWC4=-0.5"),
            new(Size: 100, Ascent: 100, Descent: 20, PerLine: 8, Variations: "SCOX=100,SCOY=-150,SCSX=0.5,SCSY=-0.75"),
            new(Size: 100, Ascent: 100, Descent: 20, PerLine: 8, Variations: "GRX0=100,GRY0=-200,GRX1=-150,GRY1=250,GRX2=300,GRY2=100,GRR0=50,GRR1=-100", Exclude: ["F0503", "F0504", "F0505", "F0C00", "F0C01", "F0C02", "F0C03", "F0C04", "F0C05", "F0C06", "F0C07", "F0C08", "F0C09", "F0C0A"]),
        ],
        ["fonts/colr-twemoji-smiley"] = [new(Size: 96, Ascent: 96, Descent: 32, PerLine: 8), new(Size: 40, Ascent: 40, Descent: 12, PerLine: 16)],

        // Emoji and emoji sequences, each drawn with one glyph: characters alone and with their selector, a skin tone,
        // a family and a kiss joined by zero width joiners, two flags of regional indicators, keycaps, a flag joined to
        // a rainbow and a flag with tags.
        ["fonts/noto-color-emoji"] =
        [
            new(
                Size: 64,
                Ascent: 64,
                Descent: 16,
                PerLine: 8,
                Texts:
                [
                    "1F600 1F642 1F680 2764 FE0F 263A FE0F 1F308",
                    "1F44D 1F44D 1F3FD 1F468 200D 1F469 200D 1F467 200D 1F466 1F469 200D 2764 FE0F 200D 1F48B 200D 1F468",
                    "1F1EB 1F1F7 1F1FA 1F1F8 0031 FE0F 20E3 0023 FE0F 20E3 1F3F3 FE0F 200D 1F308 1F3F4 E0067 E0062 E0065 E006E E0067 E007F",
                ]),
        ],
        ["fonts/colr-twemoji-smiley-cff2"] = [new(Size: 96, Ascent: 96, Descent: 32, PerLine: 8)],
    };

    // The characters of the color fonts the reference renderer does not draw as the specification says, which are
    // left out of the lines it draws:
    // - a gradient that pads and whose stops are all at the same offset, or a sweep gradient that pads and whose angles
    //   are equal, has two colors ("Color lines" and "Sweep gradients" of the COLR table); the renderer draws nothing;
    // - a glyph that paints another glyph several times, which is not a cycle; the renderer draws nothing;
    // - a radial gradient is painted where the radius of its circles is positive ("Radial gradients": when the circles
    //   make a cone, on one side of its tip); the renderer also paints the other side of the tip. The tip is in the
    //   glyphs of the radial gradients between two circles side by side (F0506 to F0508) and, when the axes of the
    //   variable font move the circles of its radial gradients, in the glyphs that are left out of that instance.
    private static readonly Dictionary<string, HashSet<string>> CharactersWithoutReference = new(StringComparer.Ordinal)
    {
        ["fonts/colr-test-glyphs"] = ["F1300", "F1303", "F1306", "F1309", "F130C", "F130F", "F1312", "F1315", "F1200", "F0506", "F0507", "F0508"],
        ["fonts/colr-test-glyphs-variable"] = ["F1300", "F1303", "F1306", "F1309", "F130C", "F130F", "F1312", "F1315", "F1200", "F0506", "F0507", "F0508"],

        // The synthetic gradients: J is a linear gradient whose stops are at one offset, M a sweep whose angles are equal.
        ["fonts/synthetic-color-gradients"] = ["004A", "004D"],
    };

    private const string ColorFontsCommit = "0046ea4c3b69e9fbbe464c2594816894e3aa5e4b";

    private static ExternalFont ColorFont(string id, string fileName, string sha256, string license, string author)
    {
        return new ExternalFont(
            "fonts/" + id,
            "fonts/color-fonts/" + fileName,
            "color-fonts/" + fileName,
            sha256,
            $"https://github.com/googlefonts/color-fonts/blob/{ColorFontsCommit}/fonts/{fileName}",
            author,
            license,
            ReservedFontName: null,
            Unicodes: null,
            HasOutlineVectors: false);
    }

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

    // The hash of the output of truetype_hinting.py, which pins the fonts, hb-subset and FreeType.
    private const string HintingSuiteSha256 = "8125166b30a0f8e6ecd603f38a67afad6b3d0e254c2e4d8038d11effa20e6abf";

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

        // A vertical line with a font that has no vertical metrics: the height of a line is the advance of every
        // glyph, and the marks are placed on their letters
        new("Hello, World!", null, Direction: "ttb"),
        new("e\u0301 a\u0308\u0304 o\u0327\u0301 A\u030A \u00E9 \u00C5", null, Direction: "ttb"),
    ];

    private static readonly ShapingText[] CjkShapingCases =
    [
        ("\u65E5\u672C\u8A9E\u306E\u30C6\u30AD\u30B9\u30C8\u3002", null),
        ("\u304B\u3099\u304D\u309A \u30AB\u3099\u30CF\u309A \u304C\u3071", null),
        ("ABC abc 123 To AV", null),
        ("\u300C\u65E5\u672C\u300D\u3001\u6771\u4EAC\u3002\uFF21\uFF22\uFF11", null),
        ("\u4E00\uFE00\u4E01\U000E0100", null),
        ("ABC abc 123 To AV", "-kern,palt"),

        // A vertical line of upright glyphs: the vertical forms of the punctuation and of the small letters, the
        // advance heights and the vertical origins of the font, and the features that change them on request.
        new("\u65E5\u672C\u8A9E\u306E\u30C6\u30AD\u30B9\u30C8\u3002", null, Direction: "ttb"),
        new("\u300C\u65E5\u672C\u300D\u3001\u6771\u4EAC\u3002\uFF21\uFF22\uFF11", null, Direction: "ttb"),
        new("\u30FC\u3041\u3083\u3063\uFF08\uFF09\u2026\u2014 \u3000", null, Direction: "ttb"),
        new("\u304B\u3099\u304D\u309A \u30AB\u3099\u30CF\u309A \u304C\u3071", null, Direction: "ttb"),
        new("ABC abc 123 To AV", null, Direction: "ttb"),
        new("\u300C\u65E5\u672C\u300D\u3001\u6771\u4EAC\u3002\u30FC", "-vert", Direction: "ttb"),
        new("\u300C\u65E5\u672C\u300D\u3001\u6771\u4EAC\u3002\u30C6\u30AD\u30B9\u30C8", "vpal,vkrn", Direction: "ttb"),
        new("\u3042\u3044\u3046\u30A2\u30A4\u30A6\u3001\u3002", "vhal,valt", Direction: "ttb"),
    ];

    private static readonly ShapingText[] BoxesShapingCases =
    [
        ("ABA BA AAB", null),
        ("ABA BA AAB", "-kern"),
        ("A\u200BB A\u00ADB", null),
        ("A\u00A0B\u2003C\u2009D", null),
        ("\u00C1 H-\u2010\u2011", null),
        ("AZB", null),

        // A vertical line with a font that has no vertical metrics
        new("ABA BA \u00C1 H", null, Direction: "ttb"),
        new("A\u00A0B\u2003C\u2009D", null, Direction: "ttb"),
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

        // A vertical line: the advance heights and the vertical origins vary too
        new("\u65E5\u672C\u8A9E\u306E\u30C6\u30AD\u30B9\u30C8\u3002 ABC", null, Direction: "ttb"),
        new("\u65E5\u672C\u8A9E\u306E\u30C6\u30AD\u30B9\u30C8\u3002 ABC", null, "wght=900", Direction: "ttb"),
        new("\u65E5\u672C\u8A9E\u306E\u30C6\u30AD\u30B9\u30C8\u3002 ABC", null, "wght=555", Direction: "ttb"),
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

    // Arabic: the forms of the letters and what joins them (the tatweel, the zero width joiner) or not (the zero width
    // non-joiner, a letter that joins on one side), the lam-alef ligatures and what keeps them from forming, the order
    // of the marks of a letter, both sets of digits, punctuation that is mirrored, presentation forms, and the
    // features of the script turned off or on.
    private static readonly ShapingText[] ArabicShapingCases =
    [
        ("\u0627\u0644\u0633\u0644\u0627\u0645 \u0639\u0644\u064A\u0643\u0645", null),
        ("\u0628\u0633\u0645 \u0627\u0644\u0644\u0647 \u0627\u0644\u0631\u062D\u0645\u0646 \u0627\u0644\u0631\u062D\u064A\u0645", null),
        ("\u0628\u0650\u0633\u0652\u0645\u0650 \u0671\u0644\u0644\u0651\u064E\u0647\u0650 \u0671\u0644\u0631\u0651\u064E\u062D\u0652\u0645\u064E\u0670\u0646\u0650", null),
        ("\u0645\u062D\u0645\u062F \u0641\u0627\u0631\u0633\u06CC \u06AF\u0686\u067E\u0698 \u0627\u0631\u062F\u0648 \u06C1\u06D2 \u0679\u0688\u0691 \u06BE\u06BA", null),
        ("\u0628 \u0628\u0628 \u0628\u0628\u0628 \u0627\u0628 \u0628\u0627 \u0627\u0628\u0627 \u062F\u0628\u062F \u0621\u0628\u0621", null),
        ("\u0628\u0651\u064E \u0628\u064E\u0651 \u0628\u064B\u0651 \u0628\u0650\u0651 \u0628\u0652\u0670", null),
        ("\u0628\u064E\u0654 \u0628\u0650\u0655 \u0628\u0651\u0650\u0655\u064E\u0654 \u0628\u064E\u06E7\u06DC \u0628\u0650\u06E3\u08CA", null),
        ("\u0644\u0627 \u0644\u0623 \u0644\u0625 \u0644\u0622 \u0628\u0644\u0627 \u0644\u0671 \u0644\u064E\u0627 \u0644\u0627\u064E \u0644\u0651\u064E\u0627\u064B", null),
        ("\u0644\u200D\u0627 \u0644\u200C\u0627 \u0644\u200D\u200D\u0627 \u0644\u0640\u0627", null),
        ("\u0628\u200D \u200D\u0628 \u0628\u200C\u0628 \u0628\u200D\u200C\u200D\u0628 \u200D\u0628\u200D", null),
        ("\u0628\u0640\u0628 \u0640\u0628 \u0628\u0640 \u0640\u0640 \u0627\u0640\u0627", null),
        ("\u0628\u200F\u0628 \u0628\u200E\u0628 \u0628\u061C\u0628 \u0628\u034F\u0628 \u0628\u064E\u200D\u0628", null),
        ("\u0627\u0653 \u0622 \u0627\u0654 \u0623 \u0648\u0654 \u064A\u0654 \u0627\u0655 \u0644\u0627\u0653", null),
        ("\u0661\u0662\u0663 \u06F4\u06F5\u06F6 123 \u060C\u061B\u061F (\u0628) \u00AB\u0628\u00BB 1,5 \u066A\u066B\u066C", null),
        ("\uFEFB \uFE8D\uFE8E \uFEDF\uFEE0\uFE8E \uFDF2", null),
        ("\u0627\u0644\u0644\u0647 \u0644\u0644\u0647 \u0641\u0644\u0644\u0647 \u0628\u0627\u0644\u0644\u0647", null),
        ("\u25CC\u064E \u064E\u0628  \u0651\u064E", null),
        ("\u0628\u0633\u0645 \u0627\u0644\u0644\u0647 \u0644\u0627 \u0641\u064A", "-liga"),
        ("\u0628\u0633\u0645 \u0627\u0644\u0644\u0647 \u0644\u0627 \u0641\u064A", "-rlig"),
        ("\u0628\u0633\u0645 \u0627\u0644\u0644\u0647 \u0644\u0627 \u0641\u064A \u0644\u0645\u062D", "dlig"),
        ("\u0628\u0628\u0628 \u0644\u0627", "-init"),
        ("\u0628\u0628\u0628 \u0644\u0627", "-medi,-fina"),
        ("\u0628\u0628\u0628 \u0644\u0627", "init"),
        ("\u0628\u0628\u0628 \u0644\u0627 \u0628\u064E", "-ccmp"),
        ("\u0661\u0662\u0663 123", "pnum"),
        ("\u0628\u0650\u0651\u064E\u0652\u064B\u064C\u064D\u064E\u064F\u0650", null),
        ("\u0633\u0644\u0627\u0645 abc 123", null),
        ("\u0688\u200D\u064C \u200D\u064D\u06E7 \u060C\u0653\u200D\u0653\u06BE", null),
        ("\u0622\u0655 \u0623\u064E \u0624\u0651\u064F \u0626\u0650 \u0644\u0622\u064E", null),
        ("\u06AF\u0654\u064F \u062E\u0654\u0658", null),
        ("\u0628\u0654\u064F \u0628\u064E\u034F\u0651 \u0628\u0651\u034F\u064E", null),

        // Upright in a vertical line, the letters do not join
        new("\u0633\u0644\u0627\u0645 \u0628\u064E", null, Direction: "ttb"),
    ];

    // Urdu in the hanging style: words whose letters are attached one to the next and go down to the baseline, marks
    // on these letters, the dots of the letters and the glyphs the font inserts for them, digits and punctuation.
    private static readonly ShapingText[] UrduShapingCases =
    [
        ("\u0627\u0631\u062F\u0648 \u0632\u0628\u0627\u0646 \u067E\u0627\u06A9\u0633\u062A\u0627\u0646", null),
        ("\u0646\u0633\u062A\u0639\u0644\u06CC\u0642 \u06C1\u06D2 \u06A9\u06CC\u0627 \u062D\u0627\u0644 \u06C1\u06D2\u061F", null),
        ("\u0628\u0650\u0633\u0652\u0645\u0650 \u0627\u0644\u0644\u0651\u0670\u06C1\u0650 \u0645\u064F\u062D\u064E\u0645\u0651\u064E\u062F", null),
        ("\u0628\u0628\u0628\u0628\u0628 \u067E\u06BE\u0644 \u0679\u06BE\u0646\u0688 \u062C\u06BE\u06CC\u0644 \u0645\u0686\u06BE\u0644\u06CC", null),
        ("\u06F1\u06F2\u06F3 123 \u060C\u061B\u06D4 \u0628\u200C\u0628 \u0628\u200D \u200D\u0628 \u0628\u0640\u0628", null),
        ("\u062B\u06C1\u0654 \u0624\u0638\u062D \u0626\u0653 \u0628\u0650\u0625\u0631\u062B \u06BA\u06AF\u0645\u0634\u0641\u0653", null),
        ("\u0646\u0633\u062A\u0639\u0644\u06CC\u0642 \u06C1\u06D2", "-curs"),
        ("\u0646\u0633\u062A\u0639\u0644\u06CC\u0642 \u06C1\u06D2", "-mark,-mkmk"),
    ];

    // Thai: the sara am, whose ring goes before the marks above the consonant, the marks above tall consonants and
    // below consonants that have a tail, the order of the marks, vowels written before their consonant (which are not
    // reordered: they are typed first), and marks without a consonant.
    private static readonly ShapingText[] ThaiShapingCases =
    [
        ("\u0E20\u0E32\u0E29\u0E32\u0E44\u0E17\u0E22 \u0E2A\u0E27\u0E31\u0E2A\u0E14\u0E35 \u0E04\u0E23\u0E31\u0E1A", null),
        ("\u0E19\u0E49\u0E33 \u0E01\u0E33 \u0E01\u0E48\u0E33 \u0E01\u0E38\u0E48\u0E33 \u0E01\u0E34\u0E48\u0E33 \u0E33 \u0E01\u0E33\u0E33", null),
        ("\u0E01\u0E4D\u0E32 \u0E01\u0E48\u0E4D\u0E32 \u0E01\u0E48\u200D\u0E33 \u0E01\u0E48\u0E38\u0E33  \u0E33 \u25CC\u0E33", null),
        ("\u0E17\u0E35\u0E48\u0E19\u0E35\u0E48 \u0E1B\u0E35\u0E48 \u0E1B\u0E49\u0E32 \u0E1F\u0E49\u0E32 \u0E1D\u0E31\u0E48\u0E07 \u0E1B\u0E34\u0E48\u0E19", null),
        ("\u0E0D\u0E38 \u0E10\u0E39 \u0E0E\u0E38 \u0E0F\u0E39 \u0E24\u0E45 \u0E2C\u0E4C", null),
        ("\u0E01\u0E38\u0E48 \u0E01\u0E48\u0E38 \u0E01\u0E3A\u0E38 \u0E01\u0E38\u0E3A \u0E01\u0E31\u0E49 \u0E01\u0E49\u0E31 \u0E01\u0E47\u0E48\u0E4C", null),
        ("\u0E40\u0E01 \u0E42\u0E01 \u0E44\u0E01 \u0E40\u0E01\u0E35\u0E22 \u0E40\u0E01\u0E37\u0E2D \u0E41\u0E01\u0E30", null),
        ("\u0E48 \u0E01\u0E48\u0E49 \u25CC\u0E48 \u0E38 \u0E51\u0E52\u0E53 \u0E3F\u0E51\u0E50\u0E50 \u0E01\u0E2F \u0E46 \u0E4F\u0E5A\u0E5B", null),
        ("\u0E01\u200B\u0E02 \u0E01\u200C\u0E48 \u0E01\u034F\u0E48 A\u0E01.", null),
        ("\u0E19\u0E49\u0E33 \u0E1B\u0E35\u0E48 \u0E0D\u0E38", "-ccmp"),
        ("\u0E19\u0E49\u0E33 \u0E1B\u0E35\u0E48 \u0E0D\u0E38", "-mark,-mkmk"),
    ];

    // Lao: the same vowel and the same marks, with the semivowel written below the consonant.
    private static readonly ShapingText[] LaoShapingCases =
    [
        ("\u0E9E\u0EB2\u0EAA\u0EB2\u0EA5\u0EB2\u0EA7 \u0EAA\u0EB0\u0E9A\u0EB2\u0E8D\u0E94\u0EB5", null),
        ("\u0E99\u0EC9\u0EB3 \u0E81\u0EB3 \u0E81\u0EC8\u0EB3 \u0E81\u0EB8\u0EC8\u0EB3 \u0E81\u0EB4\u0EC8\u0EB3 \u0EB3 \u0E81\u0EB3\u0EB3", null),
        ("\u0E81\u0ECD\u0EB2 \u0E81\u0EC8\u0ECD\u0EB2 \u0E81\u0EC8\u200D\u0EB3 \u0E81\u0ECC\u0EB3 \u0E81\u0EBB\u0EB3 \u0E81\u0ECE\u0EB3", null),
        ("\u0EAB\u0EBC \u0EAB\u0EBC\u0EB2\u0E8D \u0E81\u0EB1\u0EC8 \u0E81\u0EBB\u0EC9 \u0E9B\u0EB5\u0EC9 \u0E9F\u0EC9\u0EB2", null),
        ("\u0E81\u0EB8\u0EC8 \u0E81\u0EC8\u0EB8 \u0E81\u0EBA\u0EB9 \u0E81\u0EB9\u0EBA \u0ECB\u0EBA\u0EB9 \u0E81\u0EBC\u0EB8", null),
        ("\u0EC0\u0E81 \u0EC2\u0E81 \u0EC4\u0E81 \u0EC0\u0E81\u0EB5\u0E8D \u0EDC \u0EDD \u0EC6 \u0ED1\u0ED2\u0ED3", null),
        ("\u0EC8 \u25CC\u0EC8 \u0EB8 \u0E81\u200B\u0E82 \u0E81\u200C\u0EC8 A\u0E81.", null),
        ("\u0E99\u0EC9\u0EB3 \u0E9B\u0EB5\u0EC9 \u0EAB\u0EBC", "-ccmp"),
        ("\u0E99\u0EC9\u0EB3 \u0E9B\u0EB5\u0EC9 \u0EAB\u0EBC", "-mark,-mkmk"),
    ];

    // Hangul: syllables, letters that make a syllable the font has (composed) or not (shaped with the features of
    // their positions), a syllable followed by a trailing consonant, the letters of Old Hangul and the fillers, letters
    // that do not make a block, the tone marks and their dotted circle, and what keeps letters apart.
    private static readonly ShapingText[] HangulShapingCases =
    [
        ("\uD55C\uAE00 \uAC00\uAC01\uAC04 \uB098\uB2E4", null),
        ("\u1112\u1161\u11AB\u1100\u1173\u11AF \u1100\u1161 \u1100\u1161\u11A8 \uAC00\u11A8 \uAC00\u11AB", null),
        ("\u1100\u1161\u11A8\u11A8 \uAC01\u11A8 \uAC01\u1161 \uAC00\u1161 \u1100\u1100\u1161 \u1100\u1161\u1161", null),
        ("\u1100 \u1161 \u11A8 \u1161\u11A8 \u1100\u11A8 \u1100\u11A7 \u1100\u1161\u11A7", null),
        ("\u1113\u1161 \u1113\u1161\u11A8 \u1100\u1176 \u1100\u1161\u11C3 \uAC00\u11C3 \uAC00\u11EB \uAC00\u11F9", null),
        ("\u115F\u1161 \u1100\u1160 \u115F\u1160 \u1100\u1160\u11A8 \u115F\u1161\u11A8", null),
        ("\uA960\uD7B0 \uA964\u1161\uD7CB \u1100\uD7B5 \u1100\u1161\uD7DD \uAC00\uD7CB", null),
        ("\uAC00\u302E \uD55C\u302F \u1100\u1161\u302E \u1113\u1161\u11A8\u302F \uAC00\u302E\u302F", null),
        ("\u302E \u1100\u302E A\u302F \uAC00\u0301\u302E \u302F\u3099 \u11A8\u302E\u3099", null),
        ("\u1100\u200D\u1161 \uAC00\u200D\u11A8 \uAC00\u200C\u11A8 \u3131\u314F \uAC00\u3099 \uAC00\u0301", null),
        ("A\uAC00. \uAC00 \uB098. \u1103\u11A5 \u1110", null),
        ("\u1113\u1161\u11A8 \u1100\u1160\u11A8 \uAC00\u11C3", "-ljmo"),
        ("\u1113\u1161\u11A8 \u1100\u1160\u11A8 \uAC00\u11C3", "-vjmo,-tjmo"),
        ("\u1113\u1161\u11A8 \u1103\u11A5 \u1110", "-calt"),
        ("\u1100\u1161 \uAC00\u11A8 \uD55C\uAE00", "-ccmp,-liga"),

        // A vertical line: syllables and letters, upright
        new("\uD55C\uAE00 \uAC00\uAC01\uAC04 \uB098\uB2E4", null, Direction: "ttb"),
        new("\u1112\u1161\u11AB\u1100\u1173\u11AF \u1100\u1161 \uAC00\u11A8 \u1100 \u1161 \u11A8", null, Direction: "ttb"),
    ];

    // The Indic scripts. Each one has the same cases, written with the letters the scripts have in common at the same
    // place of their blocks (ka, ra, the halant, the vowel signs): single consonants with each vowel sign and modifier;
    // conjuncts; the reph and what keeps it from forming; the joiners after and before a halant; the vowel sign that is
    // drawn before the syllable, with half forms and conjuncts; halants and nuktas at the end of a syllable; vowel
    // letters; what is not a syllable (a sign without a letter, two modifiers too many, the generic bases); Vedic signs,
    // digits and punctuation; long syllables; then the same syllables without each of the features of the model.
    // The cases of a script come after: words, the letters and signs it has alone, the sequences the Unicode Standard
    // asks not to use for a vowel letter, and what comparing random texts with the reference shaper found.
    private static readonly ShapingText[] DevanagariShapingCases =
    [
        ("\u0915 \u0915\u093E \u0915\u093F \u0915\u0940 \u0915\u0941 \u0915\u0942 \u0915\u0947 \u0915\u0948 \u0915\u094B \u0915\u094C \u0915\u0902 \u0915\u093F\u0902 \u0915\u093E\u0903", null),
        ("\u0915\u094D\u0937 \u091C\u094D\u091E \u0924\u094D\u0930 \u0915\u094D\u0915 \u0938\u094D\u0924\u094D\u0930 \u0926\u094D\u092F \u0915\u094D\u0930\u093F \u0926\u094D\u0935 \u0936\u094D\u0930\u0940 \u0939\u094D\u092E", null),
        ("\u0930\u094D\u0915 \u0930\u094D\u0915\u093F \u0930\u094D\u0915\u094D\u0937\u093E \u0930\u094D \u0930\u094D\u0930 \u0930\u0930\u094D\u0915 \u0930\u094D\u0915\u0902 \u0930\u094D\u0915\u094B\u0902", null),
        ("\u0930\u094D\u200D\u0915 \u0930\u094D\u200C\u0915 \u0930\u200D\u094D\u0915 \u0930\u094D\u092F \u0930\u094D\u200D\u092F \u0930\u094D\u0939", null),
        ("\u0915\u094D\u200D\u0937 \u0915\u094D\u200C\u0937 \u0915\u200D\u094D\u0937 \u0915\u094D\u200D \u0915\u094D\u200C \u0915\u094D\u200D\u0924\u093F \u0915\u094D\u200C\u0924\u093F", null),
        ("\u0915\u094D\u0924\u093F \u0938\u094D\u0924\u094D\u0930\u093F \u0915\u093F\u0915\u093F \u093F \u0915\u093C\u093F \u0915\u093F\u093C \u0924\u094D\u092F\u093F \u0928\u094D\u0926\u094D\u0930\u093F", null),
        ("\u0915\u094D \u0915\u093C \u0915\u093C\u094D\u0937 \u0915\u094D\u093C \u0915\u093C\u093C \u0915\u093C\u093C\u093C \u0915\u094D\u094D \u0915\u094D\u094D\u0915", null),
        ("\u0905 \u0906 \u0905\u093E \u0907\u0902 \u0905\u0902\u0903 \u0905\u094D\u0915 \u0905\u200D \u0930\u094D\u0905 \u0930\u094D\u0907", null),
        ("\u093E \u094D \u0902 \u093C\u0915 \u0915\u093E\u093E \u0915\u0902\u093E \u0915\u0902\u0902\u0902 \u25CC\u093F \u00A0\u093F \u00A0\u094D\u0930 \u25CC\u094D\u0915 \u0915\u0903\u0903", null),
        ("\u0915\u0951 \u0915\u0941\u0952 \u0915\u0902\u0951\u0952 \u0915\u0902\u0903 \u0966\u0967\u0968 \u0915\u0964 \u0915\u0965 \u0915 \u0915\u0901\u1CDA \uA8F2\u0902", null),
        ("\u0915\u094D\u0915\u094D\u0915\u094D\u0915\u094D\u0915\u094D\u0915\u094D\u0915 \u0915\u0941\u0941\u0941\u0941\u0941\u0941 \u0915\u200C\u093F \u0915\u200D\u093F \u0915\u200C A\u0915. \u0915-\u0915", null),
        ("\u0915\u094D\u0937\u093F \u0930\u094D\u0915\u093F \u0938\u094D\u0924\u094D\u0930\u0940 \u0915\u094D\u200D\u0924 \u0915\u093C\u094D\u092F", "-rphf"),
        ("\u0915\u094D\u0937\u093F \u0930\u094D\u0915\u093F \u0938\u094D\u0924\u094D\u0930\u0940 \u0915\u094D\u200D\u0924 \u0915\u093C\u094D\u092F", "-half"),
        ("\u0915\u094D\u0937\u093F \u0930\u094D\u0915\u093F \u0938\u094D\u0924\u094D\u0930\u0940 \u0915\u094D\u200D\u0924 \u0915\u093C\u094D\u092F", "-blwf"),
        ("\u0915\u094D\u0937\u093F \u0930\u094D\u0915\u093F \u0938\u094D\u0924\u094D\u0930\u0940 \u0915\u094D\u200D\u0924 \u0915\u093C\u094D\u092F", "-akhn,-cjct"),
        ("\u0915\u094D\u0937\u093F \u0930\u094D\u0915\u093F \u0938\u094D\u0924\u094D\u0930\u0940 \u0915\u094D\u200D\u0924 \u0915\u093C\u094D\u092F", "-nukt"),
        ("\u0915\u094D\u0937\u093F \u0930\u094D\u0915\u093F \u0938\u094D\u0924\u094D\u0930\u0940 \u0915\u094D\u200D\u0924 \u0915\u093C\u094D\u092F", "-pres,-abvs,-blws,-psts"),
        ("\u0915\u094D\u0937\u093F \u0930\u094D\u0915\u093F \u0938\u094D\u0924\u094D\u0930\u0940 \u0915\u094D\u200D\u0924 \u0915\u093C\u094D\u092F", "-abvm,-blwm"),
        ("\u0915\u094D\u0937\u093F \u0930\u094D\u0915\u093F \u0938\u094D\u0924\u094D\u0930\u0940 \u0915\u094D\u200D\u0924 \u0915\u093C\u094D\u092F", "-rkrf"),
        ("\u0939\u093F\u0928\u094D\u0926\u0940 \u0915\u094D\u0937\u0924\u094D\u0930\u093F\u092F \u0936\u094D\u0930\u0940 \u0915\u0930\u094D\u092E \u0926\u094D\u0935\u093F\u0924\u0940\u092F \u092A\u094D\u0930\u093E\u0930\u094D\u0925\u0928\u093E \u091C\u093C\u093F\u0902\u0926\u0917\u0940", null),
        ("\u0931\u094D\u092F \u0930\u094D\u200D\u092F \u0926\u094D\u0927 \u0926\u094D\u200D\u0927 \u0926\u094D\u0926\u094D\u092F \u091F\u094D\u091F \u0915\u094D\u0937\u094D\u092E\u093F \u0905\u0945 \u0906\u0945 \u0909\u0941 \u090F\u0947 \u0930\u094D\u0907", null),
        ("\u0915\u093E\u0901 \u0915\u0953 \u0915\u0954 \u1CE9\u0902 \u0915\u1CE2 \u0915\u1CED \u0958 \u0915\u093C\u094D\u0937 \u0929 \u0931 \u0934\u093F \u2022\u093F \u25FB\u0902", null),
    ];

    private static readonly ShapingText[] BengaliShapingCases =
    [
        ("\u0995 \u0995\u09BE \u0995\u09BF \u0995\u09C0 \u0995\u09C1 \u0995\u09C2 \u0995\u09C7 \u0995\u09C8 \u0995\u09CB \u0995\u09CC \u0995\u0982 \u0995\u09BF\u0982 \u0995\u09BE\u0983", null),
        ("\u0995\u09CD\u09B7 \u099C\u09CD\u099E \u09A4\u09CD\u09B0 \u0995\u09CD\u0995 \u09B8\u09CD\u09A4\u09CD\u09B0 \u09A6\u09CD\u09AF \u0995\u09CD\u09B0\u09BF \u09A6\u09CD\u09B5 \u09B6\u09CD\u09B0\u09C0 \u09B9\u09CD\u09AE", null),
        ("\u09B0\u09CD\u0995 \u09B0\u09CD\u0995\u09BF \u09B0\u09CD\u0995\u09CD\u09B7\u09BE \u09B0\u09CD \u09B0\u09CD\u09B0 \u09B0\u09B0\u09CD\u0995 \u09B0\u09CD\u0995\u0982 \u09B0\u09CD\u0995\u09CB\u0982", null),
        ("\u09B0\u09CD\u200D\u0995 \u09B0\u09CD\u200C\u0995 \u09B0\u200D\u09CD\u0995 \u09B0\u09CD\u09AF \u09B0\u09CD\u200D\u09AF \u09B0\u09CD\u09B9", null),
        ("\u0995\u09CD\u200D\u09B7 \u0995\u09CD\u200C\u09B7 \u0995\u200D\u09CD\u09B7 \u0995\u09CD\u200D \u0995\u09CD\u200C \u0995\u09CD\u200D\u09A4\u09BF \u0995\u09CD\u200C\u09A4\u09BF", null),
        ("\u0995\u09CD\u09A4\u09BF \u09B8\u09CD\u09A4\u09CD\u09B0\u09BF \u0995\u09BF\u0995\u09BF \u09BF \u0995\u09BC\u09BF \u0995\u09BF\u09BC \u09A4\u09CD\u09AF\u09BF \u09A8\u09CD\u09A6\u09CD\u09B0\u09BF", null),
        ("\u0995\u09CD \u0995\u09BC \u0995\u09BC\u09CD\u09B7 \u0995\u09CD\u09BC \u0995\u09BC\u09BC \u0995\u09BC\u09BC\u09BC \u0995\u09CD\u09CD \u0995\u09CD\u09CD\u0995", null),
        ("\u0985 \u0986 \u0985\u09BE \u0987\u0982 \u0985\u0982\u0983 \u0985\u09CD\u0995 \u0985\u200D \u09B0\u09CD\u0985 \u09B0\u09CD\u0987", null),
        ("\u09BE \u09CD \u0982 \u09BC\u0995 \u0995\u09BE\u09BE \u0995\u0982\u09BE \u0995\u0982\u0982\u0982 \u25CC\u09BF \u00A0\u09BF \u00A0\u09CD\u09B0 \u25CC\u09CD\u0995 \u0995\u0983\u0983", null),
        ("\u0995\u0951 \u0995\u09C1\u0952 \u0995\u0982\u0951\u0952 \u0995\u0982\u0983 \u09E6\u09E7\u09E8 \u0995\u0964 \u0995\u0965 \u0995 \u0995\u0981\u1CDA \uA8F2\u0982", null),
        ("\u0995\u09CD\u0995\u09CD\u0995\u09CD\u0995\u09CD\u0995\u09CD\u0995\u09CD\u0995 \u0995\u09C1\u09C1\u09C1\u09C1\u09C1\u09C1 \u0995\u200C\u09BF \u0995\u200D\u09BF \u0995\u200C A\u0995. \u0995-\u0995", null),
        ("\u0995\u09CD\u09B7\u09BF \u09B0\u09CD\u0995\u09BF \u09B8\u09CD\u09A4\u09CD\u09B0\u09C0 \u0995\u09CD\u200D\u09A4 \u0995\u09BC\u09CD\u09AF", "-rphf"),
        ("\u0995\u09CD\u09B7\u09BF \u09B0\u09CD\u0995\u09BF \u09B8\u09CD\u09A4\u09CD\u09B0\u09C0 \u0995\u09CD\u200D\u09A4 \u0995\u09BC\u09CD\u09AF", "-half"),
        ("\u0995\u09CD\u09B7\u09BF \u09B0\u09CD\u0995\u09BF \u09B8\u09CD\u09A4\u09CD\u09B0\u09C0 \u0995\u09CD\u200D\u09A4 \u0995\u09BC\u09CD\u09AF", "-blwf,-pstf"),
        ("\u0995\u09CD\u09B7\u09BF \u09B0\u09CD\u0995\u09BF \u09B8\u09CD\u09A4\u09CD\u09B0\u09C0 \u0995\u09CD\u200D\u09A4 \u0995\u09BC\u09CD\u09AF", "-akhn,-cjct"),
        ("\u0995\u09CD\u09B7\u09BF \u09B0\u09CD\u0995\u09BF \u09B8\u09CD\u09A4\u09CD\u09B0\u09C0 \u0995\u09CD\u200D\u09A4 \u0995\u09BC\u09CD\u09AF", "-nukt"),
        ("\u0995\u09CD\u09B7\u09BF \u09B0\u09CD\u0995\u09BF \u09B8\u09CD\u09A4\u09CD\u09B0\u09C0 \u0995\u09CD\u200D\u09A4 \u0995\u09BC\u09CD\u09AF", "-init"),
        ("\u0995\u09CD\u09B7\u09BF \u09B0\u09CD\u0995\u09BF \u09B8\u09CD\u09A4\u09CD\u09B0\u09C0 \u0995\u09CD\u200D\u09A4 \u0995\u09BC\u09CD\u09AF", "-pres,-abvs,-blws,-psts"),
        ("\u0995\u09CD\u09B7\u09BF \u09B0\u09CD\u0995\u09BF \u09B8\u09CD\u09A4\u09CD\u09B0\u09C0 \u0995\u09CD\u200D\u09A4 \u0995\u09BC\u09CD\u09AF", "-vatu"),
        ("\u09AC\u09BE\u0982\u09B2\u09BE \u0995\u09CD\u09B7\u09A4\u09CD\u09B0\u09BF\u09AF\u09BC \u09B6\u09CD\u09B0\u09C0 \u0995\u09B0\u09CD\u09AE \u09A6\u09CD\u09AC\u09BF\u09A4\u09C0\u09AF\u09BC \u09AA\u09CD\u09B0\u09BE\u09B0\u09CD\u09A5\u09A8\u09BE", null),
        ("\u0995\u09CB \u0995\u09CC \u0995\u09C7\u09BE \u0995\u09C7\u09D7 \u09AF\u09BC \u09DF \u09A1\u09BC \u09DC \u09DD \u09CE \u09A4\u09CD\u200D \u09B0\u09CD\u09AF \u09B0\u200D\u09CD\u09AF \u09F0\u09CD\u0995 \u09F1\u09CD\u0995", null),
        ("\u0985\u09BE \u098B\u09C3 \u098C\u09E2 \u0995\u09BF \u09BF\u0995  \u0995\u09BF \u0995\u09BF\u0995\u09BF \u0995\u0981 \u0995\u09FE \u09FC\u09BE \u0995\u09C7 \u0995\u09C8", null),
    ];

    private static readonly ShapingText[] GurmukhiShapingCases =
    [
        ("\u0A15 \u0A15\u0A3E \u0A15\u0A3F \u0A15\u0A40 \u0A15\u0A41 \u0A15\u0A42 \u0A15\u0A47 \u0A15\u0A48 \u0A15\u0A4B \u0A15\u0A4C \u0A15\u0A02 \u0A15\u0A3F\u0A02 \u0A15\u0A3E\u0A03", null),
        ("\u0A15\u0A4D\u0A37 \u0A1C\u0A4D\u0A1E \u0A24\u0A4D\u0A30 \u0A15\u0A4D\u0A15 \u0A38\u0A4D\u0A24\u0A4D\u0A30 \u0A26\u0A4D\u0A2F \u0A15\u0A4D\u0A30\u0A3F \u0A26\u0A4D\u0A35 \u0A36\u0A4D\u0A30\u0A40 \u0A39\u0A4D\u0A2E", null),
        ("\u0A30\u0A4D\u0A15 \u0A30\u0A4D\u0A15\u0A3F \u0A30\u0A4D\u0A15\u0A4D\u0A37\u0A3E \u0A30\u0A4D \u0A30\u0A4D\u0A30 \u0A30\u0A30\u0A4D\u0A15 \u0A30\u0A4D\u0A15\u0A02 \u0A30\u0A4D\u0A15\u0A4B\u0A02", null),
        ("\u0A30\u0A4D\u200D\u0A15 \u0A30\u0A4D\u200C\u0A15 \u0A30\u200D\u0A4D\u0A15 \u0A30\u0A4D\u0A2F \u0A30\u0A4D\u200D\u0A2F \u0A30\u0A4D\u0A39", null),
        ("\u0A15\u0A4D\u200D\u0A37 \u0A15\u0A4D\u200C\u0A37 \u0A15\u200D\u0A4D\u0A37 \u0A15\u0A4D\u200D \u0A15\u0A4D\u200C \u0A15\u0A4D\u200D\u0A24\u0A3F \u0A15\u0A4D\u200C\u0A24\u0A3F", null),
        ("\u0A15\u0A4D\u0A24\u0A3F \u0A38\u0A4D\u0A24\u0A4D\u0A30\u0A3F \u0A15\u0A3F\u0A15\u0A3F \u0A3F \u0A15\u0A3C\u0A3F \u0A15\u0A3F\u0A3C \u0A24\u0A4D\u0A2F\u0A3F \u0A28\u0A4D\u0A26\u0A4D\u0A30\u0A3F", null),
        ("\u0A15\u0A4D \u0A15\u0A3C \u0A15\u0A3C\u0A4D\u0A37 \u0A15\u0A4D\u0A3C \u0A15\u0A3C\u0A3C \u0A15\u0A3C\u0A3C\u0A3C \u0A15\u0A4D\u0A4D \u0A15\u0A4D\u0A4D\u0A15", null),
        ("\u0A05 \u0A06 \u0A05\u0A3E \u0A07\u0A02 \u0A05\u0A02\u0A03 \u0A05\u0A4D\u0A15 \u0A05\u200D \u0A30\u0A4D\u0A05 \u0A30\u0A4D\u0A07", null),
        ("\u0A3E \u0A4D \u0A02 \u0A3C\u0A15 \u0A15\u0A3E\u0A3E \u0A15\u0A02\u0A3E \u0A15\u0A02\u0A02\u0A02 \u25CC\u0A3F \u00A0\u0A3F \u00A0\u0A4D\u0A30 \u25CC\u0A4D\u0A15 \u0A15\u0A03\u0A03", null),
        ("\u0A15\u0951 \u0A15\u0A41\u0952 \u0A15\u0A02\u0951\u0952 \u0A15\u0A02\u0A03 \u0A66\u0A67\u0A68 \u0A15\u0964 \u0A15\u0965 \u0A15 \u0A15\u0A01\u1CDA \uA8F2\u0A02", null),
        ("\u0A15\u0A4D\u0A15\u0A4D\u0A15\u0A4D\u0A15\u0A4D\u0A15\u0A4D\u0A15\u0A4D\u0A15 \u0A15\u0A41\u0A41\u0A41\u0A41\u0A41\u0A41 \u0A15\u200C\u0A3F \u0A15\u200D\u0A3F \u0A15\u200C A\u0A15. \u0A15-\u0A15", null),
        ("\u0A15\u0A4D\u0A37\u0A3F \u0A30\u0A4D\u0A15\u0A3F \u0A38\u0A4D\u0A24\u0A4D\u0A30\u0A40 \u0A15\u0A4D\u200D\u0A24 \u0A15\u0A3C\u0A4D\u0A2F", "-blwf"),
        ("\u0A15\u0A4D\u0A37\u0A3F \u0A30\u0A4D\u0A15\u0A3F \u0A38\u0A4D\u0A24\u0A4D\u0A30\u0A40 \u0A15\u0A4D\u200D\u0A24 \u0A15\u0A3C\u0A4D\u0A2F", "-pstf"),
        ("\u0A15\u0A4D\u0A37\u0A3F \u0A30\u0A4D\u0A15\u0A3F \u0A38\u0A4D\u0A24\u0A4D\u0A30\u0A40 \u0A15\u0A4D\u200D\u0A24 \u0A15\u0A3C\u0A4D\u0A2F", "-nukt"),
        ("\u0A15\u0A4D\u0A37\u0A3F \u0A30\u0A4D\u0A15\u0A3F \u0A38\u0A4D\u0A24\u0A4D\u0A30\u0A40 \u0A15\u0A4D\u200D\u0A24 \u0A15\u0A3C\u0A4D\u0A2F", "-abvs,-blws"),
        ("\u0A15\u0A4D\u0A37\u0A3F \u0A30\u0A4D\u0A15\u0A3F \u0A38\u0A4D\u0A24\u0A4D\u0A30\u0A40 \u0A15\u0A4D\u200D\u0A24 \u0A15\u0A3C\u0A4D\u0A2F", "-abvm,-blwm"),
        ("\u0A15\u0A4D\u0A37\u0A3F \u0A30\u0A4D\u0A15\u0A3F \u0A38\u0A4D\u0A24\u0A4D\u0A30\u0A40 \u0A15\u0A4D\u200D\u0A24 \u0A15\u0A3C\u0A4D\u0A2F", "-calt"),
        ("\u0A2A\u0A70\u0A1C\u0A3E\u0A2C\u0A40 \u0A38\u0A3F\u0A70\u0A18 \u0A2A\u0A4D\u0A30\u0A47\u0A2E \u0A17\u0A41\u0A30\u0A42 \u0A2A\u0A4D\u0A30\u0A3E\u0A30\u0A4D\u0A25\u0A28\u0A3E", null),
        ("\u0A15\u0A02\u0A40 \u0A15\u0A40\u0A02 \u0A15\u0A02\u0A3E \u0A15\u0A70 \u0A71\u0A15 \u0A15\u0A71\u0A15 \u0A15\u0A4D\u0A39 \u0A15\u0A4D\u0A35 \u0A15\u0A75 \u0A15\u0A51 \u0A15\u0A3E\u0A51 \u0A15\u0A41\u0A51", null),
        ("\u0A05\u0A3E \u0A05\u0A48 \u0A72\u0A3F \u0A72\u0A40 \u0A73\u0A41 \u0A73\u0A4B \u0A72 \u0A73 \u0A36 \u0A59 \u0A15\u0A3C\u0A4D\u0A30 \u0A5C\u0A4D", null),
    ];

    private static readonly ShapingText[] GujaratiShapingCases =
    [
        ("\u0A95 \u0A95\u0ABE \u0A95\u0ABF \u0A95\u0AC0 \u0A95\u0AC1 \u0A95\u0AC2 \u0A95\u0AC7 \u0A95\u0AC8 \u0A95\u0ACB \u0A95\u0ACC \u0A95\u0A82 \u0A95\u0ABF\u0A82 \u0A95\u0ABE\u0A83", null),
        ("\u0A95\u0ACD\u0AB7 \u0A9C\u0ACD\u0A9E \u0AA4\u0ACD\u0AB0 \u0A95\u0ACD\u0A95 \u0AB8\u0ACD\u0AA4\u0ACD\u0AB0 \u0AA6\u0ACD\u0AAF \u0A95\u0ACD\u0AB0\u0ABF \u0AA6\u0ACD\u0AB5 \u0AB6\u0ACD\u0AB0\u0AC0 \u0AB9\u0ACD\u0AAE", null),
        ("\u0AB0\u0ACD\u0A95 \u0AB0\u0ACD\u0A95\u0ABF \u0AB0\u0ACD\u0A95\u0ACD\u0AB7\u0ABE \u0AB0\u0ACD \u0AB0\u0ACD\u0AB0 \u0AB0\u0AB0\u0ACD\u0A95 \u0AB0\u0ACD\u0A95\u0A82 \u0AB0\u0ACD\u0A95\u0ACB\u0A82", null),
        ("\u0AB0\u0ACD\u200D\u0A95 \u0AB0\u0ACD\u200C\u0A95 \u0AB0\u200D\u0ACD\u0A95 \u0AB0\u0ACD\u0AAF \u0AB0\u0ACD\u200D\u0AAF \u0AB0\u0ACD\u0AB9", null),
        ("\u0A95\u0ACD\u200D\u0AB7 \u0A95\u0ACD\u200C\u0AB7 \u0A95\u200D\u0ACD\u0AB7 \u0A95\u0ACD\u200D \u0A95\u0ACD\u200C \u0A95\u0ACD\u200D\u0AA4\u0ABF \u0A95\u0ACD\u200C\u0AA4\u0ABF", null),
        ("\u0A95\u0ACD\u0AA4\u0ABF \u0AB8\u0ACD\u0AA4\u0ACD\u0AB0\u0ABF \u0A95\u0ABF\u0A95\u0ABF \u0ABF \u0A95\u0ABC\u0ABF \u0A95\u0ABF\u0ABC \u0AA4\u0ACD\u0AAF\u0ABF \u0AA8\u0ACD\u0AA6\u0ACD\u0AB0\u0ABF", null),
        ("\u0A95\u0ACD \u0A95\u0ABC \u0A95\u0ABC\u0ACD\u0AB7 \u0A95\u0ACD\u0ABC \u0A95\u0ABC\u0ABC \u0A95\u0ABC\u0ABC\u0ABC \u0A95\u0ACD\u0ACD \u0A95\u0ACD\u0ACD\u0A95", null),
        ("\u0A85 \u0A86 \u0A85\u0ABE \u0A87\u0A82 \u0A85\u0A82\u0A83 \u0A85\u0ACD\u0A95 \u0A85\u200D \u0AB0\u0ACD\u0A85 \u0AB0\u0ACD\u0A87", null),
        ("\u0ABE \u0ACD \u0A82 \u0ABC\u0A95 \u0A95\u0ABE\u0ABE \u0A95\u0A82\u0ABE \u0A95\u0A82\u0A82\u0A82 \u25CC\u0ABF \u00A0\u0ABF \u00A0\u0ACD\u0AB0 \u25CC\u0ACD\u0A95 \u0A95\u0A83\u0A83", null),
        ("\u0A95\u0951 \u0A95\u0AC1\u0952 \u0A95\u0A82\u0951\u0952 \u0A95\u0A82\u0A83 \u0AE6\u0AE7\u0AE8 \u0A95\u0964 \u0A95\u0965 \u0A95 \u0A95\u0A81\u1CDA \uA8F2\u0A82", null),
        ("\u0A95\u0ACD\u0A95\u0ACD\u0A95\u0ACD\u0A95\u0ACD\u0A95\u0ACD\u0A95\u0ACD\u0A95 \u0A95\u0AC1\u0AC1\u0AC1\u0AC1\u0AC1\u0AC1 \u0A95\u200C\u0ABF \u0A95\u200D\u0ABF \u0A95\u200C A\u0A95. \u0A95-\u0A95", null),
        ("\u0A95\u0ACD\u0AB7\u0ABF \u0AB0\u0ACD\u0A95\u0ABF \u0AB8\u0ACD\u0AA4\u0ACD\u0AB0\u0AC0 \u0A95\u0ACD\u200D\u0AA4 \u0A95\u0ABC\u0ACD\u0AAF", "-rphf"),
        ("\u0A95\u0ACD\u0AB7\u0ABF \u0AB0\u0ACD\u0A95\u0ABF \u0AB8\u0ACD\u0AA4\u0ACD\u0AB0\u0AC0 \u0A95\u0ACD\u200D\u0AA4 \u0A95\u0ABC\u0ACD\u0AAF", "-half"),
        ("\u0A95\u0ACD\u0AB7\u0ABF \u0AB0\u0ACD\u0A95\u0ABF \u0AB8\u0ACD\u0AA4\u0ACD\u0AB0\u0AC0 \u0A95\u0ACD\u200D\u0AA4 \u0A95\u0ABC\u0ACD\u0AAF", "-blwf"),
        ("\u0A95\u0ACD\u0AB7\u0ABF \u0AB0\u0ACD\u0A95\u0ABF \u0AB8\u0ACD\u0AA4\u0ACD\u0AB0\u0AC0 \u0A95\u0ACD\u200D\u0AA4 \u0A95\u0ABC\u0ACD\u0AAF", "-akhn,-cjct"),
        ("\u0A95\u0ACD\u0AB7\u0ABF \u0AB0\u0ACD\u0A95\u0ABF \u0AB8\u0ACD\u0AA4\u0ACD\u0AB0\u0AC0 \u0A95\u0ACD\u200D\u0AA4 \u0A95\u0ABC\u0ACD\u0AAF", "-nukt"),
        ("\u0A95\u0ACD\u0AB7\u0ABF \u0AB0\u0ACD\u0A95\u0ABF \u0AB8\u0ACD\u0AA4\u0ACD\u0AB0\u0AC0 \u0A95\u0ACD\u200D\u0AA4 \u0A95\u0ABC\u0ACD\u0AAF", "-pres,-abvs,-blws,-psts"),
        ("\u0A95\u0ACD\u0AB7\u0ABF \u0AB0\u0ACD\u0A95\u0ABF \u0AB8\u0ACD\u0AA4\u0ACD\u0AB0\u0AC0 \u0A95\u0ACD\u200D\u0AA4 \u0A95\u0ABC\u0ACD\u0AAF", "-haln"),
        ("\u0A95\u0ACD\u0AB7\u0ABF \u0AB0\u0ACD\u0A95\u0ABF \u0AB8\u0ACD\u0AA4\u0ACD\u0AB0\u0AC0 \u0A95\u0ACD\u200D\u0AA4 \u0A95\u0ABC\u0ACD\u0AAF", "-rkrf"),
        ("\u0A97\u0AC1\u0A9C\u0AB0\u0ABE\u0AA4\u0AC0 \u0A95\u0ACD\u0AB7\u0AA4\u0ACD\u0AB0\u0ABF\u0AAF \u0AB6\u0ACD\u0AB0\u0AC0 \u0A95\u0AB0\u0ACD\u0AAE \u0AA6\u0ACD\u0AB5\u0ABF\u0AA4\u0AC0\u0AAF \u0AAA\u0ACD\u0AB0\u0ABE\u0AB0\u0ACD\u0AA5\u0AA8\u0ABE", null),
        ("\u0A85\u0ABE \u0A85\u0AC5 \u0A85\u0AC7 \u0A85\u0ACC \u0AC5\u0ABE \u0A95\u0AC9 \u0A95\u0AFB \u0A95\u0ABF\u0AFB \u0A95\u0AFA \u0A95\u0AFC \u0A95\u0AE2 \u0AB9\u0ABC\u0ACD \u0AB0\u0ACD\u0AB9\u0ABC\u0ACD \u0A99\u0ACD\u0A97\u0ACD", null),
    ];

    private static readonly ShapingText[] OriyaShapingCases =
    [
        ("\u0B15 \u0B15\u0B3E \u0B15\u0B3F \u0B15\u0B40 \u0B15\u0B41 \u0B15\u0B42 \u0B15\u0B47 \u0B15\u0B48 \u0B15\u0B4B \u0B15\u0B4C \u0B15\u0B02 \u0B15\u0B3F\u0B02 \u0B15\u0B3E\u0B03", null),
        ("\u0B15\u0B4D\u0B37 \u0B1C\u0B4D\u0B1E \u0B24\u0B4D\u0B30 \u0B15\u0B4D\u0B15 \u0B38\u0B4D\u0B24\u0B4D\u0B30 \u0B26\u0B4D\u0B2F \u0B15\u0B4D\u0B30\u0B3F \u0B26\u0B4D\u0B35 \u0B36\u0B4D\u0B30\u0B40 \u0B39\u0B4D\u0B2E", null),
        ("\u0B30\u0B4D\u0B15 \u0B30\u0B4D\u0B15\u0B3F \u0B30\u0B4D\u0B15\u0B4D\u0B37\u0B3E \u0B30\u0B4D \u0B30\u0B4D\u0B30 \u0B30\u0B30\u0B4D\u0B15 \u0B30\u0B4D\u0B15\u0B02 \u0B30\u0B4D\u0B15\u0B4B\u0B02", null),
        ("\u0B30\u0B4D\u200D\u0B15 \u0B30\u0B4D\u200C\u0B15 \u0B30\u200D\u0B4D\u0B15 \u0B30\u0B4D\u0B2F \u0B30\u0B4D\u200D\u0B2F \u0B30\u0B4D\u0B39", null),
        ("\u0B15\u0B4D\u200D\u0B37 \u0B15\u0B4D\u200C\u0B37 \u0B15\u200D\u0B4D\u0B37 \u0B15\u0B4D\u200D \u0B15\u0B4D\u200C \u0B15\u0B4D\u200D\u0B24\u0B3F \u0B15\u0B4D\u200C\u0B24\u0B3F", null),
        ("\u0B15\u0B4D\u0B24\u0B3F \u0B38\u0B4D\u0B24\u0B4D\u0B30\u0B3F \u0B15\u0B3F\u0B15\u0B3F \u0B3F \u0B15\u0B3C\u0B3F \u0B15\u0B3F\u0B3C \u0B24\u0B4D\u0B2F\u0B3F \u0B28\u0B4D\u0B26\u0B4D\u0B30\u0B3F", null),
        ("\u0B15\u0B4D \u0B15\u0B3C \u0B15\u0B3C\u0B4D\u0B37 \u0B15\u0B4D\u0B3C \u0B15\u0B3C\u0B3C \u0B15\u0B3C\u0B3C\u0B3C \u0B15\u0B4D\u0B4D \u0B15\u0B4D\u0B4D\u0B15", null),
        ("\u0B05 \u0B06 \u0B05\u0B3E \u0B07\u0B02 \u0B05\u0B02\u0B03 \u0B05\u0B4D\u0B15 \u0B05\u200D \u0B30\u0B4D\u0B05 \u0B30\u0B4D\u0B07", null),
        ("\u0B3E \u0B4D \u0B02 \u0B3C\u0B15 \u0B15\u0B3E\u0B3E \u0B15\u0B02\u0B3E \u0B15\u0B02\u0B02\u0B02 \u25CC\u0B3F \u00A0\u0B3F \u00A0\u0B4D\u0B30 \u25CC\u0B4D\u0B15 \u0B15\u0B03\u0B03", null),
        ("\u0B15\u0951 \u0B15\u0B41\u0952 \u0B15\u0B02\u0951\u0952 \u0B15\u0B02\u0B03 \u0B66\u0B67\u0B68 \u0B15\u0964 \u0B15\u0965 \u0B15 \u0B15\u0B01\u1CDA \uA8F2\u0B02", null),
        ("\u0B15\u0B4D\u0B15\u0B4D\u0B15\u0B4D\u0B15\u0B4D\u0B15\u0B4D\u0B15\u0B4D\u0B15 \u0B15\u0B41\u0B41\u0B41\u0B41\u0B41\u0B41 \u0B15\u200C\u0B3F \u0B15\u200D\u0B3F \u0B15\u200C A\u0B15. \u0B15-\u0B15", null),
        ("\u0B15\u0B4D\u0B37\u0B3F \u0B30\u0B4D\u0B15\u0B3F \u0B38\u0B4D\u0B24\u0B4D\u0B30\u0B40 \u0B15\u0B4D\u200D\u0B24 \u0B15\u0B3C\u0B4D\u0B2F", "-rphf"),
        ("\u0B15\u0B4D\u0B37\u0B3F \u0B30\u0B4D\u0B15\u0B3F \u0B38\u0B4D\u0B24\u0B4D\u0B30\u0B40 \u0B15\u0B4D\u200D\u0B24 \u0B15\u0B3C\u0B4D\u0B2F", "-blwf,-pstf"),
        ("\u0B15\u0B4D\u0B37\u0B3F \u0B30\u0B4D\u0B15\u0B3F \u0B38\u0B4D\u0B24\u0B4D\u0B30\u0B40 \u0B15\u0B4D\u200D\u0B24 \u0B15\u0B3C\u0B4D\u0B2F", "-akhn,-cjct"),
        ("\u0B15\u0B4D\u0B37\u0B3F \u0B30\u0B4D\u0B15\u0B3F \u0B38\u0B4D\u0B24\u0B4D\u0B30\u0B40 \u0B15\u0B4D\u200D\u0B24 \u0B15\u0B3C\u0B4D\u0B2F", "-nukt"),
        ("\u0B15\u0B4D\u0B37\u0B3F \u0B30\u0B4D\u0B15\u0B3F \u0B38\u0B4D\u0B24\u0B4D\u0B30\u0B40 \u0B15\u0B4D\u200D\u0B24 \u0B15\u0B3C\u0B4D\u0B2F", "-pres,-abvs,-blws,-psts"),
        ("\u0B15\u0B4D\u0B37\u0B3F \u0B30\u0B4D\u0B15\u0B3F \u0B38\u0B4D\u0B24\u0B4D\u0B30\u0B40 \u0B15\u0B4D\u200D\u0B24 \u0B15\u0B3C\u0B4D\u0B2F", "-haln"),
        ("\u0B15\u0B4D\u0B37\u0B3F \u0B30\u0B4D\u0B15\u0B3F \u0B38\u0B4D\u0B24\u0B4D\u0B30\u0B40 \u0B15\u0B4D\u200D\u0B24 \u0B15\u0B3C\u0B4D\u0B2F", "-abvm,-blwm"),
        ("\u0B13\u0B21\u0B3C\u0B3F\u0B06 \u0B15\u0B4D\u0B37\u0B24\u0B4D\u0B30\u0B3F\u0B5F \u0B36\u0B4D\u0B30\u0B40 \u0B15\u0B30\u0B4D\u0B2E \u0B26\u0B4D\u0B2C\u0B3F\u0B24\u0B40\u0B5F \u0B2A\u0B4D\u0B30\u0B3E\u0B30\u0B4D\u0B25\u0B28\u0B3E", null),
        ("\u0B15\u0B48 \u0B15\u0B4B \u0B15\u0B4C \u0B15\u0B47\u0B56 \u0B15\u0B47\u0B3E \u0B15\u0B47\u0B57 \u0B15\u0B01 \u0B15\u0B40\u0B01 \u0B15\u0B01\u0B40 \u0B15\u0B3E\u0B01 \u0B15\u0B55 \u0B15\u0B3E\u0B55 \u0B5C \u0B5D \u0B21\u0B3C \u0B71", null),
        ("\u0B05\u0B3E \u0B0F\u0B57 \u0B13\u0B57 \u0B15\u0B4D\u0B2F \u0B15\u0B4D\u0B5F \u0B30\u0B4D\u0B15\u0B3F \u0B15\u0B3F \u0B15\u0B56 \u0B15\u0B44", null),
    ];

    private static readonly ShapingText[] TamilShapingCases =
    [
        ("\u0B95 \u0B95\u0BBE \u0B95\u0BBF \u0B95\u0BC0 \u0B95\u0BC1 \u0B95\u0BC2 \u0B95\u0BC7 \u0B95\u0BC8 \u0B95\u0BCB \u0B95\u0BCC \u0B95\u0B82 \u0B95\u0BBF\u0B82 \u0B95\u0BBE\u0B83", null),
        ("\u0B95\u0BCD\u0BB7 \u0B9C\u0BCD\u0B9E \u0BA4\u0BCD\u0BB0 \u0B95\u0BCD\u0B95 \u0BB8\u0BCD\u0BA4\u0BCD\u0BB0 \u0BA6\u0BCD\u0BAF \u0B95\u0BCD\u0BB0\u0BBF \u0BA6\u0BCD\u0BB5 \u0BB6\u0BCD\u0BB0\u0BC0 \u0BB9\u0BCD\u0BAE", null),
        ("\u0BB0\u0BCD\u0B95 \u0BB0\u0BCD\u0B95\u0BBF \u0BB0\u0BCD\u0B95\u0BCD\u0BB7\u0BBE \u0BB0\u0BCD \u0BB0\u0BCD\u0BB0 \u0BB0\u0BB0\u0BCD\u0B95 \u0BB0\u0BCD\u0B95\u0B82 \u0BB0\u0BCD\u0B95\u0BCB\u0B82", null),
        ("\u0BB0\u0BCD\u200D\u0B95 \u0BB0\u0BCD\u200C\u0B95 \u0BB0\u200D\u0BCD\u0B95 \u0BB0\u0BCD\u0BAF \u0BB0\u0BCD\u200D\u0BAF \u0BB0\u0BCD\u0BB9", null),
        ("\u0B95\u0BCD\u200D\u0BB7 \u0B95\u0BCD\u200C\u0BB7 \u0B95\u200D\u0BCD\u0BB7 \u0B95\u0BCD\u200D \u0B95\u0BCD\u200C \u0B95\u0BCD\u200D\u0BA4\u0BBF \u0B95\u0BCD\u200C\u0BA4\u0BBF", null),
        ("\u0B95\u0BCD\u0BA4\u0BBF \u0BB8\u0BCD\u0BA4\u0BCD\u0BB0\u0BBF \u0B95\u0BBF\u0B95\u0BBF \u0BBF \u0B95\u0BBC\u0BBF \u0B95\u0BBF\u0BBC \u0BA4\u0BCD\u0BAF\u0BBF \u0BA8\u0BCD\u0BA6\u0BCD\u0BB0\u0BBF", null),
        // Two lines: the reference shaper numbers the syllables from 1 to 15 and starts again, and does not show a
        // dotted circle for a syllable without base that has the number of the last one it showed one for.
        ("\u0B95\u0BCD \u0B95\u0BBC \u0B95\u0BBC\u0BCD\u0BB7 \u0B95\u0BCD\u0BBC", null),
        ("\u0B95\u0BBC\u0BBC \u0B95\u0BBC\u0BBC\u0BBC \u0B95\u0BCD\u0BCD \u0B95\u0BCD\u0BCD\u0B95", null),
        ("\u0B85 \u0B86 \u0B85\u0BBE \u0B87\u0B82 \u0B85\u0B82\u0B83 \u0B85\u0BCD\u0B95 \u0B85\u200D \u0BB0\u0BCD\u0B85 \u0BB0\u0BCD\u0B87", null),
        ("\u0BBE \u0BCD \u0B82 \u0BBC\u0B95 \u0B95\u0BBE\u0BBE \u0B95\u0B82\u0BBE \u0B95\u0B82\u0B82\u0B82 \u25CC\u0BBF \u00A0\u0BBF \u00A0\u0BCD\u0BB0 \u25CC\u0BCD\u0B95 \u0B95\u0B83\u0B83", null),
        ("\u0B95\u0951 \u0B95\u0BC1\u0952 \u0B95\u0B82\u0951\u0952 \u0B95\u0B82\u0B83 \u0BE6\u0BE7\u0BE8 \u0B95\u0964 \u0B95\u0965 \u0B95 \u0B95\u0B81\u1CDA \uA8F2\u0B82", null),
        ("\u0B95\u0BCD\u0B95\u0BCD\u0B95\u0BCD\u0B95\u0BCD\u0B95\u0BCD\u0B95\u0BCD\u0B95 \u0B95\u0BC1\u0BC1\u0BC1\u0BC1\u0BC1\u0BC1 \u0B95\u200C\u0BBF \u0B95\u200D\u0BBF \u0B95\u200C A\u0B95. \u0B95-\u0B95", null),
        ("\u0B95\u0BCD\u0BB7\u0BBF \u0BB0\u0BCD\u0B95\u0BBF \u0BB8\u0BCD\u0BA4\u0BCD\u0BB0\u0BC0 \u0B95\u0BCD\u200D\u0BA4 \u0B95\u0BBC\u0BCD\u0BAF", "-akhn"),
        ("\u0B95\u0BCD\u0BB7\u0BBF \u0BB0\u0BCD\u0B95\u0BBF \u0BB8\u0BCD\u0BA4\u0BCD\u0BB0\u0BC0 \u0B95\u0BCD\u200D\u0BA4 \u0B95\u0BBC\u0BCD\u0BAF", "-haln"),
        ("\u0B95\u0BCD\u0BB7\u0BBF \u0BB0\u0BCD\u0B95\u0BBF \u0BB8\u0BCD\u0BA4\u0BCD\u0BB0\u0BC0 \u0B95\u0BCD\u200D\u0BA4 \u0B95\u0BBC\u0BCD\u0BAF", "-abvs,-psts"),
        ("\u0B95\u0BCD\u0BB7\u0BBF \u0BB0\u0BCD\u0B95\u0BBF \u0BB8\u0BCD\u0BA4\u0BCD\u0BB0\u0BC0 \u0B95\u0BCD\u200D\u0BA4 \u0B95\u0BBC\u0BCD\u0BAF", "-pres"),
        ("\u0B95\u0BCD\u0BB7\u0BBF \u0BB0\u0BCD\u0B95\u0BBF \u0BB8\u0BCD\u0BA4\u0BCD\u0BB0\u0BC0 \u0B95\u0BCD\u200D\u0BA4 \u0B95\u0BBC\u0BCD\u0BAF", "-abvm,-blwm"),
        ("\u0BA4\u0BAE\u0BBF\u0BB4\u0BCD \u0BB8\u0BCD\u0BB0\u0BC0 \u0B95\u0BCD\u0BB7 \u0B95\u0BCD\u0BB7\u0BCC \u0BAA\u0BCA\u0BA9\u0BCD\u0BA9\u0BBF\u0BAF\u0BBF\u0BA9\u0BCD \u0B9A\u0BC6\u0BA9\u0BCD\u0BA9\u0BC8", null),
        ("\u0B95\u0BCA \u0B95\u0BCB \u0B95\u0BCC \u0B95\u0BC6\u0BBE \u0B95\u0BC7\u0BBE \u0B95\u0BC6\u0BD7 \u0B95\u0BC6 \u0B95\u0BC7 \u0B95\u0BC8 \u0B95\u0BC1 \u0B9F\u0BBF \u0B9F\u0BC0 \u0BB2\u0BC8 \u0BA3\u0BC8 \u0BA9\u0BC8", null),
        ("\u0B85\u0BC2 \u0B94 \u0B92\u0BD7 \u0B83 \u0B95\u0B82 \u0B95\u0B83 \u0B83\u0BAA \u0B95\u0BCD \u0B95\u0BCD\u0B95 \u0BB0\u0BCD\u0B95 \u0BB1\u0BCD\u0BB1 \u0BF3 \u0BE7\u0BE8\u0BF0", null),
    ];

    private static readonly ShapingText[] TeluguShapingCases =
    [
        ("\u0C15 \u0C15\u0C3E \u0C15\u0C3F \u0C15\u0C40 \u0C15\u0C41 \u0C15\u0C42 \u0C15\u0C47 \u0C15\u0C48 \u0C15\u0C4B \u0C15\u0C4C \u0C15\u0C02 \u0C15\u0C3F\u0C02 \u0C15\u0C3E\u0C03", null),
        ("\u0C15\u0C4D\u0C37 \u0C1C\u0C4D\u0C1E \u0C24\u0C4D\u0C30 \u0C15\u0C4D\u0C15 \u0C38\u0C4D\u0C24\u0C4D\u0C30 \u0C26\u0C4D\u0C2F \u0C15\u0C4D\u0C30\u0C3F \u0C26\u0C4D\u0C35 \u0C36\u0C4D\u0C30\u0C40 \u0C39\u0C4D\u0C2E", null),
        ("\u0C30\u0C4D\u0C15 \u0C30\u0C4D\u0C15\u0C3F \u0C30\u0C4D\u0C15\u0C4D\u0C37\u0C3E \u0C30\u0C4D \u0C30\u0C4D\u0C30 \u0C30\u0C30\u0C4D\u0C15 \u0C30\u0C4D\u0C15\u0C02 \u0C30\u0C4D\u0C15\u0C4B\u0C02", null),
        ("\u0C30\u0C4D\u200D\u0C15 \u0C30\u0C4D\u200C\u0C15 \u0C30\u200D\u0C4D\u0C15 \u0C30\u0C4D\u0C2F \u0C30\u0C4D\u200D\u0C2F \u0C30\u0C4D\u0C39", null),
        ("\u0C15\u0C4D\u200D\u0C37 \u0C15\u0C4D\u200C\u0C37 \u0C15\u200D\u0C4D\u0C37 \u0C15\u0C4D\u200D \u0C15\u0C4D\u200C \u0C15\u0C4D\u200D\u0C24\u0C3F \u0C15\u0C4D\u200C\u0C24\u0C3F", null),
        ("\u0C15\u0C4D\u0C24\u0C3F \u0C38\u0C4D\u0C24\u0C4D\u0C30\u0C3F \u0C15\u0C3F\u0C15\u0C3F \u0C3F \u0C15\u0C3C\u0C3F \u0C15\u0C3F\u0C3C \u0C24\u0C4D\u0C2F\u0C3F \u0C28\u0C4D\u0C26\u0C4D\u0C30\u0C3F", null),
        ("\u0C15\u0C4D \u0C15\u0C3C \u0C15\u0C3C\u0C4D\u0C37 \u0C15\u0C4D\u0C3C \u0C15\u0C3C\u0C3C \u0C15\u0C3C\u0C3C\u0C3C \u0C15\u0C4D\u0C4D \u0C15\u0C4D\u0C4D\u0C15", null),
        ("\u0C05 \u0C06 \u0C05\u0C3E \u0C07\u0C02 \u0C05\u0C02\u0C03 \u0C05\u0C4D\u0C15 \u0C05\u200D \u0C30\u0C4D\u0C05 \u0C30\u0C4D\u0C07", null),
        ("\u0C3E \u0C4D \u0C02 \u0C3C\u0C15 \u0C15\u0C3E\u0C3E \u0C15\u0C02\u0C3E \u0C15\u0C02\u0C02\u0C02 \u25CC\u0C3F \u00A0\u0C3F \u00A0\u0C4D\u0C30 \u25CC\u0C4D\u0C15 \u0C15\u0C03\u0C03", null),
        ("\u0C15\u0951 \u0C15\u0C41\u0952 \u0C15\u0C02\u0951\u0952 \u0C15\u0C02\u0C03 \u0C66\u0C67\u0C68 \u0C15\u0964 \u0C15\u0965 \u0C15 \u0C15\u0C01\u1CDA \uA8F2\u0C02", null),
        ("\u0C15\u0C4D\u0C15\u0C4D\u0C15\u0C4D\u0C15\u0C4D\u0C15\u0C4D\u0C15\u0C4D\u0C15 \u0C15\u0C41\u0C41\u0C41\u0C41\u0C41\u0C41 \u0C15\u200C\u0C3F \u0C15\u200D\u0C3F \u0C15\u200C A\u0C15. \u0C15-\u0C15", null),
        ("\u0C15\u0C4D\u0C37\u0C3F \u0C30\u0C4D\u0C15\u0C3F \u0C38\u0C4D\u0C24\u0C4D\u0C30\u0C40 \u0C15\u0C4D\u200D\u0C24 \u0C15\u0C3C\u0C4D\u0C2F", "-rphf"),
        ("\u0C15\u0C4D\u0C37\u0C3F \u0C30\u0C4D\u0C15\u0C3F \u0C38\u0C4D\u0C24\u0C4D\u0C30\u0C40 \u0C15\u0C4D\u200D\u0C24 \u0C15\u0C3C\u0C4D\u0C2F", "-blwf"),
        ("\u0C15\u0C4D\u0C37\u0C3F \u0C30\u0C4D\u0C15\u0C3F \u0C38\u0C4D\u0C24\u0C4D\u0C30\u0C40 \u0C15\u0C4D\u200D\u0C24 \u0C15\u0C3C\u0C4D\u0C2F", "-akhn"),
        ("\u0C15\u0C4D\u0C37\u0C3F \u0C30\u0C4D\u0C15\u0C3F \u0C38\u0C4D\u0C24\u0C4D\u0C30\u0C40 \u0C15\u0C4D\u200D\u0C24 \u0C15\u0C3C\u0C4D\u0C2F", "-haln"),
        ("\u0C15\u0C4D\u0C37\u0C3F \u0C30\u0C4D\u0C15\u0C3F \u0C38\u0C4D\u0C24\u0C4D\u0C30\u0C40 \u0C15\u0C4D\u200D\u0C24 \u0C15\u0C3C\u0C4D\u0C2F", "-abvs,-blws,-psts"),
        ("\u0C15\u0C4D\u0C37\u0C3F \u0C30\u0C4D\u0C15\u0C3F \u0C38\u0C4D\u0C24\u0C4D\u0C30\u0C40 \u0C15\u0C4D\u200D\u0C24 \u0C15\u0C3C\u0C4D\u0C2F", "-abvm,-blwm"),
        ("\u0C24\u0C46\u0C32\u0C41\u0C17\u0C41 \u0C15\u0C4D\u0C37 \u0C36\u0C4D\u0C30\u0C40 \u0C15\u0C30\u0C4D\u0C2E \u0C26\u0C4D\u0C35\u0C3F\u0C24\u0C40\u0C2F \u0C2A\u0C4D\u0C30\u0C3E\u0C30\u0C4D\u0C25\u0C28", null),
        ("\u0C15\u0C48 \u0C15\u0C46\u0C56 \u0C15\u0C46\u0C56\u0C3C \u0C15\u0C3C\u0C48 \u0C15\u0C48\u0C4D \u0C15\u0C55 \u0C15\u0C56 \u0C15\u0C4A\u0C55 \u0C30\u0C4D\u200D\u0C15 \u0C30\u0C4D\u0C15 \u0C15\u0C4D\u0C15\u0C4D\u0C15 \u0C15\u0C4D\u0C30\u0C4D\u0C2F", null),
        ("\u0C12\u0C4C \u0C12\u0C55 \u0C3F\u0C55 \u0C15\u0C00 \u0C15\u0C04 \u0C15\u0C3E\u0C02 \u0C15\u0C62 \u0C58 \u0C5A \u0C34\u0C4D \u0C66\u0C67 \u0C15\u0C4D\u200C\u0C15 \u0C15\u0C4D\u200D\u0C15", null),
    ];

    private static readonly ShapingText[] KannadaShapingCases =
    [
        ("\u0C95 \u0C95\u0CBE \u0C95\u0CBF \u0C95\u0CC0 \u0C95\u0CC1 \u0C95\u0CC2 \u0C95\u0CC7 \u0C95\u0CC8 \u0C95\u0CCB \u0C95\u0CCC \u0C95\u0C82 \u0C95\u0CBF\u0C82 \u0C95\u0CBE\u0C83", null),
        ("\u0C95\u0CCD\u0CB7 \u0C9C\u0CCD\u0C9E \u0CA4\u0CCD\u0CB0 \u0C95\u0CCD\u0C95 \u0CB8\u0CCD\u0CA4\u0CCD\u0CB0 \u0CA6\u0CCD\u0CAF \u0C95\u0CCD\u0CB0\u0CBF \u0CA6\u0CCD\u0CB5 \u0CB6\u0CCD\u0CB0\u0CC0 \u0CB9\u0CCD\u0CAE", null),
        ("\u0CB0\u0CCD\u0C95 \u0CB0\u0CCD\u0C95\u0CBF \u0CB0\u0CCD\u0C95\u0CCD\u0CB7\u0CBE \u0CB0\u0CCD \u0CB0\u0CCD\u0CB0 \u0CB0\u0CB0\u0CCD\u0C95 \u0CB0\u0CCD\u0C95\u0C82 \u0CB0\u0CCD\u0C95\u0CCB\u0C82", null),
        ("\u0CB0\u0CCD\u200D\u0C95 \u0CB0\u0CCD\u200C\u0C95 \u0CB0\u200D\u0CCD\u0C95 \u0CB0\u0CCD\u0CAF \u0CB0\u0CCD\u200D\u0CAF \u0CB0\u0CCD\u0CB9", null),
        ("\u0C95\u0CCD\u200D\u0CB7 \u0C95\u0CCD\u200C\u0CB7 \u0C95\u200D\u0CCD\u0CB7 \u0C95\u0CCD\u200D \u0C95\u0CCD\u200C \u0C95\u0CCD\u200D\u0CA4\u0CBF \u0C95\u0CCD\u200C\u0CA4\u0CBF", null),
        ("\u0C95\u0CCD\u0CA4\u0CBF \u0CB8\u0CCD\u0CA4\u0CCD\u0CB0\u0CBF \u0C95\u0CBF\u0C95\u0CBF \u0CBF \u0C95\u0CBC\u0CBF \u0C95\u0CBF\u0CBC \u0CA4\u0CCD\u0CAF\u0CBF \u0CA8\u0CCD\u0CA6\u0CCD\u0CB0\u0CBF", null),
        ("\u0C95\u0CCD \u0C95\u0CBC \u0C95\u0CBC\u0CCD\u0CB7 \u0C95\u0CCD\u0CBC \u0C95\u0CBC\u0CBC \u0C95\u0CBC\u0CBC\u0CBC \u0C95\u0CCD\u0CCD \u0C95\u0CCD\u0CCD\u0C95", null),
        ("\u0C85 \u0C86 \u0C85\u0CBE \u0C87\u0C82 \u0C85\u0C82\u0C83 \u0C85\u0CCD\u0C95 \u0C85\u200D \u0CB0\u0CCD\u0C85 \u0CB0\u0CCD\u0C87", null),
        ("\u0CBE \u0CCD \u0C82 \u0CBC\u0C95 \u0C95\u0CBE\u0CBE \u0C95\u0C82\u0CBE \u0C95\u0C82\u0C82\u0C82 \u25CC\u0CBF \u00A0\u0CBF \u00A0\u0CCD\u0CB0 \u25CC\u0CCD\u0C95 \u0C95\u0C83\u0C83", null),
        ("\u0C95\u0951 \u0C95\u0CC1\u0952 \u0C95\u0C82\u0951\u0952 \u0C95\u0C82\u0C83 \u0CE6\u0CE7\u0CE8 \u0C95\u0964 \u0C95\u0965 \u0C95 \u0C95\u0C81\u1CDA \uA8F2\u0C82", null),
        ("\u0C95\u0CCD\u0C95\u0CCD\u0C95\u0CCD\u0C95\u0CCD\u0C95\u0CCD\u0C95\u0CCD\u0C95 \u0C95\u0CC1\u0CC1\u0CC1\u0CC1\u0CC1\u0CC1 \u0C95\u200C\u0CBF \u0C95\u200D\u0CBF \u0C95\u200C A\u0C95. \u0C95-\u0C95", null),
        ("\u0C95\u0CCD\u0CB7\u0CBF \u0CB0\u0CCD\u0C95\u0CBF \u0CB8\u0CCD\u0CA4\u0CCD\u0CB0\u0CC0 \u0C95\u0CCD\u200D\u0CA4 \u0C95\u0CBC\u0CCD\u0CAF", "-rphf"),
        ("\u0C95\u0CCD\u0CB7\u0CBF \u0CB0\u0CCD\u0C95\u0CBF \u0CB8\u0CCD\u0CA4\u0CCD\u0CB0\u0CC0 \u0C95\u0CCD\u200D\u0CA4 \u0C95\u0CBC\u0CCD\u0CAF", "-blwf"),
        ("\u0C95\u0CCD\u0CB7\u0CBF \u0CB0\u0CCD\u0C95\u0CBF \u0CB8\u0CCD\u0CA4\u0CCD\u0CB0\u0CC0 \u0C95\u0CCD\u200D\u0CA4 \u0C95\u0CBC\u0CCD\u0CAF", "-akhn"),
        ("\u0C95\u0CCD\u0CB7\u0CBF \u0CB0\u0CCD\u0C95\u0CBF \u0CB8\u0CCD\u0CA4\u0CCD\u0CB0\u0CC0 \u0C95\u0CCD\u200D\u0CA4 \u0C95\u0CBC\u0CCD\u0CAF", "-haln"),
        ("\u0C95\u0CCD\u0CB7\u0CBF \u0CB0\u0CCD\u0C95\u0CBF \u0CB8\u0CCD\u0CA4\u0CCD\u0CB0\u0CC0 \u0C95\u0CCD\u200D\u0CA4 \u0C95\u0CBC\u0CCD\u0CAF", "-abvs,-blws,-psts"),
        ("\u0C95\u0CCD\u0CB7\u0CBF \u0CB0\u0CCD\u0C95\u0CBF \u0CB8\u0CCD\u0CA4\u0CCD\u0CB0\u0CC0 \u0C95\u0CCD\u200D\u0CA4 \u0C95\u0CBC\u0CCD\u0CAF", "-nukt"),
        ("\u0C95\u0CCD\u0CB7\u0CBF \u0CB0\u0CCD\u0C95\u0CBF \u0CB8\u0CCD\u0CA4\u0CCD\u0CB0\u0CC0 \u0C95\u0CCD\u200D\u0CA4 \u0C95\u0CBC\u0CCD\u0CAF", "-abvm,-blwm"),
        ("\u0C95\u0CA8\u0CCD\u0CA8\u0CA1 \u0C95\u0CCD\u0CB7 \u0CB6\u0CCD\u0CB0\u0CC0 \u0C95\u0CB0\u0CCD\u0CAE \u0CA6\u0CCD\u0CB5\u0CBF\u0CA4\u0CC0\u0CAF \u0CAA\u0CCD\u0CB0\u0CBE\u0CB0\u0CCD\u0CA5\u0CA8", null),
        ("\u0C95\u0CC0 \u0C95\u0CC7 \u0C95\u0CC8 \u0C95\u0CCA \u0C95\u0CCB \u0C95\u0CBF\u0CD5 \u0C95\u0CC6\u0CD5 \u0C95\u0CC6\u0CC2\u0CD5 \u0CB0\u0CCD\u200D\u0C95 \u0CB0\u200D\u0CCD\u0C95 \u0CB0\u0CCD\u0C95\u0CCD\u0C95 \u0C95\u0CCD\u0C95\u0CCD\u0C95", null),
        ("\u0C89\u0CBE \u0C8B\u0CBE \u0C92\u0CCC \u0C95\u0C81 \u0C80\u0CBE \u0C95\u0CBC\u0CCD\u0CB0 \u0CDE \u0CB1\u0CCD \u0CF1 \u0CF2 \u0C95\u0CE2 \u0CE6\u0CE7 \u0C95\u0CCD\u200C\u0C95 \u0C95\u0CCD\u200D\u0C95", null),
    ];

    private static readonly ShapingText[] MalayalamShapingCases =
    [
        ("\u0D15 \u0D15\u0D3E \u0D15\u0D3F \u0D15\u0D40 \u0D15\u0D41 \u0D15\u0D42 \u0D15\u0D47 \u0D15\u0D48 \u0D15\u0D4B \u0D15\u0D4C \u0D15\u0D02 \u0D15\u0D3F\u0D02 \u0D15\u0D3E\u0D03", null),
        ("\u0D15\u0D4D\u0D37 \u0D1C\u0D4D\u0D1E \u0D24\u0D4D\u0D30 \u0D15\u0D4D\u0D15 \u0D38\u0D4D\u0D24\u0D4D\u0D30 \u0D26\u0D4D\u0D2F \u0D15\u0D4D\u0D30\u0D3F \u0D26\u0D4D\u0D35 \u0D36\u0D4D\u0D30\u0D40 \u0D39\u0D4D\u0D2E", null),
        ("\u0D30\u0D4D\u0D15 \u0D30\u0D4D\u0D15\u0D3F \u0D30\u0D4D\u0D15\u0D4D\u0D37\u0D3E \u0D30\u0D4D \u0D30\u0D4D\u0D30 \u0D30\u0D30\u0D4D\u0D15 \u0D30\u0D4D\u0D15\u0D02 \u0D30\u0D4D\u0D15\u0D4B\u0D02", null),
        ("\u0D30\u0D4D\u200D\u0D15 \u0D30\u0D4D\u200C\u0D15 \u0D30\u200D\u0D4D\u0D15 \u0D30\u0D4D\u0D2F \u0D30\u0D4D\u200D\u0D2F \u0D30\u0D4D\u0D39", null),
        ("\u0D15\u0D4D\u200D\u0D37 \u0D15\u0D4D\u200C\u0D37 \u0D15\u200D\u0D4D\u0D37 \u0D15\u0D4D\u200D \u0D15\u0D4D\u200C \u0D15\u0D4D\u200D\u0D24\u0D3F \u0D15\u0D4D\u200C\u0D24\u0D3F", null),
        ("\u0D15\u0D4D\u0D24\u0D3F \u0D38\u0D4D\u0D24\u0D4D\u0D30\u0D3F \u0D15\u0D3F\u0D15\u0D3F \u0D3F \u0D15\u0D3C\u0D3F \u0D15\u0D3F\u0D3C \u0D24\u0D4D\u0D2F\u0D3F \u0D28\u0D4D\u0D26\u0D4D\u0D30\u0D3F", null),
        ("\u0D15\u0D4D \u0D15\u0D3C \u0D15\u0D3C\u0D4D\u0D37 \u0D15\u0D4D\u0D3C \u0D15\u0D3C\u0D3C \u0D15\u0D3C\u0D3C\u0D3C \u0D15\u0D4D\u0D4D \u0D15\u0D4D\u0D4D\u0D15", null),
        ("\u0D05 \u0D06 \u0D05\u0D3E \u0D07\u0D02 \u0D05\u0D02\u0D03 \u0D05\u0D4D\u0D15 \u0D05\u200D \u0D30\u0D4D\u0D05 \u0D30\u0D4D\u0D07", null),
        ("\u0D3E \u0D4D \u0D02 \u0D3C\u0D15 \u0D15\u0D3E\u0D3E \u0D15\u0D02\u0D3E \u0D15\u0D02\u0D02\u0D02 \u25CC\u0D3F \u00A0\u0D3F \u00A0\u0D4D\u0D30 \u25CC\u0D4D\u0D15 \u0D15\u0D03\u0D03", null),
        ("\u0D15\u0951 \u0D15\u0D41\u0952 \u0D15\u0D02\u0951\u0952 \u0D15\u0D02\u0D03 \u0D66\u0D67\u0D68 \u0D15\u0964 \u0D15\u0965 \u0D15 \u0D15\u0D01\u1CDA \uA8F2\u0D02", null),
        ("\u0D15\u0D4D\u0D15\u0D4D\u0D15\u0D4D\u0D15\u0D4D\u0D15\u0D4D\u0D15\u0D4D\u0D15 \u0D15\u0D41\u0D41\u0D41\u0D41\u0D41\u0D41 \u0D15\u200C\u0D3F \u0D15\u200D\u0D3F \u0D15\u200C A\u0D15. \u0D15-\u0D15", null),
        ("\u0D15\u0D4D\u0D37\u0D3F \u0D30\u0D4D\u0D15\u0D3F \u0D38\u0D4D\u0D24\u0D4D\u0D30\u0D40 \u0D15\u0D4D\u200D\u0D24 \u0D15\u0D3C\u0D4D\u0D2F", "-akhn"),
        ("\u0D15\u0D4D\u0D37\u0D3F \u0D30\u0D4D\u0D15\u0D3F \u0D38\u0D4D\u0D24\u0D4D\u0D30\u0D40 \u0D15\u0D4D\u200D\u0D24 \u0D15\u0D3C\u0D4D\u0D2F", "-pref"),
        ("\u0D15\u0D4D\u0D37\u0D3F \u0D30\u0D4D\u0D15\u0D3F \u0D38\u0D4D\u0D24\u0D4D\u0D30\u0D40 \u0D15\u0D4D\u200D\u0D24 \u0D15\u0D3C\u0D4D\u0D2F", "-blwf"),
        ("\u0D15\u0D4D\u0D37\u0D3F \u0D30\u0D4D\u0D15\u0D3F \u0D38\u0D4D\u0D24\u0D4D\u0D30\u0D40 \u0D15\u0D4D\u200D\u0D24 \u0D15\u0D3C\u0D4D\u0D2F", "-pstf"),
        ("\u0D15\u0D4D\u0D37\u0D3F \u0D30\u0D4D\u0D15\u0D3F \u0D38\u0D4D\u0D24\u0D4D\u0D30\u0D40 \u0D15\u0D4D\u200D\u0D24 \u0D15\u0D3C\u0D4D\u0D2F", "-half"),
        ("\u0D15\u0D4D\u0D37\u0D3F \u0D30\u0D4D\u0D15\u0D3F \u0D38\u0D4D\u0D24\u0D4D\u0D30\u0D40 \u0D15\u0D4D\u200D\u0D24 \u0D15\u0D3C\u0D4D\u0D2F", "-haln"),
        ("\u0D15\u0D4D\u0D37\u0D3F \u0D30\u0D4D\u0D15\u0D3F \u0D38\u0D4D\u0D24\u0D4D\u0D30\u0D40 \u0D15\u0D4D\u200D\u0D24 \u0D15\u0D3C\u0D4D\u0D2F", "-psts"),
        ("\u0D15\u0D4D\u0D37\u0D3F \u0D30\u0D4D\u0D15\u0D3F \u0D38\u0D4D\u0D24\u0D4D\u0D30\u0D40 \u0D15\u0D4D\u200D\u0D24 \u0D15\u0D3C\u0D4D\u0D2F", "-abvm,-blwm"),
        ("\u0D2E\u0D32\u0D2F\u0D3E\u0D33\u0D02 \u0D15\u0D4D\u0D37 \u0D36\u0D4D\u0D30\u0D40 \u0D15\u0D4D\u0D30 \u0D15\u0D4D\u0D30\u0D46 \u0D2A\u0D4D\u0D30\u0D3E\u0D30\u0D4D\u0D25\u0D28 \u0D38\u0D4D\u0D35\u0D3E\u0D17\u0D24\u0D02", null),
        ("\u0D15\u0D4A \u0D15\u0D4B \u0D15\u0D4C \u0D15\u0D46\u0D3E \u0D15\u0D47\u0D3E \u0D15\u0D46\u0D57 \u0D15\u0D57 \u0D15\u0D46 \u0D15\u0D47 \u0D15\u0D48 \u0D15\u0D4D\u0D32 \u0D15\u0D4D\u0D2F \u0D15\u0D4D\u0D35 \u0D15\u0D4D\u0D30\u0D4D\u0D2F \u0D15\u0D4D\u0D32\u0D46", null),
        ("\u0D7A \u0D7B\u0D3E \u0D7C\u0D4D\u0D15 \u0D28\u0D4D\u200D \u0D30\u0D4D\u200D \u0D4E\u0D15 \u0D4E\u0D15\u0D3F \u0D4E \u0D30\u0D4D\u0D15 \u0D07\u0D57 \u0D0E\u0D46 \u0D12\u0D3E \u0D15\u0D00 \u0D04\u0D3E \u0D15\u0D3B \u0D15\u0D3C \u0D54 \u0D15\u0D4D\u200C\u0D15", null),
    ];

    // Khmer: words, the vowels with two parts, the subscript ro with other subscripts before and after it, the register
    // shifters, the robat, the signs, what is not a syllable, digits and punctuation, and the syllables of the first
    // lines without each group of features of the model.
    private static readonly ShapingText[] KhmerShapingCases =
    [
        ("\u1797\u17B6\u179F\u17B6\u1781\u17D2\u1798\u17C2\u179A \u1780\u1798\u17D2\u1796\u17BB\u1787\u17B6 \u179F\u17D2\u179A\u17B8 \u179F\u17D2\u178F\u17D2\u179A\u17B8", null),
        ("\u1780\u17BE \u1780\u17BF \u1780\u17C0 \u1780\u17C4 \u1780\u17C5 \u1780\u17C1 \u1780\u17C2 \u1780\u17C3 \u1780\u17B6 \u1780\u17B7 \u1780\u17BB", null),
        ("\u1784\u17D2\u179A\u17D2\u1782 \u1784\u17D2\u1782\u17D2\u179A \u1780\u17D2\u179A \u1780\u17D2\u179A\u17C1 \u1780\u17D2\u179A\u17BE \u1780\u17D2\u1780\u17D2\u1780\u17D2\u179A \u1780\u17D2\u179A\u17D2\u179A", null),
        ("\u179F\u17CA\u17B8 \u1784\u17C9\u17B7 \u17A2\u200C\u17CA\u17B7 \u17A2\u17BB\u17CA\u17C7 \u1780\u17CC \u1780\u17C9 \u1780\u200D\u17C9", null),
        ("\u1780\u17C6 \u1780\u17CB \u1780\u17C7 \u1780\u17C8 \u1780\u17BB\u17C6 \u1780\u17B6\u17C6 \u1780\u17D0 \u1780\u17DD \u1780\u17D3 \u1780\u17C6\u17C7", null),
        ("\u17D2\u1780 \u17B6 \u17C1 \u17BE \u17C6 \u17D2 \u1780\u17D2 \u1780\u17B6\u17B6 \u1780\u17C1\u17C1 \u25CC\u17C1 \u00A0\u17B7 \u17D9\u17D2\u1780", null),
        ("\u17E1\u17E2\u17E3 \u17A5 \u17AB\u17C1 \u17B1\u17D2\u1780 \u1780\u17D4 \u17D5\u17D6 \u17DB\u17E1\u17E0 A\u1780. \u1780\u17B4 \u1780\u200C\u17D2\u1780", null),
        ("\u179F\u17D2\u179A\u17B8 \u1784\u17D2\u179A\u17D2\u1782 \u1780\u17BE \u179F\u17CA\u17B8", "-pref"),
        ("\u179F\u17D2\u179A\u17B8 \u1784\u17D2\u179A\u17D2\u1782 \u1780\u17BE \u179F\u17CA\u17B8", "-blwf,-abvf,-pstf"),
        ("\u179F\u17D2\u179A\u17B8 \u1784\u17D2\u179A\u17D2\u1782 \u1780\u17BE \u179F\u17CA\u17B8", "-cfar"),
        ("\u179F\u17D2\u179A\u17B8 \u1784\u17D2\u179A\u17D2\u1782 \u1780\u17BE \u179F\u17CA\u17B8", "-pres,-abvs,-blws,-psts,-clig"),
        ("\u179F\u17D2\u179A\u17B8 \u1780\u17C4", "liga"),
    ];

    // Myanmar: words, the medial consonants and the vowel drawn before the syllable, the marks whose order changes
    // (the anusvara and the vowels below), the kinzi, tones, what is not a syllable, digits, letters of the extended
    // blocks, joiners, and syllables without each group of features of the model.
    private static readonly ShapingText[] MyanmarShapingCases =
    [
        ("\u1019\u103C\u1014\u103A\u1019\u102C \u1000\u103B\u103D\u1014\u103A\u102F\u1015\u103A \u1021\u1004\u103A\u1039\u1002\u101C\u102D\u1015\u103A \u1015\u1005\u1039\u1005\u100A\u103A\u1038", null),
        ("\u1000\u103C\u103D\u1031 \u1000\u103B\u103D\u103E \u1000\u1031 \u1000\u1031\u102C \u1000\u1031\u1031 \u1000\u103C\u1031 \u1000\u1039\u1000\u1031 \u1000\u103C\u1031\u102C\u103A", null),
        ("\u1000\u102F\u1036 \u1000\u1036\u102F \u1000\u102D\u102F \u1000\u102F\u102D \u1000\u102D\u1036 \u1000\u1037 \u1000\u1037\u103A \u1000\u103A\u1037 \u1000\u1036\u1037 \u1000\u102F\u1036\u1037", null),
        ("\u1004\u103A\u1039\u1000 \u1004\u103A\u1039\u1000\u1031 \u1004\u103A\u1039\u1000\u103C \u1004\u103A\u1039 \u101B\u103A\u1039\u1000 \u105A\u103A\u1039\u1000 \u1004\u103A\u1000 \u1004\u1039\u1000", null),
        ("\u1000\u1038 \u1000\u102C\u1038 \u1000\u1038\u102C \u1000\u1087 \u1000\u1063 \u1000\u1064\u1036 \u1000\u109A \u1000\uAA7B \u1000\uAA7D\u102C \u1000\u102F\uAA7C", null),
        ("\u1031 \u103A \u1039\u1000 \u103C \u1036 \u1000\u1039 \u1000\u103B\u103B \u1000\u103E\u103D \u25CC\u1031 \u00A0\u102D \u104A\u1031 \u104B\u1031 -\u103C", null),
        ("\u1041\u1042\u1043 \u1040\u102D \u1021 \u1025\u102E \u1029 \u104E\u1004\u103A\u1038 \u103F \u1050\u1056 \u1075\u1085 \uAA60\uAA7A \uA9E0\uA9E5 \u1000\uFE00\u1031", null),
        ("\u1000\u200D \u1000\u200C\u1031 \u200D\u1031 \u1000\u1039\u200D\u1000 \u1000\u103A\u200C A\u1000. \u1000 \u1031", null),
        ("\u1021\u1004\u103A\u1039\u1002 \u1000\u103C\u1031 \u1015\u1005\u1039\u1005", "-rphf"),
        ("\u1021\u1004\u103A\u1039\u1002 \u1000\u103C\u1031 \u1015\u1005\u1039\u1005", "-pref"),
        ("\u1021\u1004\u103A\u1039\u1002 \u1000\u103C\u1031 \u1015\u1005\u1039\u1005", "-blwf,-pstf"),
        ("\u1021\u1004\u103A\u1039\u1002 \u1000\u103C\u1031 \u1015\u1005\u1039\u1005", "-pres,-abvs,-blws,-psts"),
        ("\u1021\u1004\u103A\u1039\u1002 \u1000\u103C\u1031 \u1015\u1005\u1039\u1005", "-liga,-mark,-mkmk"),
    ];

    // Tibetan: words, the vowel signs in an order that is not the one of shaping and those that have several parts, the
    // tsa -phru and the padma, the letters Unicode decomposes, the astrological signs, what is not a cluster, marks,
    // joiners and punctuation, and clusters without groups of features.
    private static readonly ShapingText[] TibetanShapingCases =
    [
        ("\u0F56\u0F7C\u0F51\u0F0B \u0F66\u0F90\u0F51\u0F0D \u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B \u0F62\u0FAB\u0F7C\u0F44 \u0F68\u0F7C\u0F7E\u0F0B", null),
        ("\u0F40\u0F71\u0F72 \u0F40\u0F72\u0F71 \u0F40\u0F74\u0F71 \u0F40\u0F71\u0F74\u0F7E \u0F40\u0F73 \u0F40\u0F75 \u0F40\u0F76 \u0F40\u0F77 \u0F40\u0F78 \u0F40\u0F79 \u0F40\u0F81", null),
        ("\u0F40\u0F74\u0F39 \u0F40\u0F39\u0F74 \u0F40\u0FC6\u0F72 \u0F40\u0F72\u0FC6 \u0F40\u0F39\u0F71\u0F74\u0F72", null),
        ("\u0F43 \u0F4D \u0F69 \u0F93 \u0FB9 \u0F40\u0FB5 \u0F40\u0F90\u0FB5", null),
        ("\u0F20\u0F18 \u0F21\u0F19 \u0F20\u0F3E \u0F20\u0F3F \u0F3F \u0F18 \u0F40\u0F3F\u0F72", null),
        ("\u0F72 \u0F90 \u0F84\u0F40 \u0F40\u0F84 \u0F40\u0F84\u0F40", null),
        ("\u25CC\u0F72 \u00A0\u0F74 \u0F40\u0F72\u0F90 \u0F40\u0F74\u0F72", null),
        ("\u0F40\u0F35 \u0F40\u0F37 \u0F40\u0F7E \u0F40\u0F7F \u0F40\u0F82 \u0F40\u0F83 \u0F40\u0F86 \u0F40\u0F7E\u0F72", null),
        ("\u0F40\u200D\u0F72 \u0F40\u200C\u0F72 \u0F40\u200C\u0F40 \u0F40\u034F\u0F72 A\u0F40. \u0F04\u0F05 \u0F3A\u0F40\u0F3B \u0F2A\u0F2B", null),

        // A long text: the font has more than two thousand subtables to try at each glyph.
        ("\u0F56\u0F7C\u0F51\u0F0B\u0F66\u0F90\u0F51\u0F0D \u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B \u0F56\u0F7C\u0F51\u0F0B\u0F66\u0F90\u0F51\u0F0D \u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B \u0F56\u0F7C\u0F51\u0F0B\u0F66\u0F90\u0F51\u0F0D \u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B \u0F56\u0F7C\u0F51\u0F0B\u0F66\u0F90\u0F51\u0F0D \u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B \u0F56\u0F7C\u0F51\u0F0B\u0F66\u0F90\u0F51\u0F0D \u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B \u0F56\u0F7C\u0F51\u0F0B\u0F66\u0F90\u0F51\u0F0D \u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B \u0F56\u0F7C\u0F51\u0F0B\u0F66\u0F90\u0F51\u0F0D \u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B \u0F56\u0F7C\u0F51\u0F0B\u0F66\u0F90\u0F51\u0F0D \u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B \u0F56\u0F7C\u0F51\u0F0B\u0F66\u0F90\u0F51\u0F0D \u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B \u0F56\u0F7C\u0F51\u0F0B\u0F66\u0F90\u0F51\u0F0D \u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B", null),
        ("\u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B \u0F40\u0F71\u0F74\u0F7E \u0F40\u0F39\u0F74 \u0F62\u0F90\u0FB1\u0F7A", "-abvs,-blws"),
        ("\u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B \u0F40\u0F71\u0F74\u0F7E \u0F40\u0F39\u0F74 \u0F62\u0F90\u0FB1\u0F7A", "-ccmp"),
        ("\u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B \u0F40\u0F71\u0F74\u0F7E \u0F40\u0F39\u0F74 \u0F62\u0F90\u0FB1\u0F7A", "-abvm,-blwm,-mark"),
        ("\u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B \u0F40\u0F71\u0F74\u0F7E \u0F40\u0F39\u0F74 \u0F62\u0F90\u0FB1\u0F7A", "-kern,-dist"),
        ("\u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B \u0F40\u0F71\u0F74\u0F7E \u0F40\u0F39\u0F74 \u0F62\u0F90\u0FB1\u0F7A", "-calt"),
    ];

    // Sinhala: words, the forms a consonant takes with a joiner (the rakaransaya, the yansaya, the repaya, conjuncts), the
    // vowel signs with a part before the consonant, the al-lakuna after a vowel sign, what is not a cluster, the vowel
    // letters a vowel sign is not written on, and clusters without groups of features.
    private static readonly ShapingText[] SinhalaShapingCases =
    [
        ("\u0DC3\u0DD2\u0D82\u0DC4\u0DBD \u0DC1\u0DCA\u200D\u0DBB\u0DD3 \u0DBD\u0D82\u0D9A\u0DCF\u0DC0 \u0D9A\u0DD8\u0DAD\u0DD2", null),
        ("\u0D9A\u0DCA\u200D\u0DBB \u0D9A\u0DCA\u200D\u0DBA \u0DBB\u0DCA\u200D\u0D9A \u0DBB\u0DCA\u200D\u0D9A\u0DD9 \u0D9A\u0DCA\u0DC2 \u0D9A\u0DCA\u200D\u0DC2 \u0D9A\u200D\u0DCA\u0DC2 \u0D9A\u0DCA\u200C\u0DC2", null),
        ("\u0D9A\u0DD9 \u0D9A\u0DDA \u0D9A\u0DDB \u0D9A\u0DDC \u0D9A\u0DDD \u0D9A\u0DDE \u0D9A\u0DD9\u0DCA \u0D9A\u0DD9\u0DCF\u0DCA \u0D9A\u0DCA\u200D\u0DBB\u0DDC", null),
        ("\u0D9A\u0DCF \u0D9A\u0DD2 \u0D9A\u0DD4 \u0D9A\u0DD6 \u0D9A\u0DDF \u0D9A\u0DF2 \u0D9A\u0D82 \u0D9A\u0D83 \u0D9A\u0DD2\u0D82 \u0D9A\u0D81", null),
        ("\u0DD9 \u0DCA \u0DCA\u0D9A \u0D82 \u0D9A\u0DCA \u0D9A\u0DCF\u0DD2 \u25CC\u0DD9 \u00A0\u0DD2 \u0D9A\u0DD9\u0DD9", null),
        ("\u0D85\u0DCF \u0D85\u0DD0 \u0D91\u0DCA \u0D91\u0DD9 \u0D8B\u0DDF \u0D94\u0DDF \u0D8D\u0DD8", null),
        ("\u0DE6\u0DE7 \u0DF4 A\u0D9A. \u0D9A\u034F\u0DD2 \u0D9A\uFE00\u0DD2", null),
        ("\u0DC1\u0DCA\u200D\u0DBB\u0DD3 \u0DBB\u0DCA\u200D\u0D9A\u0DD9 \u0D9A\u0DCA\u200D\u0DBA \u0D9A\u0DDC \u0D9A\u0DCA\u200D\u0DC2", "-rphf"),
        ("\u0DC1\u0DCA\u200D\u0DBB\u0DD3 \u0DBB\u0DCA\u200D\u0D9A\u0DD9 \u0D9A\u0DCA\u200D\u0DBA \u0D9A\u0DDC \u0D9A\u0DCA\u200D\u0DC2", "-akhn,-vatu,-pstf"),
        ("\u0DC1\u0DCA\u200D\u0DBB\u0DD3 \u0DBB\u0DCA\u200D\u0D9A\u0DD9 \u0D9A\u0DCA\u200D\u0DBA \u0D9A\u0DDC \u0D9A\u0DCA\u200D\u0DC2", "-pres,-abvs,-blws,-psts"),
        ("\u0DC1\u0DCA\u200D\u0DBB\u0DD3 \u0DBB\u0DCA\u200D\u0D9A\u0DD9 \u0D9A\u0DCA\u200D\u0DBA \u0D9A\u0DDC \u0D9A\u0DCA\u200D\u0DC2", "-ccmp"),
        ("\u0DC1\u0DCA\u200D\u0DBB\u0DD3 \u0DBB\u0DCA\u200D\u0D9A\u0DD9 \u0D9A\u0DCA\u200D\u0DBA \u0D9A\u0DDC \u0D9A\u0DCA\u200D\u0DC2", "-abvm,-mark,-dist,-kern"),
    ];

    // Javanese: words, the vowel sign drawn before the cluster, alone, with the tarung and after a stacked consonant, the
    // medial consonants (the cakra, the keret, the pengkal), the layar and the other signs, the pangkon, vowel signs in an
    // order a cluster does not have, what is not a cluster, and clusters without groups of features.
    private static readonly ShapingText[] JavaneseShapingCases =
    [
        ("\uA997\uA9AE \uA98F\uA9BA\uA9B4 \uA9B2\uA98F\uA9C0\uA9B1\uA9AB \uA9A0\uA9B8\uA9AD\uA9B6\uA9B1\uA9C0", null),
        ("\uA98F\uA9BA \uA98F\uA9BB \uA98F\uA9BA\uA9B4 \uA98F\uA9C0\uA9A0\uA9BA \uA98F\uA9C0\uA9A0\uA9BA\uA9B4 \uA98F\uA9BF\uA9BA \uA98F\uA9BE\uA9BA\uA9B4 \uA98F\uA9C0\uA9AB\uA9BA", null),
        ("\uA98F\uA9BF \uA98F\uA9BD \uA98F\uA9BE \uA98F\uA9BF\uA9B6 \uA98F\uA9B6\uA981 \uA98F\uA982 \uA98F\uA983 \uA98F\uA9B8\uA982 \uA98F\uA9B3 \uA98F\uA980 \uA98F\uA9BC\uA982", null),
        ("\uA98F\uA9C0 \uA98F\uA9C0\u200C\uA9A0 \uA98F\uA9C0\u200D\uA9A0 \uA98F\uA9C0\uA9A0\uA9C0\uA9A0 \uA98F\uA9B3\uA9C0\uA9A0", null),
        ("\uA9BA \uA9C0\uA98F \uA9BF \uA981 \u25CC\uA9BA\uA9B4 \u00A0\uA9B6 \uA98F\uA9B6\uA9BA \uA98F\uA9B4\uA9BA \uA98F\uA9BA\uA9BA \uA98F\uA982\uA9B6", null),
        ("\uA9D1\uA9D2 \uA9C7\uA98F\uA9C8 A\uA98F. \uA98F\u200C\uA9BA \uA98F\u034F\uA9B6 \uA984\uA9B4", null),
        ("\uA98F\uA9BA\uA9B4 \uA98F\uA9BF\uA9BA \uA98F\uA9C0\uA9A0\uA9BA \uA98F\uA9B8\uA982 \uA98F\uA9BD", "-pref"),
        ("\uA98F\uA9BA\uA9B4 \uA98F\uA9BF\uA9BA \uA98F\uA9C0\uA9A0\uA9BA \uA98F\uA9B8\uA982 \uA98F\uA9BD", "-akhn"),
        ("\uA98F\uA9BA\uA9B4 \uA98F\uA9BF\uA9BA \uA98F\uA9C0\uA9A0\uA9BA \uA98F\uA9B8\uA982 \uA98F\uA9BD", "-abvs,-blws,-pres,-psts"),
        ("\uA98F\uA9BA\uA9B4 \uA98F\uA9BF\uA9BA \uA98F\uA9C0\uA9A0\uA9BA \uA98F\uA9B8\uA982 \uA98F\uA9BD", "-rlig"),
        ("\uA98F\uA9BA\uA9B4 \uA98F\uA9BF\uA9BA \uA98F\uA9C0\uA9A0\uA9BA \uA98F\uA9B8\uA982 \uA98F\uA9BD", "-abvm,-mark,-kern"),
    ];

    // Balinese: words, the vowel signs Unicode decomposes and the ones drawn before the cluster, the letters a vowel sign
    // makes, with a joiner between the two, the adeg adeg, the surang and the other signs, marks on a musical symbol,
    // what is not a cluster, and clusters without groups of features.
    private static readonly ShapingText[] BalineseShapingCases =
    [
        ("\u1B29\u1B2E\u1B36 \u1B13\u1B40 \u1B13\u1B3E\u1B35 \u1B13\u1B41 \u1B13\u1B43 \u1B13\u1B3B \u1B13\u1B3D \u1B13\u1B3F", null),
        ("\u1B06 \u1B05\u1B35 \u1B05\u200D\u1B35 \u1B05\u200C\u1B35 \u1B08\u1B0A\u1B0C\u1B0E\u1B12 \u1B09\u1B35", null),
        ("\u1B13\u1B44\u1B13 \u1B13\u1B44\u1B2D\u1B3E \u1B13\u1B44 \u1B13\u1B44\u200C\u1B13 \u1B13\u1B44\u200D\u1B13 \u1B13\u1B44\u1B13\u1B44\u1B13 \u1B13\u1B34\u1B44\u1B13", null),
        ("\u1B13\u1B03 \u1B13\u1B36\u1B02 \u1B13\u1B04 \u1B13\u1B34 \u1B13\u1B01 \u1B13\u1B00 \u1B13\u1B38\u1B03 \u1B13\u1B3E\u1B02", null),
        ("\u1B3E \u1B44\u1B13 \u1B02 \u1B13\u1B38\u1B36 \u1B13\u1B35\u1B3E \u25CC\u1B3E \u00A0\u1B36 \u1B13\u1B3E\u1B3E", null),
        ("\u1B61\u1B6B \u1B61\u1B6C \u1B62\u1B6D\u1B6E \u1B6B \u1B51\u1B52 \u1B5A\u1B13\u1B5B A\u1B13. \u1B13\u034F\u1B36", null),
        ("\u1B13\u1B44\u1B13 \u1B13\u1B40 \u1B05\u1B35 \u1B13\u1B36\u1B02 \u1B13\u1B44\u1B2D\u1B3E", "-abvf,-blwf"),
        ("\u1B13\u1B44\u1B13 \u1B13\u1B40 \u1B05\u1B35 \u1B13\u1B36\u1B02 \u1B13\u1B44\u1B2D\u1B3E", "-ccmp"),
        ("\u1B13\u1B44\u1B13 \u1B13\u1B40 \u1B05\u1B35 \u1B13\u1B36\u1B02 \u1B13\u1B44\u1B2D\u1B3E", "-rlig"),
        ("\u1B13\u1B44\u1B13 \u1B13\u1B40 \u1B05\u1B35 \u1B13\u1B36\u1B02 \u1B13\u1B44\u1B2D\u1B3E", "-abvm,-mark,-dist"),
    ];

    // Tai Tham: the vowel signs drawn before the cluster, the medial ra, the sakot that stacks a consonant, before and
    // after a tone mark and after a vowel sign, the marks above and below, what is not a cluster, digits, and clusters
    // without groups of features.
    private static readonly ShapingText[] TaiThamShapingCases =
    [
        ("\u1A20\u1A6E\u1A63\u1A49 \u1A3E\u1A6E\u1A65\u1A20\u1A60\u1A45 \u1A32\u1A62\u1A75", null),
        ("\u1A20\u1A6E \u1A20\u1A6F \u1A20\u1A70 \u1A20\u1A71 \u1A20\u1A72 \u1A20\u1A6E\u1A63 \u1A20\u1A55\u1A6E \u1A20\u1A56 \u1A20\u1A55", null),
        ("\u1A20\u1A60\u1A20 \u1A20\u1A60 \u1A20\u1A60\u1A45\u1A6E \u1A20\u1A75\u1A60\u1A20 \u1A20\u1A60\u1A75 \u1A20\u1A63\u1A60\u1A20 \u1A20\u1A60\u1A20\u1A60\u1A20 \u1A20\u1A60\u1A3F\u1A63", null),
        ("\u1A20\u1A65\u1A75 \u1A20\u1A69\u1A65 \u1A20\u1A74 \u1A20\u1A7A \u1A20\u1A7F \u1A20\u1A62 \u1A20\u1A65\u1A76 \u1A20\u1A6B\u1A74 \u1A20\u1A58 \u1A20\u1A5A", null),
        ("\u1A6E \u1A60\u1A20 \u1A75 \u1A55 \u25CC\u1A6E \u00A0\u1A65 \u1A20\u1A6E\u1A6E \u1A20\u1A75\u1A65", null),
        ("\u1A20\u200C\u1A6E \u1A20\u200D\u1A60\u1A20 \u1A20\u1A60\u200C\u1A20 \u1A81\u1A82 \u1A91\u1A92 \u1AA0\u1A20\u1AA1 A\u1A20. \u1A53 \u1A20\u034F\u1A65", null),
        ("\u1A20\u1A60\u1A20 \u1A20\u1A75\u1A60\u1A20 \u1A20\u1A55\u1A6E \u1A20\u1A65\u1A75", "-ccmp"),
        ("\u1A20\u1A60\u1A20 \u1A20\u1A75\u1A60\u1A20 \u1A20\u1A55\u1A6E \u1A20\u1A65\u1A75", "-liga"),
        ("\u1A20\u1A60\u1A20 \u1A20\u1A75\u1A60\u1A20 \u1A20\u1A55\u1A6E \u1A20\u1A65\u1A75", "-mark"),
    ];

    // N'Ko: letters in each of their forms, with marks between them, the lajanyalan that joins as a letter does, joiners,
    // a space, digits and punctuation between letters, marks without a letter, and words without the features of the forms.
    private static readonly ShapingText[] NKoShapingCases =
    [
        ("\u07D2\u07DE\u07CF \u07D3\u07CA\u07D3 \u07D3 \u07D3\u07D3 \u07D3\u07D3\u07D3 \u07D3\u07CA \u07CA\u07D3 \u07D8\u07CA\u07EC\u07E1\u07CA", null),
        ("\u07D3\u07EC\u07D3\u07F2 \u07D3\u07EB\u07ED \u07D3\u07F3\u07D3 \u07D3\u07F2\u07EC \u07D3\u07FD\u07D3", null),
        ("\u07D3\u07FA\u07D3 \u07D3\u07FA \u07FA\u07D3 \u07D3\u07FA\u07FA\u07D3", null),
        ("\u07D3\u200C\u07D3 \u07D3\u200D \u200D\u07D3 \u07D3\u200D\u07D3 \u07D3\u200C \u07D3\u034F\u07D3", null),
        ("\u07D3\u07C1\u07C2\u07D3 \u07D3\u07F8\u07D3 \u07D3\u07F4\u07D3 \u07D3\u07F5\u07D3 \u07D3.\u07D3 \u07C1\u07C2\u07C3", null),
        ("\u07EC \u07F2\u07D3 \u25CC\u07EC \u00A0\u07F2 A\u07D3. \u07F6\u07D3\u07F7 \u07FE\u07FF", null),
        ("\u07D3\u07D3\u07D3 \u07D2\u07DE\u07CF \u07D3\u07EC\u07D3\u07F2 \u07D3\u07FA\u07D3", "-fina"),
        ("\u07D3\u07D3\u07D3 \u07D2\u07DE\u07CF \u07D3\u07EC\u07D3\u07F2 \u07D3\u07FA\u07D3", "-init,-medi"),
        ("\u07D3\u07D3\u07D3 \u07D2\u07DE\u07CF \u07D3\u07EC\u07D3\u07F2 \u07D3\u07FA\u07D3", "-ccmp"),
        ("\u07D3\u07D3\u07D3 \u07D2\u07DE\u07CF \u07D3\u07EC\u07D3\u07F2 \u07D3\u07FA\u07D3", "-mark,-kern"),
    ];

    // Mongolian: words, letters in each of their forms, the free variation selectors that choose a form, the vowel
    // separator and the narrow space before a suffix, the nirugu, joiners (and a joiner before a variation selector),
    // digits and punctuation, marks of Ali Gali, and words without groups of features.
    private static readonly ShapingText[] MongolianShapingCases =
    [
        ("\u182E\u1823\u1829\u182D\u1823\u182F \u182A\u1822\u1834\u1822\u182D \u1820 \u1820\u1820 \u1820\u1820\u1820 \u182D\u1820\u1835\u1820\u1837", null),
        ("\u1820\u180B \u182D\u180B\u1820 \u182D\u180C\u1820 \u1820\u180D \u1828\u180B\u1820 \u1833\u180B \u182D\u180B \u1836\u180B\u1822 \u1820\u180F", null),
        ("\u1833\u1820\u180E\u1820 \u1828\u180E\u1820 \u182D\u1820\u202F\u1836\u1822\u1828 \u182E\u1823\u202F\u1833\u1824 \u1820\u202F\u1824", null),
        ("\u1820\u180A\u1820 \u180A\u1820 \u1820\u180A \u1820\u1807\u1820 \u1820\u1806\u1820", null),
        ("\u1820\u200D \u200D\u1820 \u1820\u200D\u1820 \u1820\u200C\u1820 \u1897\u200D\u180D\u185E \u1820\u200D\u180B \u1820\u034F\u1820", null),
        ("\u1811\u1812\u1820 \u1820\u1802\u1820 \u1820\u1803 \u1800\u1820 \u1820 \u1820 A\u1820.", null),
        ("\u1887\u1885 \u1887\u18A9 \u1887\u1886\u1820 \u180B \u1885 \u1843\u1820 \u1844\u1845\u1846 \u1873\u1874", null),
        ("\u182E\u1823\u1829\u182D\u1823\u182F \u182D\u180B\u1820 \u1833\u1820\u180E\u1820 \u1820", "-isol"),
        ("\u182E\u1823\u1829\u182D\u1823\u182F \u182D\u180B\u1820 \u1833\u1820\u180E\u1820 \u1820", "-init,-medi,-fina"),
        ("\u182E\u1823\u1829\u182D\u1823\u182F \u182D\u180B\u1820 \u1833\u1820\u180E\u1820 \u1820", "-rclt"),
        ("\u182E\u1823\u1829\u182D\u1823\u182F \u182D\u180B\u1820 \u1833\u1820\u180E\u1820 \u1820", "-calt"),
        ("\u182E\u1823\u1829\u182D\u1823\u182F \u182D\u180B\u1820 \u1833\u1820\u180E\u1820 \u1820", "-rlig,-ccmp"),
        ("\u182E\u1823\u1829\u182D\u1823\u182F \u182D\u180B\u1820 \u1833\u1820\u180E\u1820 \u1820", "-mark,-abvm"),

        // The letters upright in a vertical line, with the vertical metrics of a font of TrueType outlines
        new("\u182E\u1823\u1829\u182D\u1823\u182F \u182A\u1822\u1834\u1822\u182D \u1820", null, Direction: "ttb"),
    ];

    // Adlam: capital and small letters in each of their forms, the marks above a letter (lengtheners, the gemination
    // mark, the nukta), the tatweel, joiners, a space, digits and punctuation, marks without a letter, and words without
    // groups of features.
    private static readonly ShapingText[] AdlamShapingCases =
    [
        ("\U0001E90A\U0001E92B\U0001E932\U0001E922 \U0001E91E\U0001E935\U0001E938 \U0001E901\U0001E922\U0001E944\U0001E923", null),
        ("\U0001E900\U0001E923\U0001E924\U0001E922\U0001E925 \U0001E922 \U0001E922\U0001E922 \U0001E922\U0001E922\U0001E922 \U0001E900\U0001E900 \U0001E90A\U0001E92B\U0001E932", null),
        ("\U0001E922\U0001E944\U0001E922 \U0001E922\U0001E94A \U0001E922\U0001E946\U0001E947 \U0001E922\U0001E945\U0001E922 \U0001E922\U0001E948\U0001E949 \U0001E922\U0001E94A\U0001E944", null),
        ("\U0001E94B\U0001E922 \U0001E922\U0001E94B \U0001E922\u0640\U0001E922 \U0001E922\u0640 \u0640\U0001E922", null),
        ("\U0001E922\u200C\U0001E922 \U0001E922\u200D \u200D\U0001E922 \U0001E922\u200D\U0001E922 \U0001E922\u034F\U0001E922 \U0001E922 \U0001E922", null),
        ("\U0001E951\U0001E952\U0001E922 \U0001E922\U0001E953\U0001E922 \U0001E95E\U0001E922! \U0001E95F\U0001E922? \U0001E922.\U0001E922", null),
        ("\U0001E944 \U0001E94A\U0001E922 \u25CC\U0001E944 \u00A0\U0001E94A A\U0001E922.", null),
        ("\U0001E922\U0001E922\U0001E922 \U0001E900\U0001E923\U0001E924\U0001E922\U0001E925 \U0001E922\U0001E944\U0001E922 \U0001E922\U0001E94A", "-fina"),
        ("\U0001E922\U0001E922\U0001E922 \U0001E900\U0001E923\U0001E924\U0001E922\U0001E925 \U0001E922\U0001E944\U0001E922 \U0001E922\U0001E94A", "-init,-medi"),
        ("\U0001E922\U0001E922\U0001E922 \U0001E900\U0001E923\U0001E924\U0001E922\U0001E925 \U0001E922\U0001E944\U0001E922 \U0001E922\U0001E94A", "-ccmp"),
        ("\U0001E922\U0001E922\U0001E922 \U0001E900\U0001E923\U0001E924\U0001E922\U0001E925 \U0001E922\U0001E944\U0001E922 \U0001E922\U0001E94A", "-mark,-abvm,-blwm"),
        ("\U0001E922\U0001E922\U0001E922 \U0001E900\U0001E923\U0001E924\U0001E922\U0001E925 \U0001E922\U0001E944\U0001E922 \U0001E922\U0001E94A", "-rlig"),
    ];

    // Cham: the vowel signs drawn before the cluster, the medial ra that a feature of the font moves there, the other
    // medial consonants, the final letters, vowel signs in an order a cluster does not have, what is not a cluster, and
    // clusters without groups of features.
    private static readonly ShapingText[] ChamShapingCases =
    [
        ("\uAA06\uAA2F\uAA20\uAA2A \uAA00\uAA06\uAA34\uAA2F\uAA4C \uAA0A\uAA2D\uAA43", null),
        ("\uAA06\uAA2F \uAA06\uAA30 \uAA06\uAA34 \uAA06\uAA34\uAA2F \uAA06\uAA34\uAA30 \uAA06\uAA33\uAA2A \uAA06\uAA33\uAA34 \uAA06\uAA35 \uAA06\uAA36", null),
        ("\uAA06\uAA29 \uAA06\uAA2A \uAA06\uAA2B \uAA06\uAA2D \uAA06\uAA2E \uAA06\uAA31 \uAA06\uAA32 \uAA06\uAA2A\uAA4C \uAA06\uAA43 \uAA06\uAA4D \uAA06\uAA2F\uAA4C", null),
        ("\uAA06\uAA2D\uAA2A \uAA06\uAA32\uAA29 \uAA06\uAA2F\uAA2F \uAA06\uAA4C\uAA2A \uAA06\uAA36\uAA35", null),
        ("\uAA2F \uAA34 \uAA4C \uAA29 \u25CC\uAA2F \u00A0\uAA2A \uAA06\u200C\uAA2F \uAA06\u200D\uAA34 \uAA06\u034F\uAA2A", null),
        ("\uAA00\uAA01\uAA02 \uAA20\uAA2F\uAA06\uAA40 \uAA51\uAA52 \uAA5C\uAA06\uAA5D A\uAA06. \uAA06\uAA40 \uAA06\uAA42", null),
        ("\uAA06\uAA34\uAA2F \uAA06\uAA2F \uAA06\uAA33\uAA2A \uAA06\uAA2A\uAA4C", "-pref"),
        ("\uAA06\uAA34\uAA2F \uAA06\uAA2F \uAA06\uAA33\uAA2A \uAA06\uAA2A\uAA4C", "-pres"),
        ("\uAA06\uAA34\uAA2F \uAA06\uAA2F \uAA06\uAA33\uAA2A \uAA06\uAA2A\uAA4C", "-ccmp,-rlig"),
        ("\uAA06\uAA34\uAA2F \uAA06\uAA2F \uAA06\uAA33\uAA2A \uAA06\uAA2A\uAA4C", "-abvm,-mark,-dist"),
        ("\uAA06\uAA34\uAA2F \uAA06\uAA2F \uAA06\uAA33\uAA2A \uAA06\uAA2A\uAA4C", "ss01"),
    ];

    // Newa: the vowel sign drawn before the cluster, conjuncts, the reph and the forms of ra after a consonant, the virama
    // with joiners, the nukta and the signs, the letters that take a stacked consonant, what is not a cluster, and clusters
    // without groups of features.
    private static readonly ShapingText[] NewaShapingCases =
    [
        ("\U0001140E\U00011436 \U0001140E\U00011435 \U0001140E\U00011437 \U0001140E\U00011438 \U0001140E\U0001143E \U0001140E\U0001143F \U0001140E\U00011440 \U0001140E\U00011441", null),
        ("\U0001140E\U00011442\U0001140E \U0001140E\U00011442\U0001141F \U00011428\U00011442\U0001140E \U00011428\U00011442\U0001140E\U00011436 \U00011428\U00011442\U0001140E\U00011442\U0001140E \U0001140E\U00011442\U00011428 \U0001140E\U00011442\U00011429\U00011436 \U0001140E\U00011442\U0001142B", null),
        ("\U0001140E\U00011442\u200D \U0001140E\U00011442\u200C\U0001140E \U0001140E\U00011442\u200D\U0001140E \U0001140E\U00011442 \U00011428\U00011442\u200D\U0001140E \U00011428\U00011442\u200C\U0001140E", null),
        ("\U0001140E\U00011446 \U0001140E\U00011438\U00011444 \U0001140E\U00011436\U00011443 \U0001140E\U00011445 \U0001140E\U0001145E \U0001140E\U00011447 \U0001140E\U00011446\U00011442\U0001140E", null),
        ("\U00011460\U0001140E \U00011461\U0001141E \U00011460\U0001140E\U00011436 \U00011400\U00011435 \U00011401\U00011444", null),
        ("\U00011436 \U00011442\U0001140E \U00011444 \U00011446 \u25CC\U00011436 \u00A0\U00011438 \U0001140E\U00011436\U00011436 \U0001140E\U00011444\U00011438", null),
        ("\U00011451\U00011452 \U0001144B\U0001140E\U0001144C A\U0001140E. \U0001140E\u034F\U00011438 \U0001140E\u200C\U00011436", null),

        // The bullet is a generic base in the specification of the model.
        ("\U00011460\u2022\U00011436", null),
        ("\U00011428\U00011442\U0001140E\U00011436 \U0001140E\U00011442\U0001140E \U0001140E\U00011442\U00011428 \U0001140E\U00011436 \U0001140E\U00011438\U00011444", "-rphf"),
        ("\U00011428\U00011442\U0001140E\U00011436 \U0001140E\U00011442\U0001140E \U0001140E\U00011442\U00011428 \U0001140E\U00011436 \U0001140E\U00011438\U00011444", "-pref"),
        ("\U00011428\U00011442\U0001140E\U00011436 \U0001140E\U00011442\U0001140E \U0001140E\U00011442\U00011428 \U0001140E\U00011436 \U0001140E\U00011438\U00011444", "-half"),
        ("\U00011428\U00011442\U0001140E\U00011436 \U0001140E\U00011442\U0001140E \U0001140E\U00011442\U00011428 \U0001140E\U00011436 \U0001140E\U00011438\U00011444", "-akhn"),
        ("\U00011428\U00011442\U0001140E\U00011436 \U0001140E\U00011442\U0001140E \U0001140E\U00011442\U00011428 \U0001140E\U00011436 \U0001140E\U00011438\U00011444", "-abvs,-blws"),
        ("\U00011428\U00011442\U0001140E\U00011436 \U0001140E\U00011442\U0001140E \U0001140E\U00011442\U00011428 \U0001140E\U00011436 \U0001140E\U00011438\U00011444", "-curs"),
        ("\U00011428\U00011442\U0001140E\U00011436 \U0001140E\U00011442\U0001140E \U0001140E\U00011442\U00011428 \U0001140E\U00011436 \U0001140E\U00011438\U00011444", "-ccmp,-calt"),
        ("\U00011428\U00011442\U0001140E\U00011436 \U0001140E\U00011442\U0001140E \U0001140E\U00011442\U00011428 \U0001140E\U00011436 \U0001140E\U00011438\U00011444", "-abvm,-mark,-dist,-kern"),
    ];

    // Kaithi: the vowel sign drawn before the cluster, conjuncts and half forms, the reph, the virama with joiners, the
    // nukta and the signs, the number signs before digits, what is not a cluster, and clusters without groups of features.
    private static readonly ShapingText[] KaithiShapingCases =
    [
        ("\U0001108D\U000110B1 \U0001108D\U000110B0 \U0001108D\U000110B2 \U0001108D\U000110B3 \U0001108D\U000110B5 \U0001108D\U000110B7 \U0001108D\U000110B8", null),
        ("\U0001108D\U000110B9\U0001108D \U0001108D\U000110B9\U000110A8 \U000110A9\U000110B9\U0001108D \U000110A9\U000110B9\U0001108D\U000110B1 \U000110A9\U000110B9\U0001108D\U000110B9\U0001108D \U0001108D\U000110B9\U000110A9 \U0001108D\U000110B9\U000110A9\U000110B1 \U000110A3\U000110B9\U000110A8", null),
        ("\U0001108D\U000110B9\u200D \U0001108D\U000110B9\u200C\U0001108D \U0001108D\U000110B9\u200D\U0001108D \U0001108D\U000110B9 \U000110A9\U000110B9\u200D\U0001108D", null),
        ("\U0001108D\U000110BA \U0001108D\U000110B3\U00011081 \U0001108D\U000110B1\U00011080 \U0001108D\U00011082 \U0001108D\U000110BA\U000110B9\U0001108D \U00011083\U000110B0 \U00011084\U00011081", null),
        ("\U000110BD\u0967\u0968 \U000110CD\u0967 \U000110BD \U000110BD\U0001108D \u0967\u0968", null),
        ("\U000110B1 \U000110B9\U0001108D \U00011081 \U000110BA \u25CC\U000110B1 \u00A0\U000110B3 \U0001108D\U000110B1\U000110B1 \U0001108D\U00011081\U000110B3", null),
        ("\U000110BB\U0001108D\U000110BC \U000110BE\U000110BF A\U0001108D. \U0001108D\u034F\U000110B3 \U0001108D\u200C\U000110B1", null),
        ("\U000110A9\U000110B9\U0001108D\U000110B1 \U0001108D\U000110B9\U0001108D \U0001108D\U000110B9\U000110A9 \U0001108D\U000110B1 \U0001108D\U000110B3\U00011081", "-rphf"),
        ("\U000110A9\U000110B9\U0001108D\U000110B1 \U0001108D\U000110B9\U0001108D \U0001108D\U000110B9\U000110A9 \U0001108D\U000110B1 \U0001108D\U000110B3\U00011081", "-half"),
        ("\U000110A9\U000110B9\U0001108D\U000110B1 \U0001108D\U000110B9\U0001108D \U0001108D\U000110B9\U000110A9 \U0001108D\U000110B1 \U0001108D\U000110B3\U00011081", "-akhn,-cjct"),
        ("\U000110A9\U000110B9\U0001108D\U000110B1 \U0001108D\U000110B9\U0001108D \U0001108D\U000110B9\U000110A9 \U0001108D\U000110B1 \U0001108D\U000110B3\U00011081", "-abvs,-psts"),
        ("\U000110A9\U000110B9\U0001108D\U000110B1 \U0001108D\U000110B9\U0001108D \U0001108D\U000110B9\U000110A9 \U0001108D\U000110B1 \U0001108D\U000110B3\U00011081", "-rclt"),
        ("\U000110A9\U000110B9\U0001108D\U000110B1 \U0001108D\U000110B9\U0001108D \U0001108D\U000110B9\U000110A9 \U0001108D\U000110B1 \U0001108D\U000110B3\U00011081", "-abvm,-blwm,-mark,-kern"),
    ];

    // Lepcha: the vowel signs drawn before the cluster, the subjoined letters, alone, with a joiner before them and in the
    // ligature a font makes of a letter and one of them, the final consonants above a letter and above such a ligature
    // when a vowel sign comes between them, the ran, the nukta, what is not a cluster, and clusters without groups of
    // features.
    private static readonly ShapingText[] LepchaShapingCases =
    [
        ("\u1C00\u1C27 \u1C00\u1C28 \u1C00\u1C29 \u1C00\u1C26 \u1C00\u1C2A \u1C00\u1C2B \u1C00\u1C2C", null),
        ("\u1C00\u1C24 \u1C00\u1C25 \u1C00\u1C24\u1C27 \u1C00\u1C25\u1C26 \u1C00\u1C25\u1C24 \u1C23\u1C24 \u1C00\u200D\u1C24 \u1C00\u200D\u1C25 \u1C00\u200C\u1C24", null),
        ("\u1C00\u1C2D \u1C00\u1C30 \u1C00\u1C26\u1C2D \u1C00\u1C2D\u1C30 \u1C0A\u1C26\u1C2D\u1C30 \u1C23\u1C24\u1C26\u1C2D\u1C30 \u1C23\u1C24\u1C2D\u1C30 \u1C23\u1C24\u1C26\u1C30", null),
        ("\u1C00\u1C2D\u1C36 \u1C00\u1C36 \u1C00\u1C37 \u1C00\u1C37\u1C26 \u1C00\u1C35 \u1C00\u1C34 \u1C00\u1C2A\u1C2C \u1C00\u1C2D\u1C35", null),
        ("\u1C27 \u1C24 \u1C2D \u1C36 \u25CC\u1C27 \u00A0\u1C2A \u1C00\u1C27\u1C27 \u1C00\u1C2D\u1C27", null),
        ("\u1C41\u1C42 \u1C3B\u1C00\u1C3C A\u1C00. \u1C00\u034F\u1C2A \u1C00\u200C\u1C27 \u1C4D\u1C4E\u1C4F", null),
        ("\u1C23\u1C24\u1C26\u1C2D\u1C30 \u1C00\u1C24\u1C27 \u1C00\u1C2D\u1C36 \u1C00\u1C28", "-ccmp"),
        ("\u1C23\u1C24\u1C26\u1C2D\u1C30 \u1C00\u1C24\u1C27 \u1C00\u1C2D\u1C36 \u1C00\u1C28", "-abvs,-psts"),
        ("\u1C23\u1C24\u1C26\u1C2D\u1C30 \u1C00\u1C24\u1C27 \u1C00\u1C2D\u1C36 \u1C00\u1C28", "-abvm,-blwm,-mark"),
        ("\u1C23\u1C24\u1C26\u1C2D\u1C30 \u1C00\u1C24\u1C27 \u1C00\u1C2D\u1C36 \u1C00\u1C28", "-dist,-kern"),
    ];

    // Duployan: letters that join, each one attached to the exit of the one before it, marks of a letter, the format
    // controls that make letters overlap or step and that the font draws, a joiner and a space between letters, a
    // long text (each letter becomes many glyphs and is tried against several thousand subtables), and letters without
    // groups of features.
    private static readonly ShapingText[] DuployanShapingCases =
    [
        ("\U0001BC02\U0001BC41\U0001BC06\U0001BC44 \U0001BC08\U0001BC46\U0001BC0B \U0001BC00\U0001BC5A\U0001BC05\U0001BC41", null),
        ("\U0001BC02\U0001BC41\U0001BC03 \U0001BC00\U0001BC01 \U0001BC01 \U0001BC02 \U0001BC02\U0001BC03\U0001BC06 \U0001BC08\U0001BC44\U0001BC0B", null),
        ("\U0001BC02\U0001BC9D \U0001BC02\U0001BC9E \U0001BC02\U0001BC9D\U0001BC03 \U0001BC9D \U0001BC9E\U0001BC02 \U0001BC03\U0001BC9E\U0001BC9D", null),
        ("\U0001BC02\U0001BCA0\U0001BC03 \U0001BC02\U0001BCA1\U0001BC03 \U0001BC02\U0001BCA2\U0001BC03 \U0001BC02\U0001BCA3\U0001BC03 \U0001BC68\U0001BCA0\U0001BC46\U0001BC3B \U0001BC9E\U0001BC40\U0001BC21\U0001BC02\U0001BC9D\U0001BC09\U0001BCA0\U0001BC37", null),
        ("\U0001BC02\u200C\U0001BC03 \U0001BC02\u200D\U0001BC03 \U0001BC02 \U0001BC03 \U0001BC02\u034F\U0001BC03 A\U0001BC02. \U0001BC02\U0001BC9F", null),
        ("\U0001BC02\U0001BC41\U0001BC03\U0001BC06\U0001BC44\U0001BC0B \U0001BC08\U0001BC46\U0001BC07\U0001BC5A\U0001BC05 \U0001BC0A\U0001BC41\U0001BC09\U0001BC04\U0001BC44 \U0001BC02\U0001BC41\U0001BC03\U0001BC06\U0001BC44\U0001BC0B \U0001BC08\U0001BC46\U0001BC07\U0001BC5A\U0001BC05 \U0001BC0A\U0001BC41\U0001BC09\U0001BC04\U0001BC44 \U0001BC02\U0001BC41\U0001BC03\U0001BC06\U0001BC44\U0001BC0B \U0001BC08\U0001BC46\U0001BC07\U0001BC5A\U0001BC05 \U0001BC0A\U0001BC41\U0001BC09\U0001BC04\U0001BC44 \U0001BC02\U0001BC41\U0001BC03\U0001BC06\U0001BC44\U0001BC0B \U0001BC08\U0001BC46\U0001BC07\U0001BC5A\U0001BC05 \U0001BC0A\U0001BC41\U0001BC09\U0001BC04\U0001BC44", null),
        ("\U0001BC02\U0001BC41\U0001BC03 \U0001BC68\U0001BCA0\U0001BC46\U0001BC3B \U0001BC02\U0001BC9D\U0001BC03 \U0001BC00\U0001BC01", "-curs"),
        ("\U0001BC02\U0001BC41\U0001BC03 \U0001BC68\U0001BCA0\U0001BC46\U0001BC3B \U0001BC02\U0001BC9D\U0001BC03 \U0001BC00\U0001BC01", "-rclt"),
        ("\U0001BC02\U0001BC41\U0001BC03 \U0001BC68\U0001BCA0\U0001BC46\U0001BC3B \U0001BC02\U0001BC9D\U0001BC03 \U0001BC00\U0001BC01", "-mark,-mkmk"),
        ("\U0001BC02\U0001BC41\U0001BC03 \U0001BC68\U0001BCA0\U0001BC46\U0001BC3B \U0001BC02\U0001BC9D\U0001BC03 \U0001BC00\U0001BC01", "-abvs,-blwm,-abvm"),
        ("\U0001BC02\U0001BC41\U0001BC03 \U0001BC68\U0001BCA0\U0001BC46\U0001BC3B \U0001BC02\U0001BC9D\U0001BC03 \U0001BC00\U0001BC01", "-dist"),
    ];

    // Hebrew: letters and final forms, the points in the order of a normalized text and in the order they are typed
    // in, a meteg between the parts of a vowel, cantillation marks, the precomposed letters (which a font that
    // positions the points draws as a letter and its points, and a font that does not draws as they are), the
    // ligatures of Yiddish, and punctuation.
    private static readonly ShapingText[] HebrewShapingCases =
    [
        ("\u05E9\u05DC\u05D5\u05DD \u05E2\u05D5\u05DC\u05DD \u05DA\u05DD\u05DF\u05E3\u05E5", null),
        ("\u05E9\u05C1\u05B8\u05DC\u05D5\u05B9\u05DD \u05D1\u05BC\u05B0\u05E8\u05B5\u05D0\u05E9\u05C1\u05B4\u05D9\u05EA", null),
        ("\u05D1\u05B0\u05BC\u05E8\u05B5\u05D0\u05E9\u05B4\u05C1\u05D9\u05EA \u05D1\u05B7\u05BC \u05D1\u05BC\u05B7", null),
        ("\u05E9\u05BC\u05C1 \u05E9\u05C1\u05BC \u05E9\u05BC\u05C2 \u05E9\u05C2\u05BC \u05E9\u05C1 \u05E9\u05C2", null),
        ("\u05D0\u05B7\u05B0\u05BD \u05D0\u05B8\u05B4\u05BD \u05D0\u05B7\u05BD \u05D0\u05B8\u05B0\u05A5 \u05DC\u05B8\u05BD\u05DE\u05B8\u05BC\u05D4", null),
        ("\u05D1\u05B0\u05BC\u05E8\u05B5\u05D0\u05E9\u05B4\u05C1\u0596\u05D9\u05EA \u05D1\u05B8\u05BC\u05E8\u05B8\u05A3\u05D0 \u05D0\u05B1\u05DC\u05B9\u05D4\u05B4\u0591\u05D9\u05DD", null),
        ("\uFB2A \uFB2B \uFB2C \uFB2D \uFB2E \uFB2F \uFB30 \uFB31 \uFB35 \uFB4A \uFB4B \uFB4C \uFB4D \uFB4E \uFB1D \uFB1F", null),
        ("\u05D9\u05B4 \u05F2\u05B7 \u05D0\u05B7 \u05D0\u05B8 \u05D5\u05B9 \u05D1\u05BF \u05DB\u05BF \u05E4\u05BF \u05D5\u05BC \u05EA\u05BC", null),
        ("\u05D0\u05DC \uFB4F \u05F0\u05F1\u05F2 \u05F3\u05F4 \u05BE \u05C0 \u05C3 \u20AA", null),
        ("\u05D0\u05DC \uFB4F", "dlig"),
        ("\u05D1\u05BC\u05B0\u05E8\u05B5\u05D0\u05E9\u05C1\u05B4\u05D9\u05EA \u05E9\u05C1\u05B8\u05DC\u05D5\u05B9\u05DD", "-ccmp"),
        ("\u05D1\u05BC\u05B0\u05E8\u05B5\u05D0\u05E9\u05C1\u05B4\u05D9\u05EA \u05E9\u05C1\u05B8\u05DC\u05D5\u05B9\u05DD", "-mark,-kern"),
        ("\u25CC\u05B7 \u05B7\u05D0 \u05D0\u200D\u05B7 \u05D0\u034F\u05B7\u05BC \u05D0\u200C\u05D1", null),
        ("\u05D4\u05B2\u05BD \u05D4\u05B2\u200D\u05BD \u05D4\u05B3\u05BD \u05D9\u05B0\u05E8\u05D5\u05BC\u05E9\u05C1\u05B8\u05DC\u05B7\u05B4\u05DD", null),
        ("\uFB2A\u05B7 \uFB2C\u05B8 \uFB4B\u05BC \uFB31\u05B0 \uFB2E\u05BD \u05D1\u05B7\u034F\u05BC", null),
    ];

    // Syriac: the forms of the letters, the forms of alaph (joined in the middle and at the end of a word; not joined
    // after dalath or rish, after another letter that does not join it, and at the start of a word), the marks, the
    // joiners, and the abbreviation mark, which stretches over the letters that follow it.
    private static readonly ShapingText[] SyriacShapingCases =
    [
        ("\u072B\u0720\u0721\u0710 \u0725\u0720\u0721\u0710", null),
        ("\u0712 \u0712\u0712 \u0712\u0712\u0712 \u0715\u0712\u0715 \u0717\u0718\u0719", null),
        ("\u0710 \u0710\u0712 \u0712\u0710 \u0712\u0710\u0712 \u0715\u0710 \u072A\u0710 \u0716\u0710 \u0717\u0710 \u0718\u0710 \u0710\u0710", null),
        ("\u0715\u0710\u0712 \u0717\u0710\u0712 \u0712\u0710\u0710 \u0715\u0710\u0710 \u0717\u0710\u0710\u0710 \u072F\u0710 \u0712\u0711\u0710", null),
        ("\u0712\u0730\u0710 \u0715\u0308\u0710 \u0717\u200D\u0710 \u0717\u200C\u0710 \u0712\u200C\u0710 \u0710\u200D \u0640\u0710 \u0710\u0640", null),
        ("\u0712\u0741\u0730 \u0712\u0730\u0741 \u072A\u0308 \u0715\u0742\u0733 \u0720\u0747\u0738 \u0712\u0323\u0307", null),
        ("\u0720\u0710 \u0720\u0710\u0720 \u0713\u0714\u071B\u071C \u072D\u072E\u072F \u074D\u074E\u074F", null),
        ("\u0712\u200D \u200D\u0712 \u0712\u200C\u0712 \u0712\u0640\u0712 \u0712\u200F\u0712", null),
        ("\u070F\u072B\u0718\u0712", null),
        ("\u070F\u072B", null),
        ("\u070F", null),
        ("\u0712 \u070F\u072B\u0718\u0712\u0718\u0712 \u0712", null),
        ("\u070F\u072B\u0718\u0712\u0730\u0718\u0712\u0718\u0712\u0718\u0712\u0718 \u070F\u0712\u0712.", null),
        ("\u070F\u0661\u0662\u0663 \u070F\u0712 \u070F \u0712", null),
        ("\u0700 \u0701 \u0702 \u0703 \u0709 \u070A \u070D (\u0712) [\u0712] \u0661\u0662\u0663 \u060C\u061B\u061F", null),
        ("\u072B\u0720\u0721\u0710 \u0720\u0710 \u0712\u0710\u0712", "-rlig"),
        ("\u072B\u0720\u0721\u0710 \u0712\u0710\u0712 \u0715\u0710 \u0717\u0710", "-fina,-med2"),
        ("\u072B\u0720\u0721\u0710 \u0712\u0710\u0712 \u0715\u0710 \u0717\u0710", "-fin2,-fin3"),
        ("\u070F\u072B\u0718\u0712", "-stch"),
        ("\u072B\u0720\u0721\u0710 \u0713\u0720", "ss01"),
        ("\u072B\u0720\u0721\u0710 \u0713\u0720", "ss02,salt"),
    ];

    // Emoji sequences (Unicode Technical Standard #51): what the font draws with one glyph, and what is left of a
    // sequence the font does not have.
    private static readonly ShapingText[] EmojiShapingCases =
    [
        // Characters alone, with the emoji and the text presentation selectors.
        ("\U0001F600 \U0001F642\U0001F680", null),
        ("\u2764 \u2764\uFE0F \u2764\uFE0E \u263A\uFE0F \u00A9\uFE0F \u2122", null),

        // A skin tone on a character that takes one, alone, and on a character that does not.
        ("\U0001F44D\U0001F3FB \U0001F44D\U0001F3FF \U0001F3FD \U0001F600\U0001F3FD \u261D\U0001F3FE", null),

        // Sequences joined by zero width joiners: families, a kiss with its heart, people with skin tones; a sequence
        // the font does not have, and a joiner that joins nothing.
        ("\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466 \U0001F469\u200D\U0001F467", null),
        ("\U0001F469\u200D\u2764\uFE0F\u200D\U0001F48B\u200D\U0001F468 \U0001F469\u200D\u2764\u200D\U0001F48B\u200D\U0001F468", null),
        ("\U0001F469\U0001F3FD\u200D\u2764\uFE0F\u200D\U0001F48B\u200D\U0001F468\U0001F3FB", null),
        ("\U0001F600\u200D\U0001F680 \U0001F468\u200D A\u200D\U0001F469", null),

        // Flags: pairs of regional indicators, a pair that is no flag, an odd one; a flag joined to a rainbow; a flag
        // with tags, and its tags without the cancel tag.
        ("\U0001F1EB\U0001F1F7\U0001F1FA\U0001F1F8 \U0001F1F7\U0001F1F7 \U0001F1EB\U0001F1F7\U0001F1FA", null),
        ("\U0001F3F3\uFE0F\u200D\U0001F308 \U0001F3F3\u200D\U0001F308 \U0001F3F3", null),
        ("\U0001F3F4\U000E0067\U000E0062\U000E0065\U000E006E\U000E0067\U000E007F \U0001F3F4\U000E0067\U000E0062 \U0001F3F4", null),

        // Keycaps, with and without the selector, and a digit alone.
        ("1\uFE0F\u20E3 #\uFE0F\u20E3 *\u20E3 12 \u20E3", null),

        // A vertical line
        new("\U0001F600 \U0001F642\U0001F680 \u2764\uFE0F", null, Direction: "ttb"),
    ];

    // A font without positioning table: where the reference shaper places a mark of each kind of combining class, on a
    // letter whose ink is not in the middle of its advance, on a narrow letter, on a letter with a descender and on a
    // space; marks that stack, marks of several classes, a mark the font draws low or high, a mark that has an advance,
    // a mark without letter, and marks around characters without visible form.
    private static readonly ShapingText[] MarksShapingCases =
    [
        ("A\u0300 A\u0301 A\u0315 A\u031B A\u0321 A\u0316 A\u0334 A\u0345", null),
        ("A\u035C A\u0361A A\u302A A\u3099 A\u093C A\u0C55 A\u0711", null),
        ("A\u0EB8 A\u0EC8 A\u0F71 A\u0F72 A\u0F74 A\u0E38 A\u0E3A A\u0E48", null),
        ("i\u0301 i\u0316 i\u0315 i\u302A p\u0301 p\u0316 p\u0321 \u0020\u0301 \u0020\u0316", null),
        ("A\u0301\u0301\u0301 A\u0316\u0316 A\u0301\u0316\u0301\u0316 A\u0321\u0316 A\u0315\u0301 A\u031B\u0301", null),
        ("A\u0302 A\u0317 A\u0323 A\u0308 A\u0302\u0301 A\u0317\u0316 A\u0323\u0316 A\u0308\u0301", null),
        ("\u0301A AA\u0301 A\u200D\u0301 A\u034F\u0301\u0316 A\u0301\u200C\u0301", null),

        // Hebrew and Arabic, from right to left: the points, the dagesh, the dots of shin and sin, accents; the vowel
        // marks and the shadda.
        ("\u05D0\u05B0 \u05D0\u05B9 \u05D0\u05BC \u05D0\u05BF \u05D0\u05C1 \u05D0\u05C2 \u05D0\u05AE \u05D0\u059A", null),
        ("\u05D0\u05B0\u05BC\u05C1 \u05D0\u05BC\u05B0 \u05D0\u05B9\u05AE \u05D0\u05D0\u05B0", null),
        ("\u0628\u064B \u0628\u064D \u0628\u0651 \u0628\u0670 \u0628\u0651\u064B \u0628\u064D\u0651 \u0628\u0628\u064B", null),

        // Thai: the marks stay where the font draws them.
        ("\u0E01\u0E38 \u0E01\u0E48 \u0E01\u0E3A \u0E01\u0E38\u0E48", null),
    ];

    // A font of Arabic without substitution table: the forms of the letters, the ligatures of lam and alef, with marks
    // on them, letters that do not join, joiners, and letters the font has no form for.
    private static readonly ShapingText[] ArabicFormsShapingCases =
    [
        ("\u0628 \u0628\u0628 \u0628\u0628\u0628 \u0627\u0628 \u0628\u0627 \u0621\u0628 \u0628\u0621", null),
        ("\u0644\u0627 \u0628\u0644\u0627 \u0644\u0622 \u0644\u0623 \u0644\u0625 \u0628\u0644\u0622 \u0644\u0627\u0644\u0627", null),
        ("\u0644\u064E\u0627 \u0644\u0627\u064E \u0644\u064E\u0627\u064B \u0628\u0644\u0651\u064E\u0622\u064C \u0644\u200D\u0627 \u0644\u200C\u0627", null),
        ("\u0628\u064E\u0628\u0650 \u0628\u0651\u064E \u0627\u064B \u0628\u0640\u0628 \u0640\u0628 \u0628\u200D \u200D\u0628 \u0628\u200C\u0628", null),
        ("\u067E\u067E\u067E \u0686\u0698\u06AF \u06CC\u06CC\u06CC \u0649\u0649\u0649 \u06A9\u06C1\u06D2 \u0671\u0679\u0679", null),
        ("\u0627\u0644\u0644\u0647 \u0626\u062C \u0645\u062D\u0645\u062F \u0633\u0644\u0627\u0645 \u0639\u0631\u0628\u064A", null),
        ("\uFE8F\u0628 \u0628\uFEFB A\u0628B 12\u0628", null),
    ];

    // A font of Thai without substitution table, with the forms of its marks in the private use area: each kind of
    // consonant (usual, with an ascender, with a descender that goes or stays) with a vowel above, a vowel below, a
    // tone mark, and both; the sara am; several marks at one place; marks on what is no consonant.
    private static readonly ShapingText[] ThaiFormsShapingCases =
    [
        ("\u0E01\u0E48 \u0E01\u0E34 \u0E01\u0E34\u0E48 \u0E01\u0E38 \u0E01\u0E38\u0E48 \u0E01\u0E4C \u0E01\u0E31\u0E49", null),
        ("\u0E1B\u0E48 \u0E1B\u0E34 \u0E1B\u0E34\u0E48 \u0E1B\u0E38 \u0E1B\u0E38\u0E48 \u0E1D\u0E31\u0E49 \u0E1F\u0E4D \u0E1F\u0E47", null),
        ("\u0E0D\u0E38 \u0E0D\u0E39 \u0E10\u0E3A \u0E0D\u0E48 \u0E0D\u0E34 \u0E10\u0E38\u0E48 \u0E0D \u0E10", null),
        ("\u0E0E\u0E38 \u0E0F\u0E39 \u0E0E\u0E3A \u0E0E\u0E48 \u0E0F\u0E34\u0E49 \u0E0E\u0E38\u0E48", null),
        ("\u0E01\u0E33 \u0E01\u0E48\u0E33 \u0E1B\u0E33 \u0E1B\u0E49\u0E33 \u0E19\u0E49\u0E33", null),
        ("\u0E01\u0E48\u0E49 \u0E1B\u0E48\u0E49\u0E4A \u0E1B\u0E34\u0E47\u0E48 \u0E01\u0E38\u0E39 \u0E0D\u0E38\u0E3A \u0E01\u0E3A\u0E38", null),
        ("\u0E32\u0E38 \u0E32\u0E48 \u0020\u0E38 \u0020\u0E48 \u0E51\u0E39 A\u0E48 \u0E2C\u0E48 \u0E2C\u0E38", null),
        ("\u0E20\u0E32\u0E29\u0E32\u0E44\u0E17\u0E22 \u0E1B\u0E39\u0E48 \u0E1F\u0E49\u0E32 \u0E0D\u0E35\u0E48\u0E1B\u0E38\u0E48\u0E19 \u0E01\u0E0F\u0E38", null),
    ];

    private static readonly Dictionary<string, ShapingText[]> ShapingCases = new(StringComparer.Ordinal)
    {
        ["fonts/synthetic-thai-forms"] = ThaiFormsShapingCases,
        ["fonts/synthetic-thai-forms-mac"] = ThaiFormsShapingCases,
        ["fonts/synthetic-marks"] = MarksShapingCases,
        ["fonts/synthetic-arabic-forms"] = ArabicFormsShapingCases,
        ["fonts/noto-color-emoji"] = EmojiShapingCases,
        ["fonts/noto-naskh-arabic"] = ArabicShapingCases,
        ["fonts/noto-nastaliq-urdu"] = UrduShapingCases,
        ["fonts/noto-sans-devanagari"] = DevanagariShapingCases,
        ["fonts/noto-sans-bengali"] = BengaliShapingCases,
        ["fonts/noto-sans-gurmukhi"] = GurmukhiShapingCases,
        ["fonts/noto-sans-gujarati"] = GujaratiShapingCases,
        ["fonts/noto-sans-oriya"] = OriyaShapingCases,
        ["fonts/noto-sans-tamil"] = TamilShapingCases,
        ["fonts/noto-sans-telugu"] = TeluguShapingCases,
        ["fonts/noto-sans-kannada"] = KannadaShapingCases,
        ["fonts/noto-sans-malayalam"] = MalayalamShapingCases,
        ["fonts/noto-sans-devanagari-first-version"] = DevanagariShapingCases,
        ["fonts/noto-sans-malayalam-first-version"] = MalayalamShapingCases,
        ["fonts/noto-sans-khmer"] = KhmerShapingCases,
        ["fonts/noto-sans-myanmar"] = MyanmarShapingCases,
        ["fonts/noto-serif-tibetan"] = TibetanShapingCases,
        ["fonts/noto-sans-sinhala"] = SinhalaShapingCases,
        ["fonts/noto-sans-javanese"] = JavaneseShapingCases,
        ["fonts/noto-sans-balinese"] = BalineseShapingCases,
        ["fonts/noto-sans-tai-tham"] = TaiThamShapingCases,
        ["fonts/noto-sans-nko"] = NKoShapingCases,
        ["fonts/noto-sans-mongolian"] = MongolianShapingCases,
        ["fonts/noto-sans-adlam"] = AdlamShapingCases,
        ["fonts/noto-sans-cham"] = ChamShapingCases,
        ["fonts/noto-sans-newa"] = NewaShapingCases,
        ["fonts/noto-sans-kaithi"] = KaithiShapingCases,
        ["fonts/noto-sans-lepcha"] = LepchaShapingCases,
        ["fonts/noto-sans-duployan"] = DuployanShapingCases,
        ["fonts/noto-sans-thai"] = ThaiShapingCases,
        ["fonts/noto-sans-lao"] = LaoShapingCases,
        ["fonts/noto-sans-cjk-kr-regular"] = HangulShapingCases,
        ["fonts/noto-sans-cjk-kr-letters"] = HangulShapingCases,
        ["fonts/noto-sans-hebrew"] = HebrewShapingCases,
        ["fonts/noto-sans-hebrew-without-positioning"] = HebrewShapingCases,
        ["fonts/noto-sans-syriac"] = SyriacShapingCases,
        ["fonts/noto-sans-variable"] = VariableLatinShapingCases,
        ["fonts/noto-sans-cjk-jp-variable"] = VariableCjkShapingCases,
        ["fonts/synthetic-variable"] = VariableBoxesShapingCases,
        ["fonts/synthetic-variable-phantom"] = VariableBoxesShapingCases,
        ["fonts/synthetic-variable-axes"] = VariableBoxesShapingCases,
        ["fonts/noto-sans-regular"] = LatinShapingCases,
        ["fonts/noto-sans-regular-cff"] = LatinShapingCases,
        ["fonts/noto-sans-cjk-jp-regular"] = CjkShapingCases,
        ["fonts/synthetic-boxes"] = BoxesShapingCases,
        ["fonts/synthetic-languages"] = CreateLanguageShapingCases(),
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
        CorpusDriver.PreserveHevcVectors(outDir, manifest);
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
                entries.Add(CreateEntry(outDir, work, tools, font.Id, font.Path, data, provenance, font.Flavor, font.FaceCount, "none", reservedFontName: null, original: null, withVectors: font.HasOutlineVectors));
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
                    List<string> arguments = font.KeepHinting
                        ? ["--unicodes=" + font.Unicodes, "--layout-features=*", "--notdef-outline"]
                        : ["--unicodes=" + font.Unicodes, "--layout-features=*", "--no-hinting", "--notdef-outline"];
                    if (font.DropTables is not null)
                    {
                        arguments.Add("--drop-tables+=" + font.DropTables);
                    }

                    if (font.LayoutScripts is not null)
                    {
                        arguments.Add("--layout-scripts=" + font.LayoutScripts);
                    }

                    Proc.Run([tools.HbSubset, .. arguments, "--output-file=" + output, input]);
                    data = File.ReadAllBytes(output);
                    modification = "subset";
                    provenance["tools"] = new List<string> { tools.HarfBuzzLabel("hb-subset") };
                    provenance["commands"] = new List<string> { $"hb-subset {string.Join(' ', arguments)} --output-file={Path.GetFileName(font.Path)} {font.FileName}" };
                }

                files.Add(font.Id, (font.Path, data, provenance, 1));
                entries.Add(CreateEntry(outDir, work, tools, font.Id, font.Path, data, provenance, GetFlavor(data), 1, modification, font.ReservedFontName, original: null, withVectors: font.HasOutlineVectors));
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

            manifest[CorpusDriver.FontSuitesKey] = new List<object?> { CopyAots(outDir, fontsDirectory / "aots"), CopyHintingSuite(outDir, fontsDirectory / "truetype-hinting") };
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
    /// Copies the TrueType hinting suite, which truetype_hinting.py writes (fonts hinted by hand and the points FreeType
    /// hints them to), and describes it.
    /// </summary>
    private static Obj CopyHintingSuite(FullPath outDir, FullPath source)
    {
        const string Directory = "font-suites/truetype-hinting";
        if (!System.IO.Directory.Exists(source / "expected"))
            throw new FatalException($"The directory {source} is not the output of truetype_hinting.py.");

        var target = outDir / Directory;
        foreach (var file in System.IO.Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = target / FullPath.FromPath(file).MakePathRelativeTo(source);
            destination.CreateParentDirectory();
            File.Copy(file, destination);
        }

        var hash = ComputeDirectoryHash(target, out var fileCount);
        if (hash != HintingSuiteSha256)
            throw new FatalException($"The TrueType hinting suite has the hash {hash} ({fileCount} files) but {HintingSuiteSha256} is pinned.");

        return new Obj
        {
            ["id"] = Directory,
            ["directory"] = Directory,
            ["provenance"] = new Obj
            {
                ["origin"] = "external",
                ["license"] = "Apache-2.0",
                ["author"] = "Google Inc. and Christian Robertson (Droid Sans, Open Sans, Roboto), subset by hb-subset; expectations from FreeType 2.13.2",
                ["source"] = "https://github.com/google/fonts (apache/droidsans, apache/opensans and apache/roboto at the commits of font-suites/truetype-hinting/README.md)",
            },
            ["fileCount"] = fileCount,
            ["sha256"] = hash,
            ["description"] = "Three fonts hinted by hand with TrueType instructions, subset to Basic Latin, and the advance and the hinted points FreeType gives for each glyph at ten sizes with its version 40 interpreter for black and white pixels.",
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

        if (Renders.TryGetValue(id, out var renderSets))
        {
            entry["features"] = GetFeatures(data);
            entry["renderTool"] = tools.HarfBuzzLabel("hb-raster") + " --logical --background=FFFFFF";
            entry["renders"] = RecordRenders(outDir, work, tools, id, data, renderSets);
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

        var info = ParseKeyValues(tools.RunText([tools.HbInfo, .. faceArguments, file]));
        var glyphCount = int.Parse(info["Glyph count"], CultureInfo.InvariantCulture);
        var metrics = RecordMetrics(tools, file, faceArguments);

        // The character map without shaping: code point to glyph name, then glyph name to glyph identifier.
        var glyphIds = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in tools.RunText([tools.HbInfo, .. faceArguments, "--list-glyphs", file]).Split('\n'))
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
        foreach (var line in tools.RunText([tools.HbInfo, .. faceArguments, "--list-unicodes", file]).Split('\n'))
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
            var withOt = GetOutline(tools.RunText([tools.HbVector, .. faceArguments, "--font-funcs=ot", "--glyphs", file, text]));
            var withFt = GetOutline(tools.RunText([tools.HbVector, .. faceArguments, "--font-funcs=ft", "--glyphs", file, text]));
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
                    var withOt = GetOutline(tools.RunText([tools.HbVector, .. arguments, "--font-funcs=ot", "--precision=3", "--glyphs", file, text]));
                    var withFt = GetOutline(tools.RunText([tools.HbVector, .. arguments, "--font-funcs=ft", "--precision=3", "--glyphs", file, text]));
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

    /// <summary>
    /// Draws lines of text with the reference renderer: the box of each line with a margin, on a white background, so
    /// that the origin of the first glyph is at (margin, margin + ascent).
    /// </summary>
    private static List<object?> RecordRenders(FullPath outDir, FullPath work, Tools tools, string id, byte[] data, RenderSet[] sets)
    {
        const int Margin = 32;
        var file = work / "render.font";
        File.WriteAllBytes(file, data);

        // The characters of the font, without the space.
        var excluded = CharactersWithoutReference.GetValueOrDefault(id) ?? [];
        var characters = new List<string>();
        foreach (var line in tools.RunText([tools.HbInfo, "--list-unicodes", file]).Split('\n'))
        {
            var parts = line.Split('\t');
            if (parts.Length == 2 && parts[0].StartsWith("U+", StringComparison.Ordinal) && parts[0] != "U+0020" && !excluded.Contains(parts[0][2..]))
            {
                characters.Add(parts[0][2..]);
            }
        }

        var renders = new List<object?>();
        for (var setIndex = 0; setIndex < sets.Length; setIndex++)
        {
            var set = sets[setIndex];
            var texts = set.Texts ?? [.. characters.Where(character => set.Exclude?.Contains(character, StringComparer.Ordinal) != true).Chunk(set.PerLine).Select(chunk => string.Join(' ', chunk))];
            for (var lineIndex = 0; lineIndex < texts.Length; lineIndex++)
            {
                var path = string.Create(CultureInfo.InvariantCulture, $"fonts/renders/{id["fonts/".Length..]}/{setIndex:00}-{lineIndex:000}.png");
                var output = work / "render.png";
                List<string> arguments =
                [
                    "--logical",
                    "--margin=" + Margin.ToString(CultureInfo.InvariantCulture),
                    "--font-size=" + set.Size.ToString(CultureInfo.InvariantCulture),

                    // The strike of a bitmap font is chosen by the number of pixels per em; without it, the largest one.
                    "--font-ppem=" + set.Size.ToString(CultureInfo.InvariantCulture),
                    string.Create(CultureInfo.InvariantCulture, $"--font-extents={set.Ascent},{set.Descent},0"),
                    "--background=FFFFFF",
                    "--output-format=png",
                ];
                if (set.Foreground is not null)
                {
                    arguments.Add("--foreground=" + set.Foreground);
                }

                if (set.Palette != 0)
                {
                    arguments.Add("--font-palette=" + set.Palette.ToString(CultureInfo.InvariantCulture));
                }

                if (set.Variations is not null)
                {
                    arguments.Add("--variations=" + set.Variations);
                }

                var image = tools.RunToFile([tools.HbRaster, .. arguments, file, "--unicodes=" + string.Join(',', texts[lineIndex].Split(' '))], output);
                var target = outDir / path;
                target.CreateParentDirectory();
                File.WriteAllBytes(target, image);

                var render = new Obj
                {
                    ["file"] = new Obj { ["path"] = path, ["sha256"] = Bytes.Sha256Hex(image), ["role"] = "font-render" },
                    ["text"] = texts[lineIndex],
                    ["size"] = set.Size,
                    ["margin"] = Margin,
                    ["ascent"] = set.Ascent,
                    ["descent"] = set.Descent,
                };
                if (set.Variations is not null)
                {
                    render["variations"] = set.Variations;
                }

                if (set.Palette != 0)
                {
                    render["palette"] = set.Palette;
                }

                if (set.Foreground is not null)
                {
                    render["foreground"] = set.Foreground;
                }

                if (set.ImageShift != 0)
                {
                    render["imageShift"] = set.ImageShift;
                }

                renders.Add(render);
            }
        }

        return renders;
    }

    private static Obj RecordMetrics(Tools tools, FullPath file, string[] arguments)
    {
        var metrics = new Obj();
        foreach (var line in tools.RunText([tools.HbInfo, .. arguments, "--list-metrics", file]).Split('\n'))
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
            var withOt = tools.RunText([tools.HbShape, .. arguments, "--font-funcs=ot", "--glyphs", "--output-format=json", "--no-glyph-names", file, glyphs]);
            var withFt = tools.RunText([tools.HbShape, .. arguments, "--font-funcs=ft", "--glyphs", "--output-format=json", "--no-glyph-names", file, glyphs]);
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
        foreach (var (text, features, variations, language, direction) in cases)
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

            if (language is not null)
            {
                arguments.Add("--language=" + language);
            }

            if (direction is not null)
            {
                arguments.Add("--direction=" + direction);
            }

            arguments.Add("--unicodes=" + string.Join(',', codePoints.Select(codePoint => "U+" + codePoint)));
            var output = tools.RunText([tools.HbShape, .. arguments, fontFile]);
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

            if (language is not null)
            {
                result["language"] = language;
            }

            if (direction is not null)
            {
                result["direction"] = direction;
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
            var glyphCount = int.Parse(ParseKeyValues(tools.RunText([tools.HbInfo, .. faceArguments, originalFile]))["Glyph count"], CultureInfo.InvariantCulture);
            var glyphs = "[" + string.Join('|', Enumerable.Range(0, Math.Min(256, glyphCount)).Select(id => id.ToString(CultureInfo.InvariantCulture))) + "]";
            var expectedAdvances = tools.RunText([tools.HbShape, .. faceArguments, "--font-funcs=ft", "--glyphs", "--output-format=json", "--no-glyph-names", originalFile, glyphs]);
            var actualAdvances = tools.RunText([tools.HbShape, .. faceArguments, "--face-loader=ft", "--font-funcs=ft", "--glyphs", "--output-format=json", "--no-glyph-names", containerFile, glyphs]);
            if (expectedAdvances != actualAdvances)
                throw new FatalException($"The {flavor} form does not give the advances of the original font.");

            for (var glyph = 0; glyph < glyphCount; glyph++)
            {
                var text = glyph.ToString(CultureInfo.InvariantCulture);
                var expected = GetOutline(tools.RunText([tools.HbVector, .. faceArguments, "--font-funcs=ft", "--glyphs", originalFile, text]));
                var actual = GetOutline(tools.RunText([tools.HbVector, .. faceArguments, "--face-loader=ft", "--font-funcs=ft", "--glyphs", containerFile, text]));
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

    /// <summary>
    /// A text to shape, with the features, the instance of a variable font and the language it is shaped with, and the
    /// direction of its line when it is not the one of its script: <c>ttb</c> for a vertical line of upright glyphs.
    /// </summary>
    private sealed record ShapingText(string Text, string? Features, string? Variations = null, string? Language = null, string? Direction = null)
    {
        public static implicit operator ShapingText((string Text, string? Features) value) => new(value.Text, value.Features);
    }

    /// <summary>Lines of text the reference renderer draws with a font.</summary>
    private sealed record RenderSet(int Size, int Ascent, int Descent, int PerLine, string[]? Texts = null, string? Variations = null, int Palette = 0, string? Foreground = null, string[]? Exclude = null, int ImageShift = 0);

    private sealed record Container(string Original, string Flavor, string? Variant = null, Func<byte[]>? Build = null, string? Description = null);

    private sealed record SyntheticFont(string Id, string Path, Func<byte[]> Build, string Function, string Flavor, int FaceCount, bool HasOutlineVectors = true);

    private sealed record ExternalFont(string Id, string Path, string FileName, string Sha256, string Source, string Author, string License, string? ReservedFontName, string? Unicodes, bool KeepHinting = false, bool HasOutlineVectors = true, string? DropTables = null, string? LayoutScripts = null);

    private sealed class Tools
    {
        private readonly FullPath? _cache;

        public Tools()
        {
            if (Environment.GetEnvironmentVariable("MEZIANTOU_FRAMEWORK_IMAGING_CORPUS_CACHE") is { Length: > 0 } cache)
            {
                _cache = FullPath.FromPath(cache);
                Directory.CreateDirectory(_cache.Value);
            }

            HbShape = Download(PrebuiltTools.HbShape);
            HbInfo = Download(PrebuiltTools.HbInfo);
            HbSubset = Download(PrebuiltTools.HbSubset);
            HbVector = Download(PrebuiltTools.HbVector);
            HbRaster = Download(PrebuiltTools.HbRaster);
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

        public string HbRaster { get; }

        public string Woff2Compress { get; }

        public string Woff2Decompress { get; }

        public string HarfBuzzVersion { get; }

        public string Woff2Version { get; }

        public string Woff2Label => "woff2 " + Woff2Version;

        public string HarfBuzzLabel(string tool) => tool + " " + HarfBuzzVersion;

        /// <summary>
        /// Runs a tool that reads files and writes text. With a cache directory (the environment variable
        /// MEZIANTOU_FRAMEWORK_IMAGING_CORPUS_CACHE), the output of a command that was already run on files of the same
        /// content is read back: the thousands of runs of an unchanged font take no time. The cache is a convenience
        /// of the person who regenerates the corpus; the pinned tools are deterministic, and a run without the cache
        /// writes the same corpus.
        /// </summary>
        public string RunText(IReadOnlyList<string> command) => Encoding.UTF8.GetString(RunCached(command, output: null));

        /// <summary>Runs a tool that writes a file, given by a last "--output-file" argument that this method adds, and returns the content of the file.</summary>
        public byte[] RunToFile(IReadOnlyList<string> command, FullPath output) => RunCached(command, output);

        private byte[] RunCached(IReadOnlyList<string> command, FullPath? output)
        {
            string? entry = null;
            if (_cache is { } cache)
            {
                // The key: the tool (its path has its version), the arguments, and the content of the files they name.
                var key = new StringBuilder();
                foreach (var argument in command)
                {
                    key.Append(argument != command[0] && File.Exists(argument) ? GetContentHash(argument) : argument).Append('\n');
                }

                key.Append(output is null ? "text" : "file");
                entry = cache / Bytes.Sha256Hex(Encoding.UTF8.GetBytes(key.ToString()));
                if (File.Exists(entry))
                    return File.ReadAllBytes(entry);
            }

            byte[] result;
            if (output is { } file)
            {
                Proc.Run([.. command, "--output-file=" + file]);
                result = File.ReadAllBytes(file);
            }
            else
            {
                result = Proc.Run(command);
            }

            if (entry is not null)
            {
                File.WriteAllBytes(entry + ".tmp", result);
                File.Move(entry + ".tmp", entry, overwrite: true);
            }

            return result;
        }

        private static string GetContentHash(string path) => Bytes.Sha256Hex(File.ReadAllBytes(path));

        private static string Download(PrebuiltTool tool) => tool.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
}
