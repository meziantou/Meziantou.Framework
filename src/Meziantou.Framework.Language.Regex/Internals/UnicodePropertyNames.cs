using System.Collections.Frozen;

namespace Meziantou.Framework.Language.Regex.Internals;

/// <summary>The Unicode property names the JavaScript and PCRE engines accept inside <c>\p{…}</c>.</summary>
/// <remarks>
/// <para>
/// A name the engine does not know is a syntax error, so the set has to be known to report the same thing. The lists
/// were taken from the Unicode 17 <c>PropertyAliases.txt</c> and <c>PropertyValueAliases.txt</c> files and kept only
/// where V8 (for JavaScript) and PCRE2 10.47 (for PCRE) accept the name. An engine built against an older Unicode
/// version knows fewer script names.
/// </para>
/// <para>
/// JavaScript compares names exactly. PCRE compares them loosely: case, spaces, hyphens, and underscores are ignored,
/// which is why its lists hold the names already in that normalized form.
/// </para>
/// </remarks>
internal static class UnicodePropertyNames
{
    /// <summary>The General_Category values, which JavaScript accepts both alone and after <c>gc=</c>.</summary>
    public static FrozenSet<string> JavaScriptGeneralCategoryValues { get; } = FrozenSet.Create(StringComparer.Ordinal,
    [
        "C", "Cased_Letter", "Cc", "Cf", "Close_Punctuation", "Cn", "Co", "Combining_Mark", "Connector_Punctuation",
        "Control", "Cs", "Currency_Symbol", "Dash_Punctuation", "Decimal_Number", "Enclosing_Mark", "Final_Punctuation",
        "Format", "Initial_Punctuation", "L", "LC", "Letter", "Letter_Number", "Line_Separator", "Ll", "Lm", "Lo",
        "Lowercase_Letter", "Lt", "Lu", "M", "Mark", "Math_Symbol", "Mc", "Me", "Mn", "Modifier_Letter",
        "Modifier_Symbol", "N", "Nd", "Nl", "No", "Nonspacing_Mark", "Number", "Open_Punctuation", "Other",
        "Other_Letter", "Other_Number", "Other_Punctuation", "Other_Symbol", "P", "Paragraph_Separator", "Pc", "Pd",
        "Pe", "Pf", "Pi", "Po", "Private_Use", "Ps", "Punctuation", "S", "Sc", "Separator", "Sk", "Sm", "So",
        "Space_Separator", "Spacing_Mark", "Surrogate", "Symbol", "Titlecase_Letter", "Unassigned", "Uppercase_Letter",
        "Z", "Zl", "Zp", "Zs", "cntrl", "digit", "punct"
    ]);

