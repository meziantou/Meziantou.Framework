namespace Meziantou.Framework;

/// <summary>Represents a Unicode block (named range of code points).</summary>
public enum UnicodeBlock : ushort
{
    /// <summary>Unknown or unassigned block.</summary>
    Unknown = 0,

    /// <summary>Basic Latin (U+0000..U+007F).</summary>
    BasicLatin = 1,

    /// <summary>Latin-1 Supplement (U+0080..U+00FF).</summary>
    Latin1Supplement = 2,

    /// <summary>Latin Extended-A (U+0100..U+017F).</summary>
    LatinExtendedA = 3,

    /// <summary>Latin Extended-B (U+0180..U+024F).</summary>
    LatinExtendedB = 4,

    /// <summary>IPA Extensions (U+0250..U+02AF).</summary>
    IpaExtensions = 5,

    /// <summary>Spacing Modifier Letters (U+02B0..U+02FF).</summary>
    SpacingModifierLetters = 6,

    /// <summary>Combining Diacritical Marks (U+0300..U+036F).</summary>
    CombiningDiacriticalMarks = 7,

    /// <summary>Greek and Coptic (U+0370..U+03FF).</summary>
    GreekAndCoptic = 8,

    /// <summary>Cyrillic (U+0400..U+04FF).</summary>
    Cyrillic = 9,

    /// <summary>Cyrillic Supplement (U+0500..U+052F).</summary>
    CyrillicSupplement = 10,

    /// <summary>Armenian (U+0530..U+058F).</summary>
    Armenian = 11,

    /// <summary>Hebrew (U+0590..U+05FF).</summary>
    Hebrew = 12,

    /// <summary>Arabic (U+0600..U+06FF).</summary>
    Arabic = 13,

    /// <summary>Syriac (U+0700..U+074F).</summary>
    Syriac = 14,

    /// <summary>Arabic Supplement (U+0750..U+077F).</summary>
    ArabicSupplement = 15,

    /// <summary>Thaana (U+0780..U+07BF).</summary>
    Thaana = 16,

    /// <summary>NKo (U+07C0..U+07FF).</summary>
    NKo = 17,

    /// <summary>Samaritan (U+0800..U+083F).</summary>
    Samaritan = 18,

    /// <summary>Mandaic (U+0840..U+085F).</summary>
    Mandaic = 19,

    /// <summary>Syriac Supplement (U+0860..U+086F).</summary>
    SyriacSupplement = 20,

    /// <summary>Arabic Extended-B (U+0870..U+089F).</summary>
    ArabicExtendedB = 21,

    /// <summary>Arabic Extended-A (U+08A0..U+08FF).</summary>
    ArabicExtendedA = 22,

    /// <summary>Devanagari (U+0900..U+097F).</summary>
    Devanagari = 23,

    /// <summary>Bengali (U+0980..U+09FF).</summary>
    Bengali = 24,

    /// <summary>Gurmukhi (U+0A00..U+0A7F).</summary>
    Gurmukhi = 25,

    /// <summary>Gujarati (U+0A80..U+0AFF).</summary>
    Gujarati = 26,

    /// <summary>Oriya (U+0B00..U+0B7F).</summary>
    Oriya = 27,

    /// <summary>Tamil (U+0B80..U+0BFF).</summary>
    Tamil = 28,

    /// <summary>Telugu (U+0C00..U+0C7F).</summary>
    Telugu = 29,

    /// <summary>Kannada (U+0C80..U+0CFF).</summary>
    Kannada = 30,

    /// <summary>Malayalam (U+0D00..U+0D7F).</summary>
    Malayalam = 31,

    /// <summary>Sinhala (U+0D80..U+0DFF).</summary>
    Sinhala = 32,

    /// <summary>Thai (U+0E00..U+0E7F).</summary>
    Thai = 33,

    /// <summary>Lao (U+0E80..U+0EFF).</summary>
    Lao = 34,

    /// <summary>Tibetan (U+0F00..U+0FFF).</summary>
    Tibetan = 35,

    /// <summary>Myanmar (U+1000..U+109F).</summary>
    Myanmar = 36,

    /// <summary>Georgian (U+10A0..U+10FF).</summary>
    Georgian = 37,

    /// <summary>Hangul Jamo (U+1100..U+11FF).</summary>
    HangulJamo = 38,

    /// <summary>Ethiopic (U+1200..U+137F).</summary>
    Ethiopic = 39,

    /// <summary>Ethiopic Supplement (U+1380..U+139F).</summary>
    EthiopicSupplement = 40,

    /// <summary>Cherokee (U+13A0..U+13FF).</summary>
    Cherokee = 41,

    /// <summary>Unified Canadian Aboriginal Syllabics (U+1400..U+167F).</summary>
    UnifiedCanadianAboriginalSyllabics = 42,

    /// <summary>Ogham (U+1680..U+169F).</summary>
    Ogham = 43,

    /// <summary>Runic (U+16A0..U+16FF).</summary>
    Runic = 44,

