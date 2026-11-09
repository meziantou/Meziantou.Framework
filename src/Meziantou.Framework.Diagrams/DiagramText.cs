using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Diagrams;

/// <summary>The text of a label of a diagram: plain text, or markdown with bold and italic parts.</summary>
public sealed class DiagramText : IEquatable<DiagramText>
{
    /// <summary>Initializes a new instance of the <see cref="DiagramText"/> class.</summary>
    /// <param name="text">The text. A line feed starts a new line.</param>
    /// <param name="format">How the text is read.</param>
    public DiagramText(string text, DiagramTextFormat format = DiagramTextFormat.Plain)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!Enum.IsDefined(format))
            throw new ArgumentOutOfRangeException(nameof(format), format, "The format is not supported.");

        Text = text;
        Format = format;
    }

    /// <summary>Gets the text.</summary>
    public string Text { get; }

    /// <summary>Gets how the text is read.</summary>
    public DiagramTextFormat Format { get; }

    /// <summary>Gets a value that indicates whether the text is empty.</summary>
    public bool IsEmpty => Text.Length is 0;

    /// <summary>Converts a text to a plain <see cref="DiagramText"/>.</summary>
    /// <param name="text">The text, or <see langword="null"/>.</param>
    [return: NotNullIfNotNull(nameof(text))]
    public static implicit operator DiagramText?(string? text) => text is null ? null : new DiagramText(text);

    /// <summary>Creates a markdown text.</summary>
    /// <param name="text">The markdown.</param>
    /// <returns>The text.</returns>
    public static DiagramText Markdown(string text) => new(text, DiagramTextFormat.Markdown);

    /// <inheritdoc/>
    public bool Equals([NotNullWhen(true)] DiagramText? other) => other is not null && Format == other.Format && string.Equals(Text, other.Text, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as DiagramText);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Text), Format);

    /// <summary>Returns the text.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Text;
}
