namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>
/// The common part of the functions: declarations, expressions, arrows, methods, accessors, class constructors, and the
/// synthetic functions of the class field initializers and static blocks. <see cref="Node.Start"/> and
/// <see cref="Node.End"/> delimit the source text returned by <c>Function.prototype.toString</c>.
/// </summary>
internal sealed class FunctionNode : Node
{
    /// <summary>Gets or sets the name of a declaration or of a named function expression.</summary>
    public Identifier? Id { get; set; }

    /// <summary>Gets or sets the parameters: identifiers, patterns, <see cref="AssignmentPattern"/> and a final <see cref="RestElement"/>.</summary>
    public List<Node> Params { get; set; } = [];

    /// <summary>Gets or sets the body: a <see cref="BlockStatement"/>, or an expression when <see cref="IsExpressionBody"/>.</summary>
    public Node Body { get; set; } = null!;

    public FunctionKind Kind { get; set; }

    public bool IsAsync { get; set; }

    public bool IsGenerator { get; set; }

    /// <summary>Gets or sets whether the body is a concise arrow body (an expression).</summary>
    public bool IsExpressionBody { get; set; }

    /// <summary>Gets or sets whether the function is strict mode code (inherited, or a 'use strict' directive of its body).</summary>
    public bool IsStrict { get; set; }

    /// <summary>Gets or sets whether the parameters are only identifiers (no default, no pattern, no rest).</summary>
    public bool HasSimpleParameterList { get; set; }

    /// <summary>Gets or sets whether the function is the constructor of a class that has an <c>extends</c> clause.</summary>
    public bool IsDerivedConstructor { get; set; }

    /// <summary>Gets or sets the offset of the '(' of the parameters (not set for the arrow functions): the start of the text parsed again by the lazy compilation.</summary>
    public int ParametersStart { get; set; }

    /// <summary>Gets or sets whether the concise body of an arrow function was parsed without the <c>in</c> operator (the head of a for statement).</summary>
    public bool ExcludesIn { get; set; }

    public override NodeType Type => NodeType.FunctionNode;
}
