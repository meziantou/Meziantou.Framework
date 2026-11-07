using System.Diagnostics;
using System.IO.Hashing;
using System.Security;

namespace Meziantou.Framework;

/// <summary>
/// Generates SVG avatars from names and options.
/// </summary>
public static class AvatarGenerator
{
    private const double FontSizeRatio = 0.5;
    private const double RoundedCornerRadiusRatio = 0.25;

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

    private static AvatarColorPair GetColorPair(string sanitizedName, IList<AvatarColorPair> palette)
    {
        // Normalization requires ICU. Under InvariantGlobalization it is a no-op, so a name that is not
        // already in Form C selects a different entry there. See the readme.
        var normalizedName = sanitizedName.Trim().Normalize(NormalizationForm.FormC);
        var hash = XxHash32.HashToUInt32(Encoding.UTF8.GetBytes(normalizedName));
        var index = (int)(hash % (uint)palette.Count);
        return palette[index];
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
