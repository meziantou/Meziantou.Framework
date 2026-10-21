namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>The kind of a variable declaration.</summary>
internal enum VariableDeclarationKind
{
    Var,
    Let,
    Const,
    Using,
    AwaitUsing,
}
