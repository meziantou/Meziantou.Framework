namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    /// <summary>%RegExp%</summary>
    public JsFunction RegExp { get; private set; } = null!;

    /// <summary>%RegExp.prototype%</summary>
    public JsObject RegExpPrototype { get; private set; } = null!;

    /// <summary>%RegExp.prototype.exec%</summary>
    public JsFunction RegExpPrototypeExec { get; private set; } = null!;

    /// <summary>%RegExpStringIteratorPrototype%</summary>
    public JsObject RegExpStringIteratorPrototype { get; private set; } = null!;

    /// <summary>The legacy static properties of %RegExp% (RegExp.$1, RegExp.lastMatch...).</summary>
    public RegExpLegacyStaticProperties RegExpLegacyStatics { get; } = new();

    partial void InitializeRegExp()
    {
        var (constructor, prototype, exec, iteratorPrototype) = RegExpBuiltins.Initialize(this);
        RegExp = constructor;
        RegExpPrototype = prototype;
        RegExpPrototypeExec = exec;
        RegExpStringIteratorPrototype = iteratorPrototype;
        StringRegExpBuiltins.Initialize(this);
    }
}
