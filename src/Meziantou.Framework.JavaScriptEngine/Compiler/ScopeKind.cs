namespace Meziantou.Framework.JavaScriptEngine.Compiler;

internal enum ScopeKind
{
    Script,
    Module,
    Eval,
    Function,

    /// <summary>The separate variable environment of a function whose parameters have expressions (defaults, patterns).</summary>
    FunctionBody,
    Block,
    Switch,
    Catch,
    Class,
    With,

    /// <summary>The scope of the name of a named function expression.</summary>
    FunctionName,
}
