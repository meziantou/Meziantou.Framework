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

    /// <summary>Gets the well-known symbol <c>Symbol.iterator</c>.</summary>
    public static JsSymbol Iterator => WellKnownSymbols.Iterator;

    /// <summary>Gets the well-known symbol <c>Symbol.asyncIterator</c>.</summary>
    public static JsSymbol AsyncIterator => WellKnownSymbols.AsyncIterator;

    /// <summary>Gets the well-known symbol <c>Symbol.toStringTag</c>.</summary>
    public static JsSymbol ToStringTag => WellKnownSymbols.ToStringTag;

    /// <summary>Gets the well-known symbol <c>Symbol.unscopables</c>.</summary>
    public static JsSymbol Unscopables => WellKnownSymbols.Unscopables;

    /// <summary>Gets the well-known symbol <c>Symbol.toPrimitive</c>.</summary>
    public static JsSymbol ToPrimitive => WellKnownSymbols.ToPrimitive;

    /// <summary>Gets the well-known symbol <c>Symbol.hasInstance</c>.</summary>
    public static JsSymbol HasInstance => WellKnownSymbols.HasInstance;
}