    /// <summary>Tagalog (U+1700..U+171F).</summary>
    Tagalog = 45,

    /// <summary>Hanunoo (U+1720..U+173F).</summary>
    Hanunoo = 46,

    /// <summary>Buhid (U+1740..U+175F).</summary>
    Buhid = 47,

    /// <summary>Tagbanwa (U+1760..U+177F).</summary>
    Tagbanwa = 48,

    /// <summary>Khmer (U+1780..U+17FF).</summary>
    Khmer = 49,

    /// <summary>Mongolian (U+1800..U+18AF).</summary>
    Mongolian = 50,

    /// <summary>Unified Canadian Aboriginal Syllabics Extended (U+18B0..U+18FF).</summary>
    UnifiedCanadianAboriginalSyllabicsExtended = 51,

    /// <summary>Limbu (U+1900..U+194F).</summary>
    Limbu = 52,

    /// <summary>Tai Le (U+1950..U+197F).</summary>
    TaiLe = 53,

    /// <summary>New Tai Lue (U+1980..U+19DF).</summary>
    NewTaiLue = 54,

    /// <summary>Khmer Symbols (U+19E0..U+19FF).</summary>
    KhmerSymbols = 55,

    /// <summary>Buginese (U+1A00..U+1A1F).</summary>
    Buginese = 56,

    /// <summary>Tai Tham (U+1A20..U+1AAF).</summary>
    TaiTham = 57,

    /// <summary>Combining Diacritical Marks Extended (U+1AB0..U+1AFF).</summary>
    CombiningDiacriticalMarksExtended = 58,

    /// <summary>Balinese (U+1B00..U+1B7F).</summary>
    Balinese = 59,

    /// <summary>Sundanese (U+1B80..U+1BBF).</summary>
    Sundanese = 60,

    /// <summary>Batak (U+1BC0..U+1BFF).</summary>
    Batak = 61,

    /// <summary>Lepcha (U+1C00..U+1C4F).</summary>
    Lepcha = 62,

    /// <summary>Ol Chiki (U+1C50..U+1C7F).</summary>
    OlChiki = 63,

    /// <summary>Cyrillic Extended-C (U+1C80..U+1C8F).</summary>
    CyrillicExtendedC = 64,

    /// <summary>Georgian Extended (U+1C90..U+1CBF).</summary>
    GeorgianExtended = 65,

    /// <summary>Sundanese Supplement (U+1CC0..U+1CCF).</summary>
    SundaneseSupplement = 66,

    /// <summary>Vedic Extensions (U+1CD0..U+1CFF).</summary>
    VedicExtensions = 67,

    /// <summary>Phonetic Extensions (U+1D00..U+1D7F).</summary>
    PhoneticExtensions = 68,

    /// <summary>Phonetic Extensions Supplement (U+1D80..U+1DBF).</summary>
    PhoneticExtensionsSupplement = 69,

    /// <summary>Combining Diacritical Marks Supplement (U+1DC0..U+1DFF).</summary>
    CombiningDiacriticalMarksSupplement = 70,

    /// <summary>Latin Extended Additional (U+1E00..U+1EFF).</summary>
    LatinExtendedAdditional = 71,

    /// <summary>Greek Extended (U+1F00..U+1FFF).</summary>
    GreekExtended = 72,

    /// <summary>General Punctuation (U+2000..U+206F).</summary>
    GeneralPunctuation = 73,

    /// <summary>Superscripts and Subscripts (U+2070..U+209F).</summary>
    SuperscriptsAndSubscripts = 74,

    /// <summary>Currency Symbols (U+20A0..U+20CF).</summary>
    CurrencySymbols = 75,

    /// <summary>Combining Diacritical Marks for Symbols (U+20D0..U+20FF).</summary>
    CombiningDiacriticalMarksForSymbols = 76,

    /// <summary>Letterlike Symbols (U+2100..U+214F).</summary>
    LetterlikeSymbols = 77,

    /// <summary>Number Forms (U+2150..U+218F).</summary>
    NumberForms = 78,

    /// <summary>Arrows (U+2190..U+21FF).</summary>
    Arrows = 79,

    /// <summary>Mathematical Operators (U+2200..U+22FF).</summary>
    MathematicalOperators = 80,

    /// <summary>Miscellaneous Technical (U+2300..U+23FF).</summary>
    MiscellaneousTechnical = 81,

    /// <summary>Control Pictures (U+2400..U+243F).</summary>
    ControlPictures = 82,

    /// <summary>Optical Character Recognition (U+2440..U+245F).</summary>
    OpticalCharacterRecognition = 83,

    /// <summary>Enclosed Alphanumerics (U+2460..U+24FF).</summary>
    EnclosedAlphanumerics = 84,

    /// <summary>Box Drawing (U+2500..U+257F).</summary>
    BoxDrawing = 85,

    /// <summary>Block Elements (U+2580..U+259F).</summary>
    BlockElements = 86,

