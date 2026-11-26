namespace Meziantou.Framework.UnicodeBenchmarks;

/// <summary>The texts of the benchmarks: paragraphs repeated to about 64 KB, so the per-call overhead does not hide the per-character cost.</summary>
internal static class BenchmarkTexts
{
    private const int TargetLength = 64 * 1024;

    // Plain English prose: ASCII letters, digits, punctuation, quotes and line feeds
    private const string EnglishParagraph =
        "The quick brown fox jumps over the lazy dog. It was the best of times, it was the worst of times; " +
        "it was the age of wisdom, it was the age of foolishness. \"Hello,\" she said, \"how are you today?\" " +
        "Version 3.14 shipped on 2024-05-17 with 1,024 fixes (see issue #42) and a 12.5% speed-up! " +
        "Mr. Smith paid $9.99 for e-mail support at www.example.com/help?id=7.\n";

    // French prose: Latin letters with precomposed accents, typographic quotes and apostrophes, non-breaking spaces
    private const string FrenchParagraph =
        "L\u2019\u00E9t\u00E9 dernier, nous sommes all\u00E9s \u00E0 la for\u00EAt pr\u00E8s de la rivi\u00E8re. " +
        "\u00AB\u00A0Quelle journ\u00E9e magnifique\u00A0!\u00A0\u00BB s\u2019est exclam\u00E9e No\u00EBlle. " +
        "Les \u0153ufs co\u00FBtent 3,50\u00A0\u20AC la douzaine\u00A0; c\u2019est tr\u00E8s cher, n\u2019est-ce pas\u00A0? " +
        "Le gar\u00E7on a mang\u00E9 une cr\u00E8me br\u00FBl\u00E9e \u00E0 c\u00F4t\u00E9 de l\u2019h\u00F4tel.\n";

    // Chinese, Japanese, Arabic, Hebrew, Russian, Hindi, Thai, emoji sequences and decomposed Latin letters
    private const string MixedParagraph =
        "\u4E2D\u6587\u7684\u6587\u672C\uFF0C\u5305\u542B\u6807\u70B9\u7B26\u53F7\u3002\u65E5\u672C\u8A9E\u306E\u6587\u7AE0\u3067\u3059\u3002 " +
        "\u0627\u0644\u0639\u0631\u0628\u064A\u0629 \u0646\u0635 \u0645\u0646 \u0627\u0644\u064A\u0645\u064A\u0646 123 \u05E2\u05D1\u05E8\u05D9\u05EA (\u05D8\u05E7\u05E1\u05D8) " +
        "\u0420\u0443\u0441\u0441\u043A\u0438\u0439 \u0442\u0435\u043A\u0441\u0442, \u043F\u0440\u0438\u0432\u0435\u0442! " +
        "\u0939\u093F\u0928\u094D\u0926\u0940 \u0915\u094D\u0937\u0924\u094D\u0930\u093F\u092F \u092A\u093E\u0920 " +
        "\u0E20\u0E32\u0E29\u0E32\u0E44\u0E17\u0E22 " +
        "\U0001F468\u200D\U0001F469\u200D\U0001F467 \U0001F44D\U0001F3FD \U0001F1EB\U0001F1F7 caf\u00E9 na\u0308ive stra\u00DFe \uFB01nal.\n";

