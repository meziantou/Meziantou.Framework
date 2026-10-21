namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>The kind of a function.</summary>
internal enum FunctionKind
{
    Normal,
    Arrow,
    Method,
    Getter,
    Setter,
    ClassConstructor,
    ClassFieldInitializer,
    ClassStaticBlock,
}
