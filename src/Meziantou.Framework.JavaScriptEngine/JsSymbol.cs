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

    /// <summary>
    /// Gets whether the symbol is in the global symbol registry of an engine (<c>Symbol.for()</c>, see
    /// <see cref="JsEngine.GetRegisteredSymbol"/>): its <see cref="Description"/> is its key (<c>Symbol.keyFor()</c>), and it
    /// cannot be held weakly.
    /// </summary>
    public bool IsRegistered { get; }

    /// <summary>
    /// Creates a new symbol (<c>Symbol(description)</c>): it is different from every other symbol, including the symbols
    /// with the same description. Unlike the objects, a symbol can be used by several engines.
    /// </summary>
    /// <param name="description">The description of the symbol, or null.</param>
    public static JsSymbol Create(string? description = null) => new(description);

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

    /// <summary>Gets the well-known symbol <c>Symbol.isConcatSpreadable</c>.</summary>
    public static JsSymbol IsConcatSpreadable => WellKnownSymbols.IsConcatSpreadable;

    /// <summary>Gets the well-known symbol <c>Symbol.species</c>.</summary>
    public static JsSymbol Species => WellKnownSymbols.Species;

    /// <summary>Gets the well-known symbol <c>Symbol.match</c>.</summary>
    public static JsSymbol Match => WellKnownSymbols.Match;

    /// <summary>Gets the well-known symbol <c>Symbol.matchAll</c>.</summary>
    public static JsSymbol MatchAll => WellKnownSymbols.MatchAll;

    /// <summary>Gets the well-known symbol <c>Symbol.replace</c>.</summary>
    public static JsSymbol Replace => WellKnownSymbols.Replace;

    /// <summary>Gets the well-known symbol <c>Symbol.search</c>.</summary>
    public static JsSymbol Search => WellKnownSymbols.Search;

    /// <summary>Gets the well-known symbol <c>Symbol.split</c>.</summary>
    public static JsSymbol Split => WellKnownSymbols.Split;

    /// <summary>Gets the well-known symbol <c>Symbol.dispose</c>.</summary>
    public static JsSymbol Dispose => WellKnownSymbols.Dispose;

    /// <summary>Gets the well-known symbol <c>Symbol.asyncDispose</c>.</summary>
    public static JsSymbol AsyncDispose => WellKnownSymbols.AsyncDispose;

    /// <summary>Gets the well-known symbol <c>Symbol.metadata</c> (the decorators proposal).</summary>
    public static JsSymbol Metadata => WellKnownSymbols.Metadata;
}
