using Meziantou.Framework.WebPlatform.Css.Fonts;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The fonts that the runner installs for the pages of the tests, in addition to the fonts bundled with the library (see
/// src/Meziantou.Framework.WebPlatform/docs/web-platform-tests.md#fonts). They are fetched with the test suites (<c>eng/webplatform/fetch-tests.sh</c>): they are not part
/// of the library.
/// <list type="bullet">
/// <item>The fonts of the CSS test suite (<c>fonts/CSSTest</c> and <c>css/css-fonts/variations/resources/csstest-*.ttf</c>
/// of the web-platform-tests: <c>CSSTest Verify</c>, <c>CSSTest FamilyName</c>, <c>CSSTest Weights Full</c>...), which the
/// tests of the font matching expect to be installed on the system.</item>
/// <item>Fallback fonts for the scripts that the bundled fonts (Latin, Greek and Cyrillic) do not cover, each one limited to
/// the blocks of its scripts: they are the fonts of last resort of these characters only, so the default font and the
/// text of the scripts of the bundled fonts do not change. The fonts of the web-platform-tests are used when they have one
/// for the script, else Noto fonts (<c>tests/Meziantou.Framework.WebPlatform.External/fonts</c>, the <c>fonts</c> source of tests/Meziantou.Framework.WebPlatform.SpecData/sources.json).</item>
/// </list>
/// </summary>
internal static class WptFonts
{
    /// <summary>The fallback fonts (paths in tests/Meziantou.Framework.WebPlatform.External) and the ranges of the characters they render.</summary>
    private static readonly (string Path, string UnicodeRange)[] FallbackFonts =
    [
        // M+ 1p: the kana, the ideographs of Japanese, the CJK punctuation and the fullwidth forms
        ("wpt/fonts/mplus-1p-regular.woff", "U+2E80-2FDF, U+3000-30FF, U+31F0-31FF, U+3200-33FF, U+3400-4DBF, U+4E00-9FFF, U+F900-FAFF, U+FE10-FE1F, U+FE30-FE4F, U+FF00-FFEF, U+1B000-1B16F, U+20000-2FA1F"),

        // Scheherazade: Arabic. It has no tabular alternates of the digits (which the markers of the list items use,
        // font-variant-numeric: tabular-nums), unlike Noto Naskh Arabic: the tests of the counter styles compare a marker
        // with the same digits in a text
        ("wpt/fonts/Scheherazade-Regular.woff", "U+0600-06FF, U+0750-077F, U+0870-08FF, U+FB50-FDFF, U+FE70-FEFE"),

        // Noto Sans Devanagari
        ("wpt/fonts/noto/NotoSansDevanagari-Regular.ttf", "U+0900-097F, U+1CD0-1CFF, U+A8E0-A8FF"),

        // Noto Sans Thai, Noto Sans Lao, Noto Serif Tibetan, Noto Sans Myanmar
        ("fonts/ofl/notosansthai/NotoSansThai[wdth,wght].ttf", "U+0E00-0E7F"),
        ("fonts/ofl/notosanslao/NotoSansLao[wdth,wght].ttf", "U+0E80-0EFF"),
        ("fonts/ofl/notoseriftibetan/NotoSerifTibetan[wght].ttf", "U+0F00-0FFF"),
        ("fonts/ofl/notosansmyanmar/NotoSansMyanmar[wdth,wght].ttf", "U+1000-109F, U+A9E0-A9FF, U+AA60-AA7F"),

        // Noto Sans KR: the Hangul syllables and jamo
        ("fonts/ofl/notosanskr/NotoSansKR[wght].ttf", "U+1100-11FF, U+3130-318F, U+A960-A97F, U+AC00-D7FF"),
    ];

    /// <summary>The installed fonts of the browsing contexts of the tests (immutable once created: shared by all the tests).</summary>
    public static InstalledFontCollection Installed { get; } = Create();

    private static InstalledFontCollection Create()
    {
        var fonts = new InstalledFontCollection();
        var external = Path.Combine(WptPaths.Root, "tests", "Meziantou.Framework.WebPlatform.External");

        // The kerned versions of the `CSSTest Weights` fonts (the tests of css/css-fonts/variations measure the kerning of
        // `A` with a digit) are installed before the fonts of the same families and styles of fonts/CSSTest: the first face
        // installed with a style is the one of its family
        InstallDirectory(Path.Combine(external, "wpt", "css", "css-fonts", "variations", "resources"), "csstest-*.ttf");
        InstallDirectory(Path.Combine(external, "wpt", "fonts", "CSSTest"), "*.ttf");

        foreach (var (relativePath, unicodeRange) in FallbackFonts)
        {
            var path = Path.Combine(external, relativePath);
            if (!File.Exists(path))
                throw new InvalidOperationException($"The fallback font '{path}' of the runner is missing: run eng/webplatform/fetch-tests.sh");

            if (!fonts.AddFallback(File.ReadAllBytes(path), unicodeRange))
                throw new InvalidOperationException($"'{path}' is not a valid font file");
        }

        return fonts;

        void InstallDirectory(string directory, string pattern)
        {
            if (!Directory.Exists(directory))
                throw new InvalidOperationException($"The fonts of the CSS test suite are missing ('{directory}'): run eng/webplatform/fetch-tests.sh");

            // In the order of their names (csstest-familyname.ttf before csstest-familyname-bold.ttf and the "funky" variants,
            // which have the same localized family name and the same style)
            foreach (var path in Directory.EnumerateFiles(directory, pattern).OrderBy(Path.GetFileNameWithoutExtension, StringComparer.Ordinal))
            {
                if (!fonts.Add(File.ReadAllBytes(path)))
                    throw new InvalidOperationException($"'{path}' is not a valid font file");
            }
        }
    }
}
