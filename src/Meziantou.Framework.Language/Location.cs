namespace Meziantou.Framework.Language;

/// <summary>
/// Represents where something was found in source: a character range and, when it is known, the text that range
/// indexes into.
/// </summary>
/// <example>
/// <code>
/// var location = new Location(new TextSpan(15, 7), SourceText.From(text));
/// Console.WriteLine(location);                          // (2,7)-(2,14)
/// Console.WriteLine(location.GetLineSpan().Start.Line); // 2
/// </code>
/// </example>
public sealed class Location : IEquatable<Location>
{
    /// <summary>A location that points at no text.</summary>
    public static Location None { get; } = new(default, sourceText: null);

    public Location(TextSpan sourceSpan, SourceText? sourceText = null)
    {
        SourceSpan = sourceSpan;
        SourceText = sourceText;
    }

    /// <summary>The character range this location covers.</summary>
    public TextSpan SourceSpan { get; }

    /// <summary>
    /// The text <see cref="SourceSpan"/> indexes into, or <see langword="null"/> when the location is not bound to
    /// any text.
    /// </summary>
    public SourceText? SourceText { get; }

    /// <summary>
    /// Maps <see cref="SourceSpan"/> onto line and character positions. Returns <see langword="default"/> when
    /// <see cref="SourceText"/> is <see langword="null"/>.
    /// </summary>
    public LinePositionSpan GetLineSpan()
    {
        if (SourceText is null)
            return default;

        return new LinePositionSpan(GetLinePosition(SourceSpan.Start), GetLinePosition(SourceSpan.End));
    }

    /// <summary>
    /// Compares the span by value and the source text by reference, so two locations are equal only when they point
    /// at the same range of the same text.
    /// </summary>
    public bool Equals([NotNullWhen(true)] Location? other) => other is not null && SourceSpan == other.SourceSpan && ReferenceEquals(SourceText, other.SourceText);

    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as Location);
    public override int GetHashCode() => SourceSpan.GetHashCode();

    /// <summary>Returns the line span, or the character range when the location is not bound to any text.</summary>
    public override string ToString() => SourceText is null ? SourceSpan.ToString() : GetLineSpan().ToString();

    private LinePosition GetLinePosition(int position)
    {
        var line = SourceText!.GetLine(position);

        return new LinePosition(line.LineNumber, position - line.Start);
    }
}