    /// <summary>The Script values, which JavaScript accepts after <c>sc=</c> and <c>scx=</c>.</summary>
    public static FrozenSet<string> JavaScriptScriptValues { get; } = FrozenSet.Create(StringComparer.Ordinal,
    [
        "Adlam", "Adlm", "Aghb", "Ahom", "Anatolian_Hieroglyphs", "Arab", "Arabic", "Armenian", "Armi", "Armn",
        "Avestan", "Avst", "Bali", "Balinese", "Bamu", "Bamum", "Bass", "Bassa_Vah", "Batak", "Batk", "Beng", "Bengali",
        "Berf", "Beria_Erfe", "Bhaiksuki", "Bhks", "Bopo", "Bopomofo", "Brah", "Brahmi", "Brai", "Braille", "Bugi",
        "Buginese", "Buhd", "Buhid", "Cakm", "Canadian_Aboriginal", "Cans", "Cari", "Carian", "Caucasian_Albanian",
        "Chakma", "Cham", "Cher", "Cherokee", "Chorasmian", "Chrs", "Common", "Copt", "Coptic", "Cpmn", "Cprt",
        "Cuneiform", "Cypriot", "Cypro_Minoan", "Cyrillic", "Cyrl", "Deseret", "Deva", "Devanagari", "Diak",
        "Dives_Akuru", "Dogr", "Dogra", "Dsrt", "Dupl", "Duployan", "Egyp", "Egyptian_Hieroglyphs", "Elba", "Elbasan",
        "Elym", "Elymaic", "Ethi", "Ethiopic", "Gara", "Garay", "Geor", "Georgian", "Glag", "Glagolitic", "Gong",
        "Gonm", "Goth", "Gothic", "Gran", "Grantha", "Greek", "Grek", "Gujarati", "Gujr", "Gukh", "Gunjala_Gondi",
        "Gurmukhi", "Guru", "Gurung_Khema", "Han", "Hang", "Hangul", "Hani", "Hanifi_Rohingya", "Hano", "Hanunoo",
        "Hatr", "Hatran", "Hebr", "Hebrew", "Hira", "Hiragana", "Hluw", "Hmng", "Hmnp", "Hung", "Imperial_Aramaic",
        "Inherited", "Inscriptional_Pahlavi", "Inscriptional_Parthian", "Ital", "Java", "Javanese", "Kaithi", "Kali",
        "Kana", "Kannada", "Katakana", "Kawi", "Kayah_Li", "Khar", "Kharoshthi", "Khitan_Small_Script", "Khmer", "Khmr",
        "Khoj", "Khojki", "Khudawadi", "Kirat_Rai", "Kits", "Knda", "Krai", "Kthi", "Lana", "Lao", "Laoo", "Latin",
        "Latn", "Lepc", "Lepcha", "Limb", "Limbu", "Lina", "Linb", "Linear_A", "Linear_B", "Lisu", "Lyci", "Lycian",
        "Lydi", "Lydian", "Mahajani", "Mahj", "Maka", "Makasar", "Malayalam", "Mand", "Mandaic", "Mani", "Manichaean",
        "Marc", "Marchen", "Masaram_Gondi", "Medefaidrin", "Medf", "Meetei_Mayek", "Mend", "Mende_Kikakui", "Merc",
        "Mero", "Meroitic_Cursive", "Meroitic_Hieroglyphs", "Miao", "Mlym", "Modi", "Mong", "Mongolian", "Mro", "Mroo",
        "Mtei", "Mult", "Multani", "Myanmar", "Mymr", "Nabataean", "Nag_Mundari", "Nagm", "Nand", "Nandinagari", "Narb",
        "Nbat", "New_Tai_Lue", "Newa", "Nko", "Nkoo", "Nshu", "Nushu", "Nyiakeng_Puachue_Hmong", "Ogam", "Ogham",
        "Ol_Chiki", "Ol_Onal", "Olck", "Old_Hungarian", "Old_Italic", "Old_North_Arabian", "Old_Permic", "Old_Persian",
        "Old_Sogdian", "Old_South_Arabian", "Old_Turkic", "Old_Uyghur", "Onao", "Oriya", "Orkh", "Orya", "Osage",
        "Osge", "Osma", "Osmanya", "Ougr", "Pahawh_Hmong", "Palm", "Palmyrene", "Pau_Cin_Hau", "Pauc", "Perm", "Phag",
        "Phags_Pa", "Phli", "Phlp", "Phnx", "Phoenician", "Plrd", "Prti", "Psalter_Pahlavi", "Qaac", "Qaai", "Rejang",
        "Rjng", "Rohg", "Runic", "Runr", "Samaritan", "Samr", "Sarb", "Saur", "Saurashtra", "Sgnw", "Sharada",
        "Shavian", "Shaw", "Shrd", "Sidd", "Siddham", "Sidetic", "Sidt", "SignWriting", "Sind", "Sinh", "Sinhala",
        "Sogd", "Sogdian", "Sogo", "Sora", "Sora_Sompeng", "Soyo", "Soyombo", "Sund", "Sundanese", "Sunu", "Sunuwar",
        "Sylo", "Syloti_Nagri", "Syrc", "Syriac", "Tagalog", "Tagb", "Tagbanwa", "Tai_Le", "Tai_Tham", "Tai_Viet",
        "Tai_Yo", "Takr", "Takri", "Tale", "Talu", "Tamil", "Taml", "Tang", "Tangsa", "Tangut", "Tavt", "Tayo", "Telu",
        "Telugu", "Tfng", "Tglg", "Thaa", "Thaana", "Thai", "Tibetan", "Tibt", "Tifinagh", "Tirh", "Tirhuta", "Tnsa",
        "Todhri", "Todr", "Tolong_Siki", "Tols", "Toto", "Tulu_Tigalari", "Tutg", "Ugar", "Ugaritic", "Unknown", "Vai",
        "Vaii", "Vith", "Vithkuqi", "Wancho", "Wara", "Warang_Citi", "Wcho", "Xpeo", "Xsux", "Yezi", "Yezidi", "Yi",
        "Yiii", "Zanabazar_Square", "Zanb", "Zinh", "Zyyy", "Zzzz"
    ]);

