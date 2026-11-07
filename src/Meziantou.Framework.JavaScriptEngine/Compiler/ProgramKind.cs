namespace Meziantou.Framework.JavaScriptEngine.Compiler;

internal enum ProgramKind
{
    Script,
    Module,

    /// <summary>The code of a direct or an indirect eval.</summary>
    Eval,
}
