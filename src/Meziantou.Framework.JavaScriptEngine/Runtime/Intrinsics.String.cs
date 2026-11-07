namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    /// <summary>%String%</summary>
    public JsFunction StringConstructor { get; private set; } = null!;

    /// <summary>%StringIteratorPrototype%</summary>
    public JsObject StringIteratorPrototype { get; private set; } = null!;

    partial void InitializeString()
    {
        StringIteratorPrototype = new JsObject(IteratorPrototype);
        StringConstructor = StringBuiltins.Initialize(this);
    }
}