    /// <summary>The binary properties JavaScript accepts alone.</summary>
    public static FrozenSet<string> JavaScriptBinaryProperties { get; } = FrozenSet.Create(StringComparer.Ordinal,
    [
        "AHex", "ASCII", "ASCII_Hex_Digit", "Alpha", "Alphabetic", "Any", "Assigned", "Bidi_C", "Bidi_Control",
        "Bidi_M", "Bidi_Mirrored", "CI", "CWCF", "CWCM", "CWKCF", "CWL", "CWT", "CWU", "Case_Ignorable", "Cased",
        "Changes_When_Casefolded", "Changes_When_Casemapped", "Changes_When_Lowercased", "Changes_When_NFKC_Casefolded",
        "Changes_When_Titlecased", "Changes_When_Uppercased", "DI", "Dash", "Default_Ignorable_Code_Point", "Dep",
        "Deprecated", "Dia", "Diacritic", "EBase", "EComp", "EMod", "EPres", "Emoji", "Emoji_Component",
        "Emoji_Modifier", "Emoji_Modifier_Base", "Emoji_Presentation", "Ext", "ExtPict", "Extended_Pictographic",
        "Extender", "Gr_Base", "Gr_Ext", "Grapheme_Base", "Grapheme_Extend", "Hex", "Hex_Digit", "IDC", "IDS", "IDSB",
        "IDST", "IDS_Binary_Operator", "IDS_Trinary_Operator", "ID_Continue", "ID_Start", "Ideo", "Ideographic",
        "Join_C", "Join_Control", "LOE", "Logical_Order_Exception", "Lower", "Lowercase", "Math", "NChar",
        "Noncharacter_Code_Point", "Pat_Syn", "Pat_WS", "Pattern_Syntax", "Pattern_White_Space", "QMark",
        "Quotation_Mark", "RI", "Radical", "Regional_Indicator", "SD", "STerm", "Sentence_Terminal", "Soft_Dotted",
        "Term", "Terminal_Punctuation", "UIdeo", "Unified_Ideograph", "Upper", "Uppercase", "VS", "Variation_Selector",
        "WSpace", "White_Space", "XIDC", "XIDS", "XID_Continue", "XID_Start", "space"
    ]);

    /// <summary>The properties of strings, which only the <c>v</c> flag has.</summary>
    public static FrozenSet<string> JavaScriptStringProperties { get; } = FrozenSet.Create(StringComparer.Ordinal,
    [
        "Basic_Emoji", "Emoji_Keycap_Sequence", "RGI_Emoji", "RGI_Emoji_Flag_Sequence", "RGI_Emoji_Modifier_Sequence",
        "RGI_Emoji_Tag_Sequence", "RGI_Emoji_ZWJ_Sequence"
    ]);

