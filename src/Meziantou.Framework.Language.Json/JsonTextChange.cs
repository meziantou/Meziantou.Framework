using System.Runtime.InteropServices;

namespace Meziantou.Framework.Language.Json;

/// <summary>Represents a text replacement operation applied to a <see cref="SourceText"/> or <see cref="JsonSyntaxTree"/>.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct JsonTextChange
{
    public JsonTextChange(TextSpan span, string newText)
    {
        ArgumentNullException.ThrowIfNull(newText);
        Span = span;
        NewText = newText;
    }

    public TextSpan Span { get; }
    public string NewText { get; }
}
