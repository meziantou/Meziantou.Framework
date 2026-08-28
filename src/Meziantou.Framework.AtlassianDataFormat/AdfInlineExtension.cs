using System.Text.Json;

namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a macro rendered inline with surrounding text.</summary>
public sealed class AdfInlineExtension : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.InlineExtension;

    /// <summary>Gets the key of the extension.</summary>
    public required string ExtensionKey { get; init; }

    /// <summary>Gets the type of the extension.</summary>
    public required string ExtensionType { get; init; }

    /// <summary>Gets the fallback text of the extension.</summary>
    public string? Text { get; init; }

    /// <summary>Gets the parameters of the extension.</summary>
    public JsonElement? Parameters { get; init; }
}
