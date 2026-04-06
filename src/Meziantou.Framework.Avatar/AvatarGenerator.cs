using System.Globalization;
using System.IO.Hashing;
using System.Security;
using System.Text;

namespace Meziantou.Framework;

/// <summary>
/// Generates SVG avatars from names and options.
/// </summary>
public static class AvatarGenerator
{
    /// <summary>
    /// Creates an avatar SVG string for the specified name and options.
    /// </summary>
    /// <param name="name">The full name used to compute the color and default bigram.</param>
    /// <param name="options">The generation options.</param>
    /// <returns>The generated SVG string.</returns>
    public static string CreateSvg(string name, AvatarOptions options)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("The name cannot be empty or whitespace.", nameof(name));

        if (options.Size <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "The option Size must be greater than 0.");

        if (options.Palette.Count == 0)
            throw new ArgumentException("The palette cannot be empty.", nameof(options));

        var bigram = GetBigram(name, options.Bigram);
        var colorPair = GetColorPair(name, options.Palette);
        return CreateSvg(options.Size, options.Shape, bigram, colorPair);
    }

    private static string CreateSvg(int size, AvatarShape shape, string bigram, AvatarColorPair colorPair)
    {
        var escapedBigram = Escape(bigram);
        var escapedBackgroundColor = Escape(colorPair.BackgroundColor);
        var escapedForegroundColor = Escape(colorPair.ForegroundColor);
        var halfSize = size / 2d;
        var fontSize = size * 0.5;
        var sizeString = size.ToString(CultureInfo.InvariantCulture);
        var halfSizeString = halfSize.ToString("0.##", CultureInfo.InvariantCulture);
        var fontSizeString = fontSize.ToString("0.##", CultureInfo.InvariantCulture);

        var sb = new StringBuilder(capacity: 256);
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"");
        sb.Append(sizeString);
        sb.Append("\" height=\"");
        sb.Append(sizeString);
        sb.Append("\" viewBox=\"0 0 ");
        sb.Append(sizeString);
        sb.Append(' ');
        sb.Append(sizeString);
        sb.Append("\" role=\"img\" aria-label=\"");
        sb.Append(escapedBigram);
        sb.Append("\">");

        if (shape == AvatarShape.Round)
        {
            sb.Append("<circle cx=\"");
            sb.Append(halfSizeString);
            sb.Append("\" cy=\"");
            sb.Append(halfSizeString);
            sb.Append("\" r=\"");
            sb.Append(halfSizeString);
            sb.Append("\" fill=\"");
            sb.Append(escapedBackgroundColor);
            sb.Append("\"/>");
        }
        else if (shape == AvatarShape.Square)
        {
            sb.Append("<rect width=\"");
            sb.Append(sizeString);
            sb.Append("\" height=\"");
            sb.Append(sizeString);
            sb.Append("\" fill=\"");
            sb.Append(escapedBackgroundColor);
            sb.Append("\"/>");
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unsupported avatar shape.");
        }

        sb.Append("<text x=\"50%\" y=\"50%\" text-anchor=\"middle\" dominant-baseline=\"middle\" alignment-baseline=\"middle\" dy=\".05em\" fill=\"");
        sb.Append(escapedForegroundColor);
        sb.Append("\" font-family=\"monospace\" font-weight=\"700\" font-size=\"");
        sb.Append(fontSizeString);
        sb.Append("\">");
        sb.Append(escapedBigram);
        sb.Append("</text></svg>");

        return sb.ToString();
    }

    private static AvatarColorPair GetColorPair(string name, IList<AvatarColorPair> palette)
    {
        var normalizedName = name.Trim().Normalize(NormalizationForm.FormC);
        var hash = XxHash32.HashToUInt32(Encoding.UTF8.GetBytes(normalizedName));
        var index = (int)(hash % (uint)palette.Count);
        return palette[index];
    }

    private static string GetBigram(string name, string? explicitBigram)
    {
        if (explicitBigram is not null)
            return ValidateBigram(explicitBigram.Trim());

        var words = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
            throw new ArgumentException("The name cannot be empty or whitespace.", nameof(name));

        if (words.Length > 1)
        {
            var first = GetFirstTextElement(words[0]);
            var last = GetFirstTextElement(words[^1]);
            return string.Concat(first, last);
        }

        return TakeFirstTextElements(words[0], maxTextElements: 2);
    }

    private static string ValidateBigram(string bigram)
    {
        var textElementCount = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(bigram);
        while (enumerator.MoveNext())
        {
            var textElement = enumerator.GetTextElement();
            if (ContainsWhiteSpace(textElement))
                throw new ArgumentException("The explicit bigram must not contain whitespace.", nameof(bigram));

            textElementCount++;
            if (textElementCount > 2)
                throw new ArgumentException("The explicit bigram must contain 1 or 2 characters.", nameof(bigram));
        }

        if (textElementCount == 0)
            throw new ArgumentException("The explicit bigram must contain 1 or 2 characters.", nameof(bigram));

        return bigram;
    }

    private static string TakeFirstTextElements(string text, int maxTextElements)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        if (!enumerator.MoveNext())
            throw new ArgumentException("The value must contain at least one character.", nameof(text));

        var textElementCount = 1;
        while (textElementCount < maxTextElements && enumerator.MoveNext())
        {
            textElementCount++;
        }

        if (textElementCount < maxTextElements)
            return text;

        var endIndex = text.Length;
        if (enumerator.MoveNext())
            endIndex = enumerator.ElementIndex;

        return text[..endIndex];
    }

    private static string GetFirstTextElement(string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        if (!enumerator.MoveNext())
            throw new ArgumentException("The value must contain at least one character.", nameof(text));

        var startIndex = enumerator.ElementIndex;
        var endIndex = text.Length;
        if (enumerator.MoveNext())
            endIndex = enumerator.ElementIndex;

        return text[startIndex..endIndex];
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

    private static string Escape(string value)
    {
        return SecurityElement.Escape(value) ?? string.Empty;
    }
}