    // Thai prose, written without spaces between words and with a space between phrases: the line breaks need a dictionary
    private const string ThaiParagraph =
        "\u0E20\u0E32\u0E29\u0E32\u0E44\u0E17\u0E22\u0E40\u0E1B\u0E47\u0E19\u0E20\u0E32\u0E29\u0E32\u0E23\u0E32\u0E0A\u0E01\u0E32\u0E23\u0E02\u0E2D\u0E07\u0E1B\u0E23\u0E30\u0E40\u0E17\u0E28\u0E44\u0E17\u0E22 " +
        "\u0E41\u0E25\u0E30\u0E40\u0E1B\u0E47\u0E19\u0E20\u0E32\u0E29\u0E32\u0E41\u0E21\u0E48\u0E02\u0E2D\u0E07\u0E0A\u0E32\u0E27\u0E44\u0E17\u0E22\u0E2A\u0E48\u0E27\u0E19\u0E43\u0E2B\u0E0D\u0E48 " +
        "\u0E20\u0E32\u0E29\u0E32\u0E44\u0E17\u0E22\u0E40\u0E02\u0E35\u0E22\u0E19\u0E14\u0E49\u0E27\u0E22\u0E2D\u0E31\u0E01\u0E29\u0E23\u0E44\u0E17\u0E22\u0E0B\u0E36\u0E48\u0E07\u0E44\u0E21\u0E48\u0E21\u0E35\u0E01\u0E32\u0E23\u0E40\u0E27\u0E49\u0E19\u0E27\u0E23\u0E23\u0E04\u0E23\u0E30\u0E2B\u0E27\u0E48\u0E32\u0E07\u0E04\u0E33 " +
        "\u0E41\u0E15\u0E48\u0E08\u0E30\u0E40\u0E27\u0E49\u0E19\u0E27\u0E23\u0E23\u0E04\u0E40\u0E21\u0E37\u0E48\u0E2D\u0E08\u0E1A\u0E1B\u0E23\u0E30\u0E42\u0E22\u0E04\u0E2B\u0E23\u0E37\u0E2D\u0E27\u0E25\u0E35 " +
        "\u0E04\u0E19\u0E44\u0E17\u0E22\u0E19\u0E34\u0E22\u0E21\u0E01\u0E34\u0E19\u0E02\u0E49\u0E32\u0E27\u0E01\u0E31\u0E1A\u0E2D\u0E32\u0E2B\u0E32\u0E23\u0E2B\u0E25\u0E32\u0E22\u0E2D\u0E22\u0E48\u0E32\u0E07 " +
        "\u0E40\u0E0A\u0E48\u0E19 \u0E15\u0E49\u0E21\u0E22\u0E33\u0E01\u0E38\u0E49\u0E07 \u0E41\u0E01\u0E07\u0E40\u0E02\u0E35\u0E22\u0E27\u0E2B\u0E27\u0E32\u0E19 \u0E41\u0E25\u0E30\u0E1C\u0E31\u0E14\u0E44\u0E17\u0E22 " +
        "\u0E01\u0E23\u0E38\u0E07\u0E40\u0E17\u0E1E\u0E21\u0E2B\u0E32\u0E19\u0E04\u0E23\u0E40\u0E1B\u0E47\u0E19\u0E40\u0E21\u0E37\u0E2D\u0E07\u0E2B\u0E25\u0E27\u0E07\u0E41\u0E25\u0E30\u0E40\u0E21\u0E37\u0E2D\u0E07\u0E17\u0E35\u0E48\u0E43\u0E2B\u0E0D\u0E48\u0E17\u0E35\u0E48\u0E2A\u0E38\u0E14\u0E02\u0E2D\u0E07\u0E1B\u0E23\u0E30\u0E40\u0E17\u0E28 " +
        "\u0E21\u0E35\u0E1B\u0E23\u0E30\u0E0A\u0E32\u0E01\u0E23\u0E21\u0E32\u0E01\u0E01\u0E27\u0E48\u0E32\u0E2A\u0E34\u0E1A\u0E25\u0E49\u0E32\u0E19\u0E04\u0E19\n";

    public static string English { get; } = Repeat(EnglishParagraph);

    public static string French { get; } = Repeat(FrenchParagraph);

    public static string Mixed { get; } = Repeat(MixedParagraph);

    public static string Thai { get; } = Repeat(ThaiParagraph);

    public static string Get(string name) => name switch
    {
        "English" => English,
        "French" => French,
        "Mixed" => Mixed,
        "Thai" => Thai,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static string Repeat(string paragraph) => string.Concat(Enumerable.Repeat(paragraph, Math.Max(1, TargetLength / paragraph.Length)));
}
