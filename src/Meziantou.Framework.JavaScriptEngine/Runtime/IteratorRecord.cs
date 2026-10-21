namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>An Iterator Record (https://tc39.es/ecma262/#sec-iterator-records).</summary>
internal sealed class IteratorRecord(JsObject iterator, JsValue nextMethod)
{
    public JsObject Iterator { get; } = iterator;

    public JsValue NextMethod { get; } = nextMethod;

    public bool Done { get; set; }
}
