namespace Meziantou.Framework.JavaScriptEngine.Compiler;

[Flags]
internal enum FunctionTemplateFlags
{
    None = 0,
    Strict = 1 << 0,
    Arrow = 1 << 1,
    Async = 1 << 2,
    Generator = 1 << 3,

    /// <summary>The function has a [[Construct]] internal method (ordinary functions and classes).</summary>
    Constructor = 1 << 4,
    ClassConstructor = 1 << 5,
    DerivedConstructor = 1 << 6,

    /// <summary>A method, a getter or a setter (an object literal or a class element): it has a [[HomeObject]].</summary>
    Method = 1 << 7,
    ScriptCode = 1 << 8,
    EvalCode = 1 << 9,
    ModuleCode = 1 << 10,

    /// <summary>The synthetic initializer of the fields of a class.</summary>
    ClassFieldInitializer = 1 << 11,

    /// <summary>The function has no name in its source (it gets one by NamedEvaluation).</summary>
    Anonymous = 1 << 12,

    /// <summary>The template is the default constructor of a class (no constructor method).</summary>
    DefaultConstructor = 1 << 13,
}
