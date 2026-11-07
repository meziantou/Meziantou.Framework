namespace Meziantou.Framework.JavaScriptEngine.Compiler;

internal enum VariableKind
{
    Var,
    Let,
    Const,
    Class,

    /// <summary>A function declaration at the top level of a function or a script (var-scoped).</summary>
    Function,

    /// <summary>A function declaration in a block (block-scoped, initialized when the block is entered).</summary>
    BlockFunction,
    Parameter,
    CatchParameter,
    Import,

    /// <summary>The name of a named function expression in its own scope (immutable).</summary>
    FunctionName,

    /// <summary>The inner binding of the name of a class (immutable).</summary>
    ClassName,

    /// <summary>The arguments object.</summary>
    Arguments,

    /// <summary>The this binding of a function, a script or a module (stored in the environment when an arrow function or eval uses it).</summary>
    This,

    /// <summary>new.target</summary>
    NewTarget,

    /// <summary>The [[HomeObject]] of a method (super).</summary>
    HomeObject,

    /// <summary>The function being called (needed by super() in arrow functions: the active function of the derived constructor).</summary>
    ActiveFunction,

    /// <summary>A private name (#x) of a class.</summary>
    PrivateName,

    /// <summary>A temporary binding created by the compiler (e.g. a computed key of a class field).</summary>
    Hidden,

    /// <summary>A var declared by sloppy eval code: created at runtime in the variable environment of the caller.</summary>
    EvalVar,
}
