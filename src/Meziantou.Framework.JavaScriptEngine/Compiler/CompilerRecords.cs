using Meziantou.Framework.JavaScriptEngine.Text;

namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>The declarations of a script, for GlobalDeclarationInstantiation.</summary>
internal sealed record GlobalDeclarations((string Name, bool IsConstant)[] LexicalNames, string[] VarNames, string[] FunctionNames, string[] AnnexBFunctionNames);

/// <summary>The declarations of sloppy eval code, for EvalDeclarationInstantiation.</summary>
internal sealed record EvalDeclarations(string[] VarNames, string[] FunctionNames, string[] AnnexBFunctionNames);

/// <summary>The context of a direct eval call: what the code of the eval may use.</summary>
internal sealed record EvalContext(bool IsStrict, bool AllowNewTarget, bool AllowSuperProperty, bool AllowSuperCall, bool InClassFieldInitializer, string[] PrivateNames);

/// <summary>A template literal of a tagged template: its template object is created once per realm.</summary>
internal sealed class TemplateSite(string?[] cooked, string[] raw)
{
    public string?[] Cooked { get; } = cooked;

    public string[] Raw { get; } = raw;
}

/// <summary>The compile-time description of a class: its constructor (null for a default constructor) and its source text.</summary>
internal sealed record ClassTemplate(string Name, FunctionTemplate? Constructor, bool HasHeritage, SourceText Source, int SourceStart, int SourceEnd);
