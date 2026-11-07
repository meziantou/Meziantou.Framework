namespace Meziantou.Framework.JavaScriptEngine.Compiler;

internal enum FunctionInfoKind
{
    Script,
    Module,
    Eval,
    Normal,
    Arrow,
    Method,
    ClassConstructor,

    /// <summary>The synthetic function that initializes the instance fields of a class.</summary>
    ClassFieldInitializer,

    /// <summary>The synthetic function that defines the static fields and runs the static blocks of a class.</summary>
    ClassStaticInitializer,

    /// <summary>A static block of a class (a method-like function called with the class as this).</summary>
    ClassStaticBlock,
}