    /// <summary>Geometric Shapes (U+25A0..U+25FF).</summary>
    GeometricShapes = 87,

    /// <summary>Miscellaneous Symbols (U+2600..U+26FF).</summary>
    MiscellaneousSymbols = 88,

    /// <summary>Dingbats (U+2700..U+27BF).</summary>
    Dingbats = 89,

    /// <summary>Miscellaneous Mathematical Symbols-A (U+27C0..U+27EF).</summary>
    MiscellaneousMathematicalSymbolsA = 90,

    /// <summary>Supplemental Arrows-A (U+27F0..U+27FF).</summary>
    SupplementalArrowsA = 91,

    /// <summary>Braille Patterns (U+2800..U+28FF).</summary>
    BraillePatterns = 92,

    /// <summary>Supplemental Arrows-B (U+2900..U+297F).</summary>
    SupplementalArrowsB = 93,

    /// <summary>Miscellaneous Mathematical Symbols-B (U+2980..U+29FF).</summary>
    MiscellaneousMathematicalSymbolsB = 94,

    /// <summary>Supplemental Mathematical Operators (U+2A00..U+2AFF).</summary>
    SupplementalMathematicalOperators = 95,

    /// <summary>Miscellaneous Symbols and Arrows (U+2B00..U+2BFF).</summary>
    MiscellaneousSymbolsAndArrows = 96,

    /// <summary>Glagolitic (U+2C00..U+2C5F).</summary>
    Glagolitic = 97,

    /// <summary>Latin Extended-C (U+2C60..U+2C7F).</summary>
    LatinExtendedC = 98,

    /// <summary>Coptic (U+2C80..U+2CFF).</summary>
    Coptic = 99,

    /// <summary>Georgian Supplement (U+2D00..U+2D2F).</summary>
    GeorgianSupplement = 100,

    /// <summary>Tifinagh (U+2D30..U+2D7F).</summary>
    Tifinagh = 101,

    /// <summary>Ethiopic Extended (U+2D80..U+2DDF).</summary>
    EthiopicExtended = 102,

    /// <summary>Cyrillic Extended-A (U+2DE0..U+2DFF).</summary>
    CyrillicExtendedA = 103,

    /// <summary>Supplemental Punctuation (U+2E00..U+2E7F).</summary>
    SupplementalPunctuation = 104,

    /// <summary>CJK Radicals Supplement (U+2E80..U+2EFF).</summary>
    CjkRadicalsSupplement = 105,

    /// <summary>Kangxi Radicals (U+2F00..U+2FDF).</summary>
    KangxiRadicals = 106,

    /// <summary>Ideographic Description Characters (U+2FF0..U+2FFF).</summary>
    IdeographicDescriptionCharacters = 107,

    /// <summary>CJK Symbols and Punctuation (U+3000..U+303F).</summary>
    CjkSymbolsAndPunctuation = 108,

    /// <summary>Hiragana (U+3040..U+309F).</summary>
    Hiragana = 109,

    /// <summary>Katakana (U+30A0..U+30FF).</summary>
    Katakana = 110,

    /// <summary>Bopomofo (U+3100..U+312F).</summary>
    Bopomofo = 111,

    /// <summary>Hangul Compatibility Jamo (U+3130..U+318F).</summary>
    HangulCompatibilityJamo = 112,

    /// <summary>Kanbun (U+3190..U+319F).</summary>
    Kanbun = 113,

    /// <summary>Bopomofo Extended (U+31A0..U+31BF).</summary>
    BopomofoExtended = 114,

    /// <summary>CJK Strokes (U+31C0..U+31EF).</summary>
    CjkStrokes = 115,

    /// <summary>Katakana Phonetic Extensions (U+31F0..U+31FF).</summary>
    KatakanaPhoneticExtensions = 116,

    /// <summary>Enclosed CJK Letters and Months (U+3200..U+32FF).</summary>
    EnclosedCjkLettersAndMonths = 117,

    /// <summary>CJK Compatibility (U+3300..U+33FF).</summary>
    CjkCompatibility = 118,

    /// <summary>CJK Unified Ideographs Extension A (U+3400..U+4DBF).</summary>
    CjkUnifiedIdeographsExtensionA = 119,

    /// <summary>Yijing Hexagram Symbols (U+4DC0..U+4DFF).</summary>
    YijingHexagramSymbols = 120,

    /// <summary>CJK Unified Ideographs (U+4E00..U+9FFF).</summary>
    CjkUnifiedIdeographs = 121,

    /// <summary>Yi Syllables (U+A000..U+A48F).</summary>
    YiSyllables = 122,

    /// <summary>Yi Radicals (U+A490..U+A4CF).</summary>
    YiRadicals = 123,

    /// <summary>Lisu (U+A4D0..U+A4FF).</summary>
    Lisu = 124,

    /// <summary>Vai (U+A500..U+A63F).</summary>
    Vai = 125,

