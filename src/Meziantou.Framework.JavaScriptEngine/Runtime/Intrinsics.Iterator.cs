namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>%Iterator% and %Iterator.prototype%; the prototypes of the iterator helpers are created on first use.</summary>
internal sealed partial class Intrinsics
{
    private JsObject? _iteratorHelperPrototype;
    private JsObject? _wrapForValidIteratorPrototype;

    /// <summary>%Iterator%</summary>
    public JsFunction IteratorConstructor { get; private set; } = null!;

    /// <summary>%IteratorHelperPrototype%</summary>
    public JsObject IteratorHelperPrototype => EnsureIteratorHelpers()._iteratorHelperPrototype!;

    /// <summary>%WrapForValidIteratorPrototype%</summary>
    public JsObject WrapForValidIteratorPrototype => EnsureIteratorHelpers()._wrapForValidIteratorPrototype!;

    partial void InitializeIterator()
    {
        IteratorConstructor = IteratorBuiltins.Initialize(this);
    }

    /// <summary>Creates %IteratorHelperPrototype% and %WrapForValidIteratorPrototype%, if they are not created yet.</summary>
    private Intrinsics EnsureIteratorHelpers()
    {
        if (_iteratorHelperPrototype is null)
        {
            _iteratorHelperPrototype = new JsObject(IteratorPrototype);
            _wrapForValidIteratorPrototype = new JsObject(IteratorPrototype);
            IteratorBuiltins.InitializeHelpers(Realm, _iteratorHelperPrototype, _wrapForValidIteratorPrototype);
        }

        return this;
    }
}