    /// <summary>The names PCRE accepts alone other than scripts: general categories, binary properties, and its own.</summary>
    public static FrozenSet<string> PcreProperties { get; } = FrozenSet.Create(StringComparer.Ordinal,
    [
        "ahex", "alpha", "alphabetic", "any", "ascii", "asciihexdigit", "bidic", "bidicontrol", "bidim", "bidimirrored",
        "c", "cased", "caseignorable", "cc", "cf", "changeswhencasefolded", "changeswhencasemapped",
        "changeswhenlowercased", "changeswhentitlecased", "changeswhenuppercased", "ci", "cn", "co", "cs", "cwcf",
        "cwcm", "cwl", "cwt", "cwu", "dash", "defaultignorablecodepoint", "dep", "deprecated", "di", "dia", "diacritic",
        "ebase", "ecomp", "emod", "emoji", "emojicomponent", "emojimodifier", "emojimodifierbase", "emojipresentation",
        "epres", "ext", "extendedpictographic", "extender", "extpict", "graphemebase", "graphemeextend", "graphemelink",
        "grbase", "grext", "grlink", "hex", "hexdigit", "idc", "idcompatmathcontinue", "idcompatmathstart",
        "idcontinue", "ideo", "ideographic", "ids", "idsb", "idsbinaryoperator", "idst", "idstart",
        "idstrinaryoperator", "idsu", "idsunaryoperator", "incb", "joinc", "joincontrol", "l", "l&", "lc", "ll", "lm",
        "lo", "loe", "logicalorderexception", "lower", "lowercase", "lt", "lu", "m", "math", "mc", "mcm", "me", "mn",
        "modifiercombiningmark", "n", "nchar", "nd", "nl", "no", "noncharactercodepoint", "p", "patsyn",
        "patternsyntax", "patternwhitespace", "patws", "pc", "pcm", "pd", "pe", "pf", "pi", "po",
        "prependedconcatenationmark", "ps", "qmark", "quotationmark", "radical", "regionalindicator", "ri", "s", "sc",
        "sd", "sentenceterminal", "sk", "sm", "so", "softdotted", "space", "sterm", "term", "terminalpunctuation",
        "uideo", "unifiedideograph", "upper", "uppercase", "variationselector", "vs", "whitespace", "wspace", "xan",
        "xidc", "xidcontinue", "xids", "xidstart", "xps", "xsp", "xuc", "xwd", "z", "zl", "zp", "zs"
    ]);

