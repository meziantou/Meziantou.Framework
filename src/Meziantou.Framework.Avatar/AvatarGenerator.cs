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
            sb.Append("\" />");
        }
        else if (shape == AvatarShape.Square)
        {
            sb.Append("<rect width=\"");
            sb.Append(sizeString);
            sb.Append("\" height=\"");
            sb.Append(sizeString);
            sb.Append("\" fill=\"");
            sb.Append(escapedBackgroundColor);
            sb.Append("\" />");
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unsupported avatar shape.");
        }

        sb.Append("<text x=\"50%\" y=\"50%\" text-anchor=\"middle\" dominant-baseline=\"middle\" alignment-baseline=\"middle\" dy=\"0.05em\" fill=\"");
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
            var first = GetFirstRune(words[0]);
            var last = GetFirstRune(words[^1]);
            return string.Concat(first, last);
        }

        return TakeFirstRunes(words[0], maxRunes: 2);
    }

    private static string ValidateBigram(string bigram)
    {
        var runeCount = 0;
        foreach (var rune in bigram.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
                throw new ArgumentException("The explicit bigram must not contain whitespace.", nameof(bigram));

            runeCount++;
            if (runeCount > 2)
                throw new ArgumentException("The explicit bigram must contain 1 or 2 characters.", nameof(bigram));
        }

        if (runeCount == 0)
            throw new ArgumentException("The explicit bigram must contain 1 or 2 characters.", nameof(bigram));

        return bigram;
    }

    private static string TakeFirstRunes(string text, int maxRunes)
    {
        var sb = new StringBuilder(capacity: maxRunes * 2);
        var index = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            sb.Append(rune);
            index++;
            if (index == maxRunes)
                break;
        }

        if (sb.Length == 0)
            throw new ArgumentException("The value must contain at least one character.", nameof(text));

        return sb.ToString();
    }

    private static Rune GetFirstRune(string text)
    {
        foreach (var rune in text.EnumerateRunes())
            return rune;

        throw new ArgumentException("The value must contain at least one character.", nameof(text));
    }

    private static string Escape(string value)
    {
        return SecurityElement.Escape(value) ?? string.Empty;
    }
}