    /// <summary>Cyrillic Extended-B (U+A640..U+A69F).</summary>
    CyrillicExtendedB = 126,

    /// <summary>Bamum (U+A6A0..U+A6FF).</summary>
    Bamum = 127,

    /// <summary>Modifier Tone Letters (U+A700..U+A71F).</summary>
    ModifierToneLetters = 128,

    /// <summary>Latin Extended-D (U+A720..U+A7FF).</summary>
    LatinExtendedD = 129,

    /// <summary>Syloti Nagri (U+A800..U+A82F).</summary>
    SylotiNagri = 130,

    /// <summary>Common Indic Number Forms (U+A830..U+A83F).</summary>
    CommonIndicNumberForms = 131,

    /// <summary>Phags-pa (U+A840..U+A87F).</summary>
    PhagsPa = 132,

    /// <summary>Saurashtra (U+A880..U+A8DF).</summary>
    Saurashtra = 133,

    /// <summary>Devanagari Extended (U+A8E0..U+A8FF).</summary>
    DevanagariExtended = 134,

    /// <summary>Kayah Li (U+A900..U+A92F).</summary>
    KayahLi = 135,

    /// <summary>Rejang (U+A930..U+A95F).</summary>
    Rejang = 136,

    /// <summary>Hangul Jamo Extended-A (U+A960..U+A97F).</summary>
    HangulJamoExtendedA = 137,

    /// <summary>Javanese (U+A980..U+A9DF).</summary>
    Javanese = 138,

    /// <summary>Myanmar Extended-B (U+A9E0..U+A9FF).</summary>
    MyanmarExtendedB = 139,

    /// <summary>Cham (U+AA00..U+AA5F).</summary>
    Cham = 140,

    /// <summary>Myanmar Extended-A (U+AA60..U+AA7F).</summary>
    MyanmarExtendedA = 141,

    /// <summary>Tai Viet (U+AA80..U+AADF).</summary>
    TaiViet = 142,

    /// <summary>Meetei Mayek Extensions (U+AAE0..U+AAFF).</summary>
    MeeteiMayekExtensions = 143,

    /// <summary>Ethiopic Extended-A (U+AB00..U+AB2F).</summary>
    EthiopicExtendedA = 144,

    /// <summary>Latin Extended-E (U+AB30..U+AB6F).</summary>
    LatinExtendedE = 145,

    /// <summary>Cherokee Supplement (U+AB70..U+ABBF).</summary>
    CherokeeSupplement = 146,

    /// <summary>Meetei Mayek (U+ABC0..U+ABFF).</summary>
    MeeteiMayek = 147,

    /// <summary>Hangul Syllables (U+AC00..U+D7AF).</summary>
    HangulSyllables = 148,

    /// <summary>Hangul Jamo Extended-B (U+D7B0..U+D7FF).</summary>
    HangulJamoExtendedB = 149,

    /// <summary>High Surrogates (U+D800..U+DB7F).</summary>
    HighSurrogates = 150,

    /// <summary>High Private Use Surrogates (U+DB80..U+DBFF).</summary>
    HighPrivateUseSurrogates = 151,

    /// <summary>Low Surrogates (U+DC00..U+DFFF).</summary>
    LowSurrogates = 152,

    /// <summary>Private Use Area (U+E000..U+F8FF).</summary>
    PrivateUseArea = 153,

    /// <summary>CJK Compatibility Ideographs (U+F900..U+FAFF).</summary>
    CjkCompatibilityIdeographs = 154,

    /// <summary>Alphabetic Presentation Forms (U+FB00..U+FB4F).</summary>
    AlphabeticPresentationForms = 155,

    /// <summary>Arabic Presentation Forms-A (U+FB50..U+FDFF).</summary>
    ArabicPresentationFormsA = 156,

    /// <summary>Variation Selectors (U+FE00..U+FE0F).</summary>
    VariationSelectors = 157,

    /// <summary>Vertical Forms (U+FE10..U+FE1F).</summary>
    VerticalForms = 158,

    /// <summary>Combining Half Marks (U+FE20..U+FE2F).</summary>
    CombiningHalfMarks = 159,

    /// <summary>CJK Compatibility Forms (U+FE30..U+FE4F).</summary>
    CjkCompatibilityForms = 160,

    /// <summary>Small Form Variants (U+FE50..U+FE6F).</summary>
    SmallFormVariants = 161,

    /// <summary>Arabic Presentation Forms-B (U+FE70..U+FEFF).</summary>
    ArabicPresentationFormsB = 162,

    /// <summary>Halfwidth and Fullwidth Forms (U+FF00..U+FFEF).</summary>
    HalfwidthAndFullwidthForms = 163,

    /// <summary>Specials (U+FFF0..U+FFFF).</summary>
    Specials = 164,

    /// <summary>Linear B Syllabary (U+10000..U+1007F).</summary>
    LinearBSyllabary = 165,

    /// <summary>Linear B Ideograms (U+10080..U+100FF).</summary>
    LinearBIdeograms = 166,

