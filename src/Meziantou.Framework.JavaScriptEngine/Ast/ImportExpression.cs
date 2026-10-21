namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>import(source, options)</c>, or <c>import.defer(source, options)</c> (https://tc39.es/proposal-defer-import-eval/)</summary>
internal sealed class ImportExpression : Node
{
    public Node Source { get; set; } = null!;

    public Node? Options { get; set; }

    /// <summary>Gets or sets whether the expression is <c>import.defer(...)</c>: the promise is fulfilled with a deferred namespace object.</summary>
    public bool IsDefer { get; set; }

    public override NodeType Type => NodeType.ImportExpression;
}
