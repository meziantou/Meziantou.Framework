using System.Text.Json;

namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a macro rendered as a block, such as a diagram or a report.</summary>
public sealed class AdfExtension : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.Extension;

    /// <summary>Gets the key of the extension.</summary>
    public required string ExtensionKey { get; init; }

    /// <summary>Gets the type of the extension.</summary>
    public required string ExtensionType { get; init; }

    /// <summary>Gets the fallback text of the extension.</summary>
    public string? Text { get; init; }

    /// <summary>Gets the layout of the extension.</summary>
    public string? Layout { get; init; }

    /// <summary>Gets the parameters of the extension.</summary>
    public JsonElement? Parameters { get; init; }
}