    /// <summary>Aegean Numbers (U+10100..U+1013F).</summary>
    AegeanNumbers = 167,

    /// <summary>Ancient Greek Numbers (U+10140..U+1018F).</summary>
    AncientGreekNumbers = 168,

    /// <summary>Ancient Symbols (U+10190..U+101CF).</summary>
    AncientSymbols = 169,

    /// <summary>Phaistos Disc (U+101D0..U+101FF).</summary>
    PhaistosDisc = 170,

    /// <summary>Lycian (U+10280..U+1029F).</summary>
    Lycian = 171,

    /// <summary>Carian (U+102A0..U+102DF).</summary>
    Carian = 172,

    /// <summary>Coptic Epact Numbers (U+102E0..U+102FF).</summary>
    CopticEpactNumbers = 173,

    /// <summary>Old Italic (U+10300..U+1032F).</summary>
    OldItalic = 174,

    /// <summary>Gothic (U+10330..U+1034F).</summary>
    Gothic = 175,

    /// <summary>Old Permic (U+10350..U+1037F).</summary>
    OldPermic = 176,

    /// <summary>Ugaritic (U+10380..U+1039F).</summary>
    Ugaritic = 177,

    /// <summary>Old Persian (U+103A0..U+103DF).</summary>
    OldPersian = 178,

    /// <summary>Deseret (U+10400..U+1044F).</summary>
    Deseret = 179,

    /// <summary>Shavian (U+10450..U+1047F).</summary>
    Shavian = 180,

    /// <summary>Osmanya (U+10480..U+104AF).</summary>
    Osmanya = 181,

    /// <summary>Osage (U+104B0..U+104FF).</summary>
    Osage = 182,

    /// <summary>Elbasan (U+10500..U+1052F).</summary>
    Elbasan = 183,

    /// <summary>Caucasian Albanian (U+10530..U+1056F).</summary>
    CaucasianAlbanian = 184,

    /// <summary>Vithkuqi (U+10570..U+105BF).</summary>
    Vithkuqi = 185,

    /// <summary>Linear A (U+10600..U+1077F).</summary>
    LinearA = 186,

    /// <summary>Latin Extended-F (U+10780..U+107BF).</summary>
    LatinExtendedF = 187,

    /// <summary>Cypriot Syllabary (U+10800..U+1083F).</summary>
    CypriotSyllabary = 188,

    /// <summary>Imperial Aramaic (U+10840..U+1085F).</summary>
    ImperialAramaic = 189,

    /// <summary>Palmyrene (U+10860..U+1087F).</summary>
    Palmyrene = 190,

    /// <summary>Nabataean (U+10880..U+108AF).</summary>
    Nabataean = 191,

    /// <summary>Hatran (U+108E0..U+108FF).</summary>
    Hatran = 192,

    /// <summary>Phoenician (U+10900..U+1091F).</summary>
    Phoenician = 193,

    /// <summary>Lydian (U+10920..U+1093F).</summary>
    Lydian = 194,

    /// <summary>Meroitic Hieroglyphs (U+10980..U+1099F).</summary>
    MeroiticHieroglyphs = 195,

    /// <summary>Meroitic Cursive (U+109A0..U+109FF).</summary>
    MeroiticCursive = 196,

    /// <summary>Kharoshthi (U+10A00..U+10A5F).</summary>
    Kharoshthi = 197,

    /// <summary>Old South Arabian (U+10A60..U+10A7F).</summary>
    OldSouthArabian = 198,

    /// <summary>Old North Arabian (U+10A80..U+10A9F).</summary>
    OldNorthArabian = 199,

    /// <summary>Manichaean (U+10AC0..U+10AFF).</summary>
    Manichaean = 200,

    /// <summary>Avestan (U+10B00..U+10B3F).</summary>
    Avestan = 201,

    /// <summary>Inscriptional Parthian (U+10B40..U+10B5F).</summary>
    InscriptionalParthian = 202,

    /// <summary>Inscriptional Pahlavi (U+10B60..U+10B7F).</summary>
    InscriptionalPahlavi = 203,

    /// <summary>Psalter Pahlavi (U+10B80..U+10BAF).</summary>
    PsalterPahlavi = 204,

    /// <summary>Old Turkic (U+10C00..U+10C4F).</summary>
    OldTurkic = 205,

    /// <summary>Old Hungarian (U+10C80..U+10CFF).</summary>
    OldHungarian = 206,

    /// <summary>Garay (U+10D00..U+10D3F).</summary>
    Garay = 207,

    /// <summary>Rumi Numeral Symbols (U+10E60..U+10E7F).</summary>
    RumiNumeralSymbols = 208,

    /// <summary>Yezidi (U+10E80..U+10EBF).</summary>
    Yezidi = 209,

    /// <summary>Arabic Extended-C (U+10EC0..U+10EFF).</summary>
    ArabicExtendedC = 210,

