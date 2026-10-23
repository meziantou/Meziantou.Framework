namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The ShadowRealm built-in is created on first use (a global property, or an intrinsic).</summary>
internal sealed partial class Intrinsics
{
    private JsFunction? _shadowRealm;
    private JsObject? _shadowRealmPrototype;

    /// <summary>%ShadowRealm%</summary>
    public JsFunction ShadowRealm => EnsureShadowRealm()._shadowRealm!;

    /// <summary>%ShadowRealm.prototype%</summary>
    public JsObject ShadowRealmPrototype => EnsureShadowRealm()._shadowRealmPrototype!;

    partial void InitializeShadowRealm()
    {
        AddLazyGlobal("ShadowRealm", static intrinsics => intrinsics.ShadowRealm);
    }

    /// <summary>Creates the ShadowRealm built-in, if it is not created yet.</summary>
    private Intrinsics EnsureShadowRealm()
    {
        if (_shadowRealm is null)
        {
            _shadowRealmPrototype = new JsObject(ObjectPrototype);
            _shadowRealm = ShadowRealmBuiltins.Initialize(this, _shadowRealmPrototype);
        }

        return this;
    }
}
