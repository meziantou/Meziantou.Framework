namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>A string part of a template literal.</summary>
internal sealed class TemplateElement : Node
{
    /// <summary>The cooked value, or null when the part has an invalid escape (only allowed in a tagged template).</summary>
    public string? Cooked { get; set; }

    /// <summary>The raw value (the line terminators normalized to LF).</summary>
    public string Raw { get; set; } = "";

    public bool Tail { get; set; }

    public override NodeType Type => NodeType.TemplateElement;
}
