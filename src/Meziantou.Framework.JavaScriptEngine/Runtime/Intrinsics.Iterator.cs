namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    /// <summary>%Iterator%</summary>
    public JsFunction IteratorConstructor { get; private set; } = null!;

    /// <summary>%IteratorHelperPrototype%</summary>
    public JsObject IteratorHelperPrototype { get; private set; } = null!;

    /// <summary>%WrapForValidIteratorPrototype%</summary>
    public JsObject WrapForValidIteratorPrototype { get; private set; } = null!;

    partial void InitializeIterator()
    {
        IteratorHelperPrototype = new JsObject(IteratorPrototype);
        WrapForValidIteratorPrototype = new JsObject(IteratorPrototype);
        IteratorConstructor = IteratorBuiltins.Initialize(this);
    }
}
