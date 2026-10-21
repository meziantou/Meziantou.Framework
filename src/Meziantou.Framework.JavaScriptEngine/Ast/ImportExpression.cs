namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>import(source, options)</c>, <c>import.defer(source, options)</c> or <c>import.source(source, options)</c>.</summary>
internal sealed class ImportExpression : Node
{
    public Node Source { get; set; } = null!;

    public Node? Options { get; set; }

    /// <summary>
    /// Gets or sets the phase of the import: <c>import.defer(...)</c> is fulfilled with a deferred namespace object, and
    /// <c>import.source(...)</c> with the source of the module (https://tc39.es/proposal-source-phase-imports/).
    /// </summary>
    public Modules.ModulePhase Phase { get; set; }

    public override NodeType Type => NodeType.ImportExpression;
}