    /// <summary>Old Sogdian (U+10F00..U+10F2F).</summary>
    OldSogdian = 211,

    /// <summary>Sogdian (U+10F30..U+10F6F).</summary>
    Sogdian = 212,

    /// <summary>Old Uyghur (U+10F70..U+10FAF).</summary>
    OldUyghur = 213,

    /// <summary>Chorasmian (U+10FB0..U+10FDF).</summary>
    Chorasmian = 214,

    /// <summary>Elymaic (U+10FE0..U+10FFF).</summary>
    Elymaic = 215,

    /// <summary>Brahmi (U+11000..U+1107F).</summary>
    Brahmi = 216,

    /// <summary>Kaithi (U+11080..U+110CF).</summary>
    Kaithi = 217,

    /// <summary>Sora Sompeng (U+110D0..U+110FF).</summary>
    SoraSompeng = 218,

    /// <summary>Chakma (U+11100..U+1114F).</summary>
    Chakma = 219,

    /// <summary>Mahajani (U+11150..U+1117F).</summary>
    Mahajani = 220,

    /// <summary>Sharada (U+11180..U+111DF).</summary>
    Sharada = 221,

    /// <summary>Sinhala Archaic Numbers (U+111E0..U+111FF).</summary>
    SinhalaArchaicNumbers = 222,

    /// <summary>Khojki (U+11200..U+1124F).</summary>
    Khojki = 223,

    /// <summary>Multani (U+11280..U+112AF).</summary>
    Multani = 224,

    /// <summary>Khudawadi (U+112B0..U+112FF).</summary>
    Khudawadi = 225,

    /// <summary>Grantha (U+11300..U+1137F).</summary>
    Grantha = 226,

    /// <summary>Newa (U+11400..U+1147F).</summary>
    Newa = 227,

    /// <summary>Tirhuta (U+11480..U+114DF).</summary>
    Tirhuta = 228,

    /// <summary>Siddham (U+11580..U+115FF).</summary>
    Siddham = 229,

    /// <summary>Modi (U+11600..U+1165F).</summary>
    Modi = 230,

    /// <summary>Mongolian Supplement (U+11660..U+1167F).</summary>
    MongolianSupplement = 231,

    /// <summary>Takri (U+11680..U+116CF).</summary>
    Takri = 232,

    /// <summary>Ahom (U+11700..U+1174F).</summary>
    Ahom = 233,

    /// <summary>Dogra (U+11800..U+1184F).</summary>
    Dogra = 234,

    /// <summary>Warang Citi (U+118A0..U+118FF).</summary>
    WarangCiti = 235,

    /// <summary>Dives Akuru (U+11900..U+1195F).</summary>
    DivesAkuru = 236,

    /// <summary>Nandinagari (U+119A0..U+119FF).</summary>
    Nandinagari = 237,

    /// <summary>Zanabazar Square (U+11A00..U+11A4F).</summary>
    ZanabazarSquare = 238,

    /// <summary>Soyombo (U+11A50..U+11AAF).</summary>
    Soyombo = 239,

    /// <summary>Unified Canadian Aboriginal Syllabics Extended-A (U+11AB0..U+11ABF).</summary>
    UnifiedCanadianAboriginalSyllabicsExtendedA = 240,

    /// <summary>Pau Cin Hau (U+11AC0..U+11AFF).</summary>
    PauCinHau = 241,

    /// <summary>Devanagari Extended-A (U+11B00..U+11B5F).</summary>
    DevanagariExtendedA = 242,

    /// <summary>Bhaiksuki (U+11C00..U+11C6F).</summary>
    Bhaiksuki = 243,

    /// <summary>Marchen (U+11C70..U+11CBF).</summary>
    Marchen = 244,

    /// <summary>Masaram Gondi (U+11D00..U+11D5F).</summary>
    MasaramGondi = 245,

    /// <summary>Gunjala Gondi (U+11D60..U+11DAF).</summary>
    GunjalaGondi = 246,

    /// <summary>Makasar (U+11EE0..U+11EFF).</summary>
    Makasar = 247,

    /// <summary>Kawi (U+11F00..U+11F5F).</summary>
    Kawi = 248,

    /// <summary>Lisu Supplement (U+11FB0..U+11FBF).</summary>
    LisuSupplement = 249,

    /// <summary>Tamil Supplement (U+11FC0..U+11FFF).</summary>
    TamilSupplement = 250,

    /// <summary>Cuneiform (U+12000..U+123FF).</summary>
    Cuneiform = 251,

    /// <summary>Cuneiform Numbers and Punctuation (U+12400..U+1247F).</summary>
    CuneiformNumbersAndPunctuation = 252,

    /// <summary>Early Dynastic Cuneiform (U+12480..U+1254F).</summary>
    EarlyDynasticCuneiform = 253,

    /// <summary>Cypro-Minoan (U+12F90..U+12FFF).</summary>
    CyproMinoan = 254,

