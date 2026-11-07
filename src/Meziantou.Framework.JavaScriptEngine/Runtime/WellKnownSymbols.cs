namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The well-known symbols (https://tc39.es/ecma262/#sec-well-known-symbols), shared by all the realms.</summary>
internal static class WellKnownSymbols
{
    public static readonly JsSymbol AsyncDispose = new("Symbol.asyncDispose");
    public static readonly JsSymbol AsyncIterator = new("Symbol.asyncIterator");
    public static readonly JsSymbol Dispose = new("Symbol.dispose");
    public static readonly JsSymbol HasInstance = new("Symbol.hasInstance");
    public static readonly JsSymbol IsConcatSpreadable = new("Symbol.isConcatSpreadable");
    public static readonly JsSymbol Iterator = new("Symbol.iterator");
    public static readonly JsSymbol Match = new("Symbol.match");
    public static readonly JsSymbol Metadata = new("Symbol.metadata");
    public static readonly JsSymbol MatchAll = new("Symbol.matchAll");
    public static readonly JsSymbol Replace = new("Symbol.replace");
    public static readonly JsSymbol Search = new("Symbol.search");
    public static readonly JsSymbol Species = new("Symbol.species");
    public static readonly JsSymbol Split = new("Symbol.split");
    public static readonly JsSymbol ToPrimitive = new("Symbol.toPrimitive");
    public static readonly JsSymbol ToStringTag = new("Symbol.toStringTag");
    public static readonly JsSymbol Unscopables = new("Symbol.unscopables");
}
