namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>An expression statement, or a directive of a directive prologue.</summary>
internal sealed class ExpressionStatement : Node
{
    public Node Expression { get; set; } = null!;

    /// <summary>The raw text of the string literal (without the quotes) when the statement is a directive of the prologue.</summary>
    public string? Directive { get; set; }

    public override NodeType Type => NodeType.ExpressionStatement;
}
