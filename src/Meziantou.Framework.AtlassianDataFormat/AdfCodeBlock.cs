namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a block of preformatted code.</summary>
public sealed class AdfCodeBlock : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.CodeBlock;

    /// <summary>Gets the language of the code, or <see langword="null"/> when unspecified.</summary>
    public string? Language { get; init; }
}
