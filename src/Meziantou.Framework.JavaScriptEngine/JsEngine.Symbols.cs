namespace Meziantou.Framework.JavaScriptEngine;

public sealed partial class JsEngine
{
    // The GlobalSymbolRegistry (https://tc39.es/ecma262/#sec-symbol.for): shared by all the realms of the agent
    private readonly Dictionary<string, JsSymbol> _symbolRegistry = new(StringComparer.Ordinal);

    /// <summary>Gets the symbol of the global symbol registry whose key is <paramref name="key"/>, creating it when needed (<c>Symbol.for</c>).</summary>
    internal JsSymbol GetOrCreateRegisteredSymbol(string key)
    {
        if (!_symbolRegistry.TryGetValue(key, out var symbol))
        {
            symbol = new JsSymbol(key, isRegistered: true);
            _symbolRegistry.Add(key, symbol);
        }

        return symbol;
    }

    /// <summary>Increments the depth of the running calls after checking the recursion limit, for the internal methods that recurse without a call (proxies, bound functions).</summary>
    internal void EnterRecursion()
    {
        CheckCallDepth();
        CallDepth++;
    }

    /// <summary>Decrements the depth of the running calls (after <see cref="EnterRecursion"/>).</summary>
    internal void ExitRecursion() => CallDepth--;
}
