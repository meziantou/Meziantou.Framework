using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>The C# representation of a symbol or of a type, split into segments.</summary>
public sealed class PublicApiFormattedDeclaration
{
    internal PublicApiFormattedDeclaration(ImmutableArray<PublicApiDeclarationSegment> segments)
    {
        Segments = segments;
        Text = string.Concat(segments.Select(static segment => segment.Text));
    }

    /// <summary>Gets the full text, which is the concatenation of the segments.</summary>
    public string Text { get; }

    public ImmutableArray<PublicApiDeclarationSegment> Segments { get; }

    public override string ToString() => Text;
}
