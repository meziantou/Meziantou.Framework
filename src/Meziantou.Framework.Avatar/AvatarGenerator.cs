using System.Diagnostics;
using System.IO.Hashing;
using System.Security;
using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework;

/// <summary>
/// Generates avatars from names and options, as SVG strings or as scenes of <c>Meziantou.Framework.VectorGraphics</c>.
/// </summary>
public static class AvatarGenerator
{
    private const double FontSizeRatio = 0.5;
    private const double RoundedCornerRadiusRatio = 0.25;

    /// <summary>
    /// The widest the initials of a scene can be, as a ratio of the size. A circle is still 0.93 times the size wide at
    /// the top and the bottom of capital letters, so wider initials are drawn with a smaller font.
    /// </summary>
    private const double MaxTextWidthRatio = 0.8;

    /// <summary>
    /// The height of the capital letters of Helvetica Bold, in em (<c>CapHeight</c> of its AFM metrics). The ascender
    /// that <see cref="AfmTextMeasurer"/> reports is this height, not the top of a line box like the ascent of a font file.
    /// </summary>
    private const double HelveticaBoldCapHeight = 0.718;

    /// <summary>Optical nudge that centers the glyph on the baseline.</summary>
    private const string BaselineOffset = ".05em";

    /// <summary>Every generated coordinate is a multiple of 0.25, so two decimals are always exact.</summary>
    private const string NumberFormat = "0.##";

    /// <summary>The observed length of a default-options document is 353 characters.</summary>
    private const int SvgCapacity = 512;

    /// <summary>Rendered when a name contains nothing that can be displayed.</summary>
    private const string PlaceholderBigram = "?";

    /// <summary>
    /// A grapheme cluster has no length limit, so a name made of combining marks would otherwise
    /// flow into the output unbounded. The longest legitimate cluster (a family emoji) is 11 UTF-16 units.
    /// </summary>
    private const int MaxBigramElementLength = 64;

    /// <summary>
    /// Creates an avatar SVG string for the specified name, using the default options.
    /// </summary>
    /// <param name="name">The full name used to compute the color and the bigram.</param>
    /// <returns>The generated SVG string.</returns>
    public static string CreateSvg(string name)
    {
        return CreateSvg(name, AvatarOptions.Default);
    }

    /// <summary>
    /// Creates an avatar SVG string for the specified name and options.
    /// </summary>
    /// <param name="name">The full name used to compute the color and default bigram.</param>
    /// <param name="options">The generation options.</param>
    /// <returns>The generated SVG string.</returns>
    /// <remarks>
    /// Characters that are not valid in an XML document are removed from <paramref name="name"/>.
    /// An explicit <see cref="AvatarOptions.Bigram"/> containing such characters is rejected instead.
    /// </remarks>
    public static string CreateSvg(string name, AvatarOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Size, $"{nameof(options)}.{nameof(options.Size)}");
        ValidatePalette(options);
        ValidateShape(options);

