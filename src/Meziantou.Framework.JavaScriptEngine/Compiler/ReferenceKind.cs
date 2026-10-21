namespace Meziantou.Framework.JavaScriptEngine.Compiler;

internal enum ReferenceKind
{
    Read,
    Write,
    ReadWrite,

    /// <summary>The initialization of a binding by its declaration.</summary>
    Declaration,
}
