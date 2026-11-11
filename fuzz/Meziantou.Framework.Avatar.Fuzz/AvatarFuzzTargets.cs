using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Avatar.Fuzz;

/// <summary>
/// <see cref="AvatarGenerator.CreateSvg(string, AvatarOptions)"/> and <see cref="AvatarGenerator.CreateScene(string, AvatarOptions, ITextMeasurer)"/>
/// accept and reject the same names and options, draw the same bigram with the same colors, and give well-formed SVG
/// whose initials fit the avatar, whatever the name holds.
/// </summary>
public static partial class AvatarFuzzTargets
{
    private static readonly string[] NameTokens =
    [
        " ", "\t", "\n", "-", "'", "\u2019", ".", "_", "\u2010", "\u2014", "\u00A0", "\u200B", "\u200D", "\u200E", "\u202E", "\uFEFF",
        "\u0301", "\u0308", "\U0001F600", "\U0001F468\u200D\U0001F469\u200D\U0001F467", "\U0001F1EB\U0001F1F7", "\u5C71\u7530", "\u05E9",
        "\0", "\u0007", "\uD800", "\uDC00", "\uFFFE", "<", ">", "&", "\"", "]]>", "John", "Doe", "W", "M", "e\u0301",
    ];

    private static readonly int[] Sizes = [64, 1, 2, 3, 7, 16, 32, 33, 100, 128, 255, 1000];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("avatar-generator", GeneratorTarget) { Seeds = GetSeeds, Dictionary = NameTokens },
    ];

    /// <summary>
    /// The input is a byte of options (shape, size, decorative, accessible label, explicit bigram), then the name, then,
    /// after a NUL byte, the explicit bigram or the accessible label. <see cref="AvatarGenerator.CreateSvg(string, AvatarOptions)"/>
    /// and <see cref="AvatarGenerator.CreateScene(string, AvatarOptions, ITextMeasurer)"/> both throw an
    /// <see cref="ArgumentException"/>, or both succeed. Then the SVG of both is well-formed XML, the scene draws the
    /// bigram and the colors of the SVG, its initials are at most 80% of the size wide, and its title is the accessible label.
    /// </summary>
    private static void GeneratorTarget(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var name = FuzzText.Decode(FuzzInput.ReadUntil(ref data, 0));
        var extra = FuzzText.Decode(data);
        var options = new AvatarOptions
        {
            Shape = (AvatarShape)(flags % 3),
            Size = Sizes[(flags >> 2) % Sizes.Length],
            IsDecorative = (flags & 0x40) != 0,
            Bigram = (flags & 0x80) != 0 ? extra : null,
            AccessibleLabel = (flags & 0x80) == 0 && extra.Length > 0 ? extra : null,
        };

        var svgException = Capture(() => AvatarGenerator.CreateSvg(name, options), out var svg);
        var sceneException = Capture(() => AvatarGenerator.CreateScene(name, options), out var scene);
        FuzzAssert.Equal(svgException?.GetType(), sceneException?.GetType(), $"CreateSvg and CreateScene disagree on name {FuzzAssert.Format(name)} and {FuzzAssert.Format(extra)}: {svgException?.Message} / {sceneException?.Message}");
        if (svg is null || scene is null)
            return;

        var document = LoadXml(svg, "CreateSvg");
        _ = LoadXml(SvgSceneRenderer.Render(scene), "The SVG of the scene");

        var textElement = document.DocumentElement!.LastChild!;
        var text = (SceneText)scene.Root.Children[1];
        FuzzAssert.Equal(textElement.InnerText, text.Text, $"The bigrams of {FuzzAssert.Format(name)} differ");
        FuzzAssert.Equal(SceneColor.Parse(textElement.Attributes!["fill"]!.Value), text.Color, $"The foreground colors of {FuzzAssert.Format(name)} differ");
        var background = (SolidPaint)((SceneShape)scene.Root.Children[0]).Fill!;
        FuzzAssert.Equal(SceneColor.Parse(document.DocumentElement.FirstChild!.Attributes!["fill"]!.Value), background.Color, $"The background colors of {FuzzAssert.Format(name)} differ");

        FuzzAssert.True(text.MeasuredWidth <= (options.Size * 0.8) + 1e-9, $"The initials {FuzzAssert.Format(text.Text)} are {text.MeasuredWidth} wide in an avatar of {options.Size}");
        FuzzAssert.True(text.Font.Size > 0 && text.Font.Size <= options.Size * 0.5, $"The font size of {FuzzAssert.Format(text.Text)} is {text.Font.Size}");

        // An XML parser replaces the tabs and line breaks of an attribute with spaces
        var label = options.IsDecorative ? null : document.DocumentElement.GetAttribute("aria-label");
        FuzzAssert.Equal(label, scene.Title?.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '), $"The title of the scene of {FuzzAssert.Format(name)} is not the accessible label");
    }

    private static ArgumentException? Capture<T>(Func<T> action, out T? result)
        where T : class
    {
        try
        {
            result = action();
            return null;
        }
        catch (ArgumentException exception)
        {
            result = null;
            return exception;
        }
    }

    private static XmlDocument LoadXml(string svg, string source)
    {
        var document = new XmlDocument { XmlResolver = null };
        try
        {
            using var reader = XmlReader.Create(new StringReader(svg), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document.Load(reader);
        }
        catch (XmlException exception)
        {
            FuzzAssert.Fail($"{source} is not well-formed: {exception.Message}");
        }

        return document;
    }

    // A regular C# string literal
    [GeneratedRegex(""""(?<!@)"(?<value>(?:[^"\\\n]|\\.)*)"""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex StringLiteral();

    private static string Unescape(string literal)
    {
        var builder = new StringBuilder(literal.Length);
        for (var i = 0; i < literal.Length; i++)
        {
            if (literal[i] is not '\\' || i + 1 >= literal.Length)
            {
                builder.Append(literal[i]);
                continue;
            }

            i++;
            switch (literal[i])
            {
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case '0':
                    builder.Append('\0');
                    break;
                case 'u' when i + 4 < literal.Length:
                    builder.Append((char)int.Parse(literal.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                case 'U' when i + 8 < literal.Length:
                    builder.Append(char.ConvertFromUtf32(int.Parse(literal.AsSpan(i + 1, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                    i += 8;
                    break;
                default:
                    builder.Append(literal[i]);
                    break;
            }
        }

        return builder.ToString();
    }

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        yield return FuzzSeed.FromText("default", [0], "John Doe");
        yield return FuzzSeed.FromText("explicit-bigram", [0x80 | 1], "John Doe\0JD");
        yield return FuzzSeed.FromText("accessible-label", [2 | (9 << 2)], "Ada Lovelace\0<Ada> & \u0007Lovelace");
        yield return FuzzSeed.FromText("decorative-wide", [0x40 | (4 << 2)], "Wolfgang Mozart");

        // The names and bigrams of the unit tests, with each option byte
        var source = FuzzResources.ReadText(typeof(AvatarFuzzTargets).Assembly, "AvatarGeneratorTests.cs");
        var index = 0;
        foreach (Match match in StringLiteral().Matches(source))
        {
            var flags = (byte)(index * 37);
            yield return FuzzSeed.FromText("test-" + index.ToString(CultureInfo.InvariantCulture), [flags], Unescape(match.Groups["value"].Value));
            index++;
        }
    }
}
