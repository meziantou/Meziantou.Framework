using Meziantou.Framework.JavaScriptEngine.Ast;

namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>The compile-time information of a function (or a script, a module, eval code, a class initializer).</summary>
internal sealed class FunctionInfo
{
    public FunctionInfo(FunctionInfo? parent, Node node, FunctionInfoKind kind, bool isStrict)
    {
        Parent = parent;
        Node = node;
        Kind = kind;
        IsStrict = isStrict;
    }

    public FunctionInfo? Parent { get; }

    /// <summary>Gets the <see cref="FunctionNode"/>, the <see cref="Program"/>, or the <see cref="ClassNode"/> of a class initializer.</summary>
    public Node Node { get; }

    public FunctionInfoKind Kind { get; }

    public bool IsStrict { get; }

    public FunctionNode? FunctionNode => Node as FunctionNode;

    /// <summary>Gets whether the function has its own this, new.target, arguments and home object (not an arrow function).</summary>
    public bool HasOwnThis => Kind != FunctionInfoKind.Arrow;

    public Scope Scope { get; set; } = null!;

    /// <summary>Gets or sets the variable environment of the body when the parameters have expressions.</summary>
    public Scope? BodyVarScope { get; set; }

    /// <summary>Gets or sets the scope of the lexical declarations of the top level of the body.</summary>
    public Scope? LexicalScope { get; set; }

    public bool HasDirectEval { get; set; }

    public bool HasParameterExpressions { get; set; }

    /// <summary>Gets or sets the arguments binding, when the function needs an arguments object.</summary>
    public Variable? ArgumentsVariable { get; set; }

    public bool UsesMappedArguments { get; set; }

    /// <summary>Gets the special bindings (this, new.target, home object, active function) declared in the function scope when an arrow function or eval uses them.</summary>
    public Variable? ThisVariable { get; set; }

    public Variable? NewTargetVariable { get; set; }

    public Variable? HomeObjectVariable { get; set; }

    public Variable? ActiveFunctionVariable { get; set; }

    /// <summary>Gets or sets the number of registers of the frame.</summary>
    public int RegisterCount { get; set; }

    /// <summary>Gets the function declarations of a block of sloppy code that also create a var binding (Annex B.3.2).</summary>
    public HashSet<FunctionDeclaration> AnnexBFunctions { get; } = [];

    /// <summary>Gets the nested functions, in source order.</summary>
    public List<FunctionInfo> Children { get; } = [];

    public int AllocateRegister() => RegisterCount++;
}