        var sanitizedName = RemoveInvalidXmlCharacters(name);
        var bigram = GetBigram(sanitizedName, options);
        var colorPair = GetColorPair(sanitizedName, options.Palette);
        return CreateSvg(options, bigram, colorPair);
    }

    /// <summary>
    /// Creates the avatar of the specified name as a scene, using the default options.
    /// </summary>
    /// <param name="name">The full name used to compute the color and the bigram.</param>
    /// <returns>The scene of the avatar.</returns>
    /// <remarks>
    /// The scene can be written as SVG with <c>SvgSceneRenderer</c>, or drawn as an image (PNG, WebP, JPEG...) with
    /// <c>Meziantou.Framework.Imaging.VectorGraphics</c>.
    /// </remarks>
    public static Scene CreateScene(string name)
    {
        return CreateScene(name, AvatarOptions.Default, textMeasurer: null);
    }

    /// <summary>
    /// Creates the avatar of the specified name as a scene.
    /// </summary>
    /// <param name="name">The full name used to compute the color and default bigram.</param>
    /// <param name="options">The generation options.</param>
    /// <returns>The scene of the avatar.</returns>
    /// <remarks>
    /// The scene can be written as SVG with <c>SvgSceneRenderer</c>, or drawn as an image (PNG, WebP, JPEG...) with
    /// <c>Meziantou.Framework.Imaging.VectorGraphics</c>.
    /// </remarks>
    public static Scene CreateScene(string name, AvatarOptions options)
    {
        return CreateScene(name, options, textMeasurer: null);
    }

    /// <summary>
    /// Creates the avatar of the specified name as a scene, laid out with the specified fonts.
    /// </summary>
    /// <param name="name">The full name used to compute the color and default bigram.</param>
    /// <param name="options">The generation options.</param>
    /// <param name="textMeasurer">
    /// The measurer of the fonts that draw the scene, or <see langword="null"/> for <see cref="AfmTextMeasurer.Default"/>.
    /// The initials are centered and sized with its metrics, so pass the measurer of the fonts that draw the image.
    /// </param>
    /// <returns>The scene of the avatar.</returns>
    /// <remarks>
    /// <para>
    /// The avatar is the same as the one of <see cref="CreateSvg(string, AvatarOptions)"/>: same shape, colors and bigram.
    /// The initials are drawn in the bold font of the measurer, centered vertically, and with a smaller font when they
    /// would be wider than the shape. The fonts of the measurer (<see cref="ITextMeasurer.Fonts"/>) become the fonts of the
    /// scene, so the renderers draw the initials with the fonts that measured them, including the fallback fonts of the
    /// characters the main font does not have. The title of the scene is the accessible label, or nothing when
    /// <see cref="AvatarOptions.IsDecorative"/> is <see langword="true"/>.
    /// </para>
    /// <para>
    /// The colors of every pair of the palette must be colors that <see cref="SceneColor.Parse(string)"/> accepts:
    /// <c>#rrggbb</c>, <c>rgb()</c>, <c>hsl()</c>, a CSS color name... CSS variables and <c>currentColor</c> are only
    /// supported by <see cref="CreateSvg(string, AvatarOptions)"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Avatar.Tests/AvatarGeneratorTests.cs" ref:region="AvatarGenerator_CreatePng" ref:dedent="true">
    /// // Lay the avatar out with the fonts that draw the image, so the initials are centered
    /// var measurer = SceneImageRenderer.CreateMeasurer();
    /// var scene = AvatarGenerator.CreateScene("John Doe", new AvatarOptions { Size = 128 }, measurer);
    ///
    /// using var image = scene.ToImage();
    /// Assert.Equal((128, 128), (image.Width, image.Height));
    ///
    /// // Save to a file (image.Save("avatar.png")) or to a stream, in any format
    /// using var png = new MemoryStream();
    /// image.Save(png, new PngEncoder());
    /// </code>
    /// </example>
    public static Scene CreateScene(string name, AvatarOptions options, ITextMeasurer? textMeasurer)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Size, $"{nameof(options)}.{nameof(options.Size)}");
        ValidatePalette(options);
        ValidateShape(options);
        var colors = ParseSceneColors(options);

        var sanitizedName = RemoveInvalidXmlCharacters(name);
        var bigram = GetBigram(sanitizedName, options);
        var (backgroundColor, foregroundColor) = colors[GetColorIndex(sanitizedName, options.Palette.Count)];
        textMeasurer ??= AfmTextMeasurer.Default;

        var size = (double)options.Size;
        var center = size / 2;
        var scene = new Scene(size, size, textMeasurer.FontFamily)
        {
            Fonts = textMeasurer.Fonts,
            Title = options.IsDecorative ? null : options.AccessibleLabel is null ? bigram : RemoveInvalidXmlCharacters(options.AccessibleLabel),
        };

        scene.Root.Add<SceneShape>(options.Shape switch
        {
            AvatarShape.Round => new SceneCircle(center, center, center) { Fill = backgroundColor },
            AvatarShape.Square => new SceneRectangle(0, 0, size, size) { Fill = backgroundColor },
            AvatarShape.RoundedSquare => new SceneRectangle(0, 0, size, size) { Fill = backgroundColor, CornerRadius = size * RoundedCornerRadiusRatio },
            _ => throw new UnreachableException($"The shape '{options.Shape}' was not rejected by {nameof(ValidateShape)}."),
        });

        var font = new SceneFont(size * FontSizeRatio, Weight: SceneFont.BoldWeight);
        var metrics = textMeasurer.Measure(bigram, font);
        var maxWidth = size * MaxTextWidthRatio;
        if (metrics.Width > maxWidth)
        {
            font = font with { Size = font.Size * maxWidth / metrics.Width };
            metrics = textMeasurer.Measure(bigram, font);
        }

        // The metrics of a font file describe its line box, which is centered. The metrics of Helvetica describe its
        // capital letters, so they are centered instead: browsers draw the SVG with Helvetica or Arial, whose line
        // boxes differ between platforms but whose capital letters have the same height.
        var baseline = textMeasurer is AfmTextMeasurer
            ? center + (font.Size * HelveticaBoldCapHeight / 2)
            : center + ((metrics.Ascent - metrics.Descent) / 2);
        scene.Root.Add(new SceneText(center, baseline, bigram, font, foregroundColor)
        {
            Anchor = SceneTextAnchor.Middle,
            MeasuredWidth = metrics.Width,
        });

        return scene;
    }

    private static string CreateSvg(AvatarOptions options, string bigram, AvatarColorPair colorPair)
    {
        var size = options.Size;
        var escapedBigram = Escape(bigram);
        var escapedBackgroundColor = Escape(colorPair.BackgroundColor);
        var escapedForegroundColor = Escape(colorPair.ForegroundColor);
        var sizeString = size.ToString(CultureInfo.InvariantCulture);
        var halfSizeString = (size / 2d).ToString(NumberFormat, CultureInfo.InvariantCulture);
        var roundedCornerRadiusString = (size * RoundedCornerRadiusRatio).ToString(NumberFormat, CultureInfo.InvariantCulture);
        var fontSizeString = (size * FontSizeRatio).ToString(NumberFormat, CultureInfo.InvariantCulture);

        var sb = new StringBuilder(capacity: SvgCapacity);
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"");
        sb.Append(sizeString);
        sb.Append("\" height=\"");
        sb.Append(sizeString);
        sb.Append("\" viewBox=\"0 0 ");
        sb.Append(sizeString);
        sb.Append(' ');
        sb.Append(sizeString);

        if (options.IsDecorative)
        {
            sb.Append("\" aria-hidden=\"true\" focusable=\"false\">");
        }
        else
        {
            sb.Append("\" role=\"img\" aria-label=\"");
            sb.Append(options.AccessibleLabel is null ? escapedBigram : Escape(RemoveInvalidXmlCharacters(options.AccessibleLabel)));
            sb.Append("\">");
        }

        switch (options.Shape)
        {
            case AvatarShape.Round:
                sb.Append("<circle cx=\"");
                sb.Append(halfSizeString);
                sb.Append("\" cy=\"");
                sb.Append(halfSizeString);
                sb.Append("\" r=\"");
                sb.Append(halfSizeString);
                sb.Append("\" fill=\"");
                sb.Append(escapedBackgroundColor);
                sb.Append("\"/>");
                break;

            case AvatarShape.Square:
                sb.Append("<rect width=\"");
                sb.Append(sizeString);
                sb.Append("\" height=\"");
                sb.Append(sizeString);
                sb.Append("\" fill=\"");
                sb.Append(escapedBackgroundColor);
                sb.Append("\"/>");
                break;

            case AvatarShape.RoundedSquare:
                sb.Append("<rect width=\"");
                sb.Append(sizeString);
                sb.Append("\" height=\"");
                sb.Append(sizeString);
                sb.Append("\" rx=\"");
                sb.Append(roundedCornerRadiusString);
                sb.Append("\" ry=\"");
                sb.Append(roundedCornerRadiusString);
                sb.Append("\" fill=\"");
                sb.Append(escapedBackgroundColor);
                sb.Append("\"/>");
                break;

            default:
                throw new UnreachableException($"The shape '{options.Shape}' was not rejected by {nameof(ValidateShape)}.");
        }

        sb.Append("<text x=\"50%\" y=\"50%\" text-anchor=\"middle\" dominant-baseline=\"middle\" alignment-baseline=\"middle\" dy=\"");
        sb.Append(BaselineOffset);
        sb.Append("\" fill=\"");
        sb.Append(escapedForegroundColor);
        sb.Append("\" font-family=\"monospace\" font-weight=\"700\" font-size=\"");
        sb.Append(fontSizeString);
        sb.Append("\">");
        sb.Append(escapedBigram);
        sb.Append("</text></svg>");

        return sb.ToString();
    }

    private static void ValidatePalette(AvatarOptions options)
    {
        var palette = options.Palette;
        if (palette.Count == 0)
            throw new ArgumentException("The palette cannot be empty.", nameof(options));

        for (var i = 0; i < palette.Count; i++)
        {
            var pair = palette[i];

            // AvatarColorPair is a struct, so default(AvatarColorPair) bypasses the constructor guards.
            if (string.IsNullOrWhiteSpace(pair.BackgroundColor) || string.IsNullOrWhiteSpace(pair.ForegroundColor))
                throw new ArgumentException($"The palette entry at index {i.ToString(CultureInfo.InvariantCulture)} has a null or whitespace color.", nameof(options));
        }
    }

    private static void ValidateShape(AvatarOptions options)
    {
        if (options.Shape is not (AvatarShape.Round or AvatarShape.Square or AvatarShape.RoundedSquare))
            throw new ArgumentOutOfRangeException(nameof(options), options.Shape, "Unsupported avatar shape.");
    }

    private static (SceneColor Background, SceneColor Foreground)[] ParseSceneColors(AvatarOptions options)
    {
        var palette = options.Palette;
        var result = new (SceneColor Background, SceneColor Foreground)[palette.Count];
        for (var i = 0; i < palette.Count; i++)
        {
            var pair = palette[i];
            if (!SceneColor.TryParse(pair.BackgroundColor, out var background) || !SceneColor.TryParse(pair.ForegroundColor, out var foreground))
                throw new ArgumentException($"The palette entry at index {i.ToString(CultureInfo.InvariantCulture)} has a color that a scene does not support. Use #rgb, #rrggbb, #rrggbbaa, rgb(), hsl() or a CSS color name.", nameof(options));

            result[i] = (background, foreground);
        }

        return result;
    }

    private static AvatarColorPair GetColorPair(string sanitizedName, IList<AvatarColorPair> palette)
    {
        return palette[GetColorIndex(sanitizedName, palette.Count)];
    }

    private static int GetColorIndex(string sanitizedName, int paletteCount)
    {
        // Normalization requires ICU. Under InvariantGlobalization it is a no-op, so a name that is not
        // already in Form C selects a different entry there. See the readme.
        var normalizedName = sanitizedName.Trim().Normalize(NormalizationForm.FormC);
        var hash = XxHash32.HashToUInt32(Encoding.UTF8.GetBytes(normalizedName));
        return (int)(hash % (uint)paletteCount);
    }

    private static string GetBigram(string sanitizedName, AvatarOptions options)
    {
        if (options.Bigram is not null)
            return ValidateBigram(options);

        string? firstElement = null;
        string? lastElement = null;
        string? singleWord = null;
        var wordCount = 0;

        foreach (var word in EnumerateWords(sanitizedName))
        {
            var element = GetFirstVisibleTextElement(word);
            if (element is null)
                continue;

            wordCount++;
            if (firstElement is null)
            {
                firstElement = element;
                singleWord = word;
            }

            lastElement = element;
        }

        if (wordCount == 0)
            return PlaceholderBigram;

        if (wordCount > 1)
            return string.Concat(firstElement, lastElement);

        return TakeFirstVisibleTextElements(singleWord!, maxTextElements: 2);
    }

    private static string ValidateBigram(AvatarOptions options)
    {
        var bigram = options.Bigram!.Trim();
        if (bigram.Length != RemoveInvalidXmlCharacters(bigram).Length)
            throw new ArgumentException("The explicit bigram must not contain characters that are invalid in an XML document.", nameof(options));

        var textElementCount = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(bigram);
        while (enumerator.MoveNext())
        {
            var textElement = enumerator.GetTextElement();
            if (ContainsWhiteSpace(textElement))
                throw new ArgumentException("The explicit bigram must not contain whitespace.", nameof(options));

            textElementCount++;
            if (textElementCount > 2)
                throw new ArgumentException("The explicit bigram must contain 1 or 2 characters.", nameof(options));
        }

        if (textElementCount == 0)
            throw new ArgumentException("The explicit bigram must contain 1 or 2 characters.", nameof(options));

        return bigram;
    }

    /// <summary>
    /// Splits a name on whitespace and on the connectors used inside compound names, so that
    /// "Jean-Pierre" and "O'Brien" yield two words rather than one.
    /// </summary>
    private static IEnumerable<string> EnumerateWords(string name)
    {
        var start = -1;
        for (var i = 0; i < name.Length; i++)
        {
            if (IsWordSeparator(name[i]))
            {
                if (start >= 0)
                {
                    yield return name[start..i];
                    start = -1;
                }
            }
            else if (start < 0)
            {
                start = i;
            }
        }

        if (start >= 0)
            yield return name[start..];
    }

    private static bool IsWordSeparator(char value)
    {
        return char.IsWhiteSpace(value)
            || value is '-' or '‐' or '‑' or '‒' or '–' or '—' // hyphen and dash variants
            or '\'' or '’' // straight and typographic apostrophes
            or '.' or '_';
    }

    private static string TakeFirstVisibleTextElements(string text, int maxTextElements)
    {
        string? result = null;
        var textElementCount = 0;

        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var textElement = enumerator.GetTextElement();
            if (!IsVisibleTextElement(textElement))
                continue;

            result = result is null ? LimitLength(textElement) : string.Concat(result, LimitLength(textElement));
            if (++textElementCount == maxTextElements)
                break;
        }

        return result ?? PlaceholderBigram;
    }

    private static string? GetFirstVisibleTextElement(string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var textElement = enumerator.GetTextElement();
            if (IsVisibleTextElement(textElement))
                return LimitLength(textElement);
        }

        return null;
    }

    /// <summary>
    /// Keeps a pathological grapheme cluster (a base character followed by thousands of combining
    /// marks) from flowing into the output by reducing it to its base character.
    /// </summary>
    private static string LimitLength(string textElement)
    {
        if (textElement.Length <= MaxBigramElementLength)
            return textElement;

        var enumerator = textElement.EnumerateRunes();
        return enumerator.MoveNext() ? enumerator.Current.ToString() : PlaceholderBigram;
    }

    /// <summary>
    /// Determines whether a grapheme cluster renders anything. Zero-width and bidi control characters
    /// are not whitespace, so without this check they can become the whole bigram.
    /// </summary>
    private static bool IsVisibleTextElement(string textElement)
    {
        foreach (var rune in textElement.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
                continue;

            switch (Rune.GetUnicodeCategory(rune))
            {
                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.SpacingCombiningMark:
                case UnicodeCategory.EnclosingMark:
                case UnicodeCategory.SpaceSeparator:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                case UnicodeCategory.Surrogate:
                case UnicodeCategory.OtherNotAssigned:
                    continue;

                default:
                    return true;
            }
        }

        return false;
    }

    private static bool ContainsWhiteSpace(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Removes the code points that the XML 1.0 Char production forbids, including unpaired surrogates.
    /// <see cref="SecurityElement.Escape(string)"/> passes them through, and <see cref="string.Normalize(NormalizationForm)"/>
    /// throws on some of them.
    /// </summary>
    private static string RemoveInvalidXmlCharacters(string value)
    {
        if (!ContainsInvalidXmlCharacter(value))
            return value;

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c))
            {
                // Any well-formed pair maps to U+10000..U+10FFFF, which the Char production allows.
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    sb.Append(c);
                    sb.Append(value[i + 1]);
                    i++;
                }

                continue;
            }

            if (!char.IsLowSurrogate(c) && IsValidXmlCharacter(c))
                sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool ContainsInvalidXmlCharacter(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                    continue;
                }

                return true;
            }

            if (char.IsLowSurrogate(c) || !IsValidXmlCharacter(c))
                return true;
        }

        return false;
    }

    private static bool IsValidXmlCharacter(char value)
    {
        return value is '\t' or '\n' or '\r' or (>= '\u0020' and <= '\ud7ff') or (>= '\ue000' and <= '\ufffd');
    }

    private static string Escape(string value)
    {
        return SecurityElement.Escape(value);
    }
}