    /// <summary>Egyptian Hieroglyphs (U+13000..U+1342F).</summary>
    EgyptianHieroglyphs = 255,

    /// <summary>Egyptian Hieroglyph Format Controls (U+13430..U+1345F).</summary>
    EgyptianHieroglyphFormatControls = 256,

    /// <summary>Anatolian Hieroglyphs (U+14400..U+1467F).</summary>
    AnatolianHieroglyphs = 257,

    /// <summary>Bamum Supplement (U+16800..U+16A3F).</summary>
    BamumSupplement = 258,

    /// <summary>Mro (U+16A40..U+16A6F).</summary>
    Mro = 259,

    /// <summary>Tangsa (U+16A70..U+16ACF).</summary>
    Tangsa = 260,

    /// <summary>Bassa Vah (U+16AD0..U+16AFF).</summary>
    BassaVah = 261,

    /// <summary>Pahawh Hmong (U+16B00..U+16B8F).</summary>
    PahawhHmong = 262,

    /// <summary>Medefaidrin (U+16E40..U+16E9F).</summary>
    Medefaidrin = 263,

    /// <summary>Miao (U+16F00..U+16F9F).</summary>
    Miao = 264,

    /// <summary>Ideographic Symbols and Punctuation (U+16FE0..U+16FFF).</summary>
    IdeographicSymbolsAndPunctuation = 265,

    /// <summary>Tangut (U+17000..U+187FF).</summary>
    Tangut = 266,

    /// <summary>Tangut Components (U+18800..U+18AFF).</summary>
    TangutComponents = 267,

    /// <summary>Khitan Small Script (U+18B00..U+18CFF).</summary>
    KhitanSmallScript = 268,

    /// <summary>Tangut Supplement (U+18D00..U+18D7F).</summary>
    TangutSupplement = 269,

    /// <summary>Kana Extended-B (U+1AFF0..U+1AFFF).</summary>
    KanaExtendedB = 270,

    /// <summary>Kana Supplement (U+1B000..U+1B0FF).</summary>
    KanaSupplement = 271,

    /// <summary>Kana Extended-A (U+1B100..U+1B12F).</summary>
    KanaExtendedA = 272,

    /// <summary>Small Kana Extension (U+1B130..U+1B16F).</summary>
    SmallKanaExtension = 273,

    /// <summary>Nushu (U+1B170..U+1B2FF).</summary>
    Nushu = 274,

    /// <summary>Duployan (U+1BC00..U+1BC9F).</summary>
    Duployan = 275,

    /// <summary>Shorthand Format Controls (U+1BCA0..U+1BCAF).</summary>
    ShorthandFormatControls = 276,

    /// <summary>Znamenny Musical Notation (U+1CF00..U+1CFCF).</summary>
    ZnamennyMusicalNotation = 277,

    /// <summary>Byzantine Musical Symbols (U+1D000..U+1D0FF).</summary>
    ByzantineMusicalSymbols = 278,

    /// <summary>Musical Symbols (U+1D100..U+1D1FF).</summary>
    MusicalSymbols = 279,

    /// <summary>Ancient Greek Musical Notation (U+1D200..U+1D24F).</summary>
    AncientGreekMusicalNotation = 280,

    /// <summary>Kaktovik Numerals (U+1D2C0..U+1D2DF).</summary>
    KaktovikNumerals = 281,

    /// <summary>Mayan Numerals (U+1D2E0..U+1D2FF).</summary>
    MayanNumerals = 282,

    /// <summary>Tai Xuan Jing Symbols (U+1D300..U+1D35F).</summary>
    TaiXuanJingSymbols = 283,

    /// <summary>Counting Rod Numerals (U+1D360..U+1D37F).</summary>
    CountingRodNumerals = 284,

    /// <summary>Mathematical Alphanumeric Symbols (U+1D400..U+1D7FF).</summary>
    MathematicalAlphanumericSymbols = 285,

    /// <summary>Sutton SignWriting (U+1D800..U+1DAAF).</summary>
    SuttonSignWriting = 286,

    /// <summary>Latin Extended-G (U+1DF00..U+1DFFF).</summary>
    LatinExtendedG = 287,

    /// <summary>Glagolitic Supplement (U+1E000..U+1E02F).</summary>
    GlagoliticSupplement = 288,

    /// <summary>Cyrillic Extended-D (U+1E030..U+1E08F).</summary>
    CyrillicExtendedD = 289,

    /// <summary>Nyiakeng Puachue Hmong (U+1E100..U+1E14F).</summary>
    NyiakengPuachueHmong = 290,

    /// <summary>Toto (U+1E290..U+1E2BF).</summary>
    Toto = 291,

    /// <summary>Wancho (U+1E2C0..U+1E2FF).</summary>
    Wancho = 292,

    /// <summary>Nag Mundari (U+1E4D0..U+1E4FF).</summary>
    NagMundari = 293,

    /// <summary>Ethiopic Extended-B (U+1E7E0..U+1E7FF).</summary>
    EthiopicExtendedB = 294,

