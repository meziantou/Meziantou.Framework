namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>A script or a module.</summary>
internal sealed class Program : Node
{
    /// <summary>The statements and the module items.</summary>
    public List<Node> Body { get; set; } = [];

    /// <summary>Whether the source is a module (always strict).</summary>
    public bool IsModule { get; set; }

    /// <summary>Whether the code is strict mode code (a module, or a 'use strict' directive).</summary>
    public bool IsStrict { get; set; }

    public override NodeType Type => NodeType.Program;
}
