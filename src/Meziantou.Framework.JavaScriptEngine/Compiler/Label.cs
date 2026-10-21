namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>A position in the bytecode, marked once, targeted by jumps.</summary>
internal readonly record struct Label(int Id);