    /// <summary>The script names PCRE accepts, alone or after <c>sc:</c> and <c>scx:</c>.</summary>
    public static FrozenSet<string> PcreScripts { get; } = FrozenSet.Create(StringComparer.Ordinal,
    [
        "adlam", "adlm", "aghb", "ahom", "anatolianhieroglyphs", "arab", "arabic", "armenian", "armi", "armn",
        "avestan", "avst", "bali", "balinese", "bamu", "bamum", "bass", "bassavah", "batak", "batk", "beng", "bengali",
        "bhaiksuki", "bhks", "bopo", "bopomofo", "brah", "brahmi", "brai", "braille", "bugi", "buginese", "buhd",
        "buhid", "cakm", "canadianaboriginal", "cans", "cari", "carian", "caucasianalbanian", "chakma", "cham", "cher",
        "cherokee", "chorasmian", "chrs", "common", "copt", "coptic", "cpmn", "cprt", "cuneiform", "cypriot",
        "cyprominoan", "cyrillic", "cyrl", "deseret", "deva", "devanagari", "diak", "divesakuru", "dogr", "dogra",
        "dsrt", "dupl", "duployan", "egyp", "egyptianhieroglyphs", "elba", "elbasan", "elym", "elymaic", "ethi",
        "ethiopic", "gara", "garay", "geor", "georgian", "glag", "glagolitic", "gong", "gonm", "goth", "gothic", "gran",
        "grantha", "greek", "grek", "gujarati", "gujr", "gukh", "gunjalagondi", "gurmukhi", "guru", "gurungkhema",
        "han", "hang", "hangul", "hani", "hanifirohingya", "hano", "hanunoo", "hatr", "hatran", "hebr", "hebrew",
        "hira", "hiragana", "hluw", "hmng", "hmnp", "hung", "imperialaramaic", "inherited", "inscriptionalpahlavi",
        "inscriptionalparthian", "ital", "java", "javanese", "kaithi", "kali", "kana", "kannada", "katakana", "kawi",
        "kayahli", "khar", "kharoshthi", "khitansmallscript", "khmer", "khmr", "khoj", "khojki", "khudawadi",
        "kiratrai", "kits", "knda", "krai", "kthi", "lana", "lao", "laoo", "latin", "latn", "lepc", "lepcha", "limb",
        "limbu", "lina", "linb", "lineara", "linearb", "lisu", "lyci", "lycian", "lydi", "lydian", "mahajani", "mahj",
        "maka", "makasar", "malayalam", "mand", "mandaic", "mani", "manichaean", "marc", "marchen", "masaramgondi",
        "medefaidrin", "medf", "meeteimayek", "mend", "mendekikakui", "merc", "mero", "meroiticcursive",
        "meroitichieroglyphs", "miao", "mlym", "modi", "mong", "mongolian", "mro", "mroo", "mtei", "mult", "multani",
        "myanmar", "mymr", "nabataean", "nagm", "nagmundari", "nand", "nandinagari", "narb", "nbat", "newa",
        "newtailue", "nko", "nkoo", "nshu", "nushu", "nyiakengpuachuehmong", "ogam", "ogham", "olchiki", "olck",
        "oldhungarian", "olditalic", "oldnortharabian", "oldpermic", "oldpersian", "oldsogdian", "oldsoutharabian",
        "oldturkic", "olduyghur", "olonal", "onao", "oriya", "orkh", "orya", "osage", "osge", "osma", "osmanya", "ougr",
        "pahawhhmong", "palm", "palmyrene", "pauc", "paucinhau", "perm", "phag", "phagspa", "phli", "phlp", "phnx",
        "phoenician", "plrd", "prti", "psalterpahlavi", "qaac", "qaai", "rejang", "rjng", "rohg", "runic", "runr",
        "samaritan", "samr", "sarb", "saur", "saurashtra", "sgnw", "sharada", "shavian", "shaw", "shrd", "sidd",
        "siddham", "signwriting", "sind", "sinh", "sinhala", "sogd", "sogdian", "sogo", "sora", "sorasompeng", "soyo",
        "soyombo", "sund", "sundanese", "sunu", "sunuwar", "sylo", "sylotinagri", "syrc", "syriac", "tagalog", "tagb",
        "tagbanwa", "taile", "taitham", "taiviet", "takr", "takri", "tale", "talu", "tamil", "taml", "tang", "tangsa",
        "tangut", "tavt", "telu", "telugu", "tfng", "tglg", "thaa", "thaana", "thai", "tibetan", "tibt", "tifinagh",
        "tirh", "tirhuta", "tnsa", "todhri", "todr", "toto", "tulutigalari", "tutg", "ugar", "ugaritic", "unknown",
        "vai", "vaii", "vith", "vithkuqi", "wancho", "wara", "warangciti", "wcho", "xpeo", "xsux", "yezi", "yezidi",
        "yi", "yiii", "zanabazarsquare", "zanb", "zinh", "zyyy", "zzzz"
    ]);

    /// <summary>The bidirectional classes PCRE accepts after <c>bc:</c>.</summary>
    public static FrozenSet<string> PcreBidiClasses { get; } = FrozenSet.Create(StringComparer.Ordinal,
    [
        "al", "an", "b", "bn", "c", "control", "cs", "en", "es", "et", "fsi", "l", "lre", "lri", "lro", "m", "nsm",
        "on", "pdf", "pdi", "r", "rle", "rli", "rlo", "s", "ws"
    ]);
}