    /// <summary>Mende Kikakui (U+1E800..U+1E8DF).</summary>
    MendeKikakui = 295,

    /// <summary>Adlam (U+1E900..U+1E95F).</summary>
    Adlam = 296,

    /// <summary>Indic Siyaq Numbers (U+1EC70..U+1ECBF).</summary>
    IndicSiyaqNumbers = 297,

    /// <summary>Ottoman Siyaq Numbers (U+1ED00..U+1ED4F).</summary>
    OttomanSiyaqNumbers = 298,

    /// <summary>Arabic Mathematical Alphabetic Symbols (U+1EE00..U+1EEFF).</summary>
    ArabicMathematicalAlphabeticSymbols = 299,

    /// <summary>Mahjong Tiles (U+1F000..U+1F02F).</summary>
    MahjongTiles = 300,

    /// <summary>Domino Tiles (U+1F030..U+1F09F).</summary>
    DominoTiles = 301,

    /// <summary>Playing Cards (U+1F0A0..U+1F0FF).</summary>
    PlayingCards = 302,

    /// <summary>Enclosed Alphanumeric Supplement (U+1F100..U+1F1FF).</summary>
    EnclosedAlphanumericSupplement = 303,

    /// <summary>Enclosed Ideographic Supplement (U+1F200..U+1F2FF).</summary>
    EnclosedIdeographicSupplement = 304,

    /// <summary>Miscellaneous Symbols and Pictographs (U+1F300..U+1F5FF).</summary>
    MiscellaneousSymbolsAndPictographs = 305,

    /// <summary>Emoticons (U+1F600..U+1F64F).</summary>
    Emoticons = 306,

    /// <summary>Ornamental Dingbats (U+1F650..U+1F67F).</summary>
    OrnamentalDingbats = 307,

    /// <summary>Transport and Map Symbols (U+1F680..U+1F6FF).</summary>
    TransportAndMapSymbols = 308,

    /// <summary>Alchemical Symbols (U+1F700..U+1F77F).</summary>
    AlchemicalSymbols = 309,

    /// <summary>Geometric Shapes Extended (U+1F780..U+1F7FF).</summary>
    GeometricShapesExtended = 310,

    /// <summary>Supplemental Arrows-C (U+1F800..U+1F8FF).</summary>
    SupplementalArrowsC = 311,

    /// <summary>Supplemental Symbols and Pictographs (U+1F900..U+1F9FF).</summary>
    SupplementalSymbolsAndPictographs = 312,

    /// <summary>Chess Symbols (U+1FA00..U+1FA6F).</summary>
    ChessSymbols = 313,

    /// <summary>Symbols and Pictographs Extended-A (U+1FA70..U+1FAFF).</summary>
    SymbolsAndPictographsExtendedA = 314,

    /// <summary>Symbols for Legacy Computing (U+1FB00..U+1FBFF).</summary>
    SymbolsForLegacyComputing = 315,

    /// <summary>CJK Unified Ideographs Extension B (U+20000..U+2A6DF).</summary>
    CjkUnifiedIdeographsExtensionB = 316,

    /// <summary>CJK Unified Ideographs Extension C (U+2A700..U+2B73F).</summary>
    CjkUnifiedIdeographsExtensionC = 317,

    /// <summary>CJK Unified Ideographs Extension D (U+2B740..U+2B81F).</summary>
    CjkUnifiedIdeographsExtensionD = 318,

    /// <summary>CJK Unified Ideographs Extension E (U+2B820..U+2CEAF).</summary>
    CjkUnifiedIdeographsExtensionE = 319,

    /// <summary>CJK Unified Ideographs Extension F (U+2CEB0..U+2EBEF).</summary>
    CjkUnifiedIdeographsExtensionF = 320,

    /// <summary>CJK Unified Ideographs Extension I (U+2EBF0..U+2EE5F).</summary>
    CjkUnifiedIdeographsExtensionI = 321,

    /// <summary>CJK Compatibility Ideographs Supplement (U+2F800..U+2FA1F).</summary>
    CjkCompatibilityIdeographsSupplement = 322,

    /// <summary>CJK Unified Ideographs Extension G (U+30000..U+3134F).</summary>
    CjkUnifiedIdeographsExtensionG = 323,

    /// <summary>CJK Unified Ideographs Extension H (U+31350..U+323AF).</summary>
    CjkUnifiedIdeographsExtensionH = 324,

    /// <summary>Tags (U+E0000..U+E007F).</summary>
    Tags = 325,

    /// <summary>Variation Selectors Supplement (U+E0100..U+E01EF).</summary>
    VariationSelectorsSupplement = 326,

    /// <summary>Supplementary Private Use Area-A (U+F0000..U+FFFFF).</summary>
    SupplementaryPrivateUseAreaA = 327,

    /// <summary>Supplementary Private Use Area-B (U+100000..U+10FFFF).</summary>
    SupplementaryPrivateUseAreaB = 328,
}
