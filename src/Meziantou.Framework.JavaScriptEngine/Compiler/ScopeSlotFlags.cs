namespace Meziantou.Framework.JavaScriptEngine.Compiler;

[Flags]
internal enum ScopeSlotFlags : byte
{
    None = 0,

    /// <summary>The binding is immutable (const, class name, import): an assignment throws a TypeError.</summary>
    Immutable = 1,

    /// <summary>The binding is the name of a function expression: an assignment is ignored in sloppy code.</summary>
    FunctionName = 2,

    /// <summary>The slot is initialized with a hole (TDZ) when the environment is created.</summary>
    Uninitialized = 4,

    /// <summary>The slot holds an import binding (an indirection to the binding of another module).</summary>
    Import = 8,

    /// <summary>The binding can be deleted (a var created by sloppy eval code).</summary>
    Deletable = 16,
}
