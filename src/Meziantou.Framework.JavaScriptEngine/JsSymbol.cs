namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A JavaScript symbol. Two symbols are the same value only if they are the same instance.</summary>
public sealed class JsSymbol : JsCell
{
    internal JsSymbol(string? description, bool isRegistered = false)
        : base(JsValueKind.Symbol)
    {
        Description = description;
        IsRegistered = isRegistered;
    }

    /// <summary>Gets the description of the symbol, or null (<c>Symbol()</c>).</summary>
    public string? Description { get; }

    /// <summary>Gets whether the symbol is in the global symbol registry (<c>Symbol.for()</c>): it cannot be held weakly.</summary>
    internal bool IsRegistered { get; }

    /// <summary>Returns <c>Symbol(description)</c> (SymbolDescriptiveString).</summary>
    public override string ToString() => "Symbol(" + Description + ")";
}
