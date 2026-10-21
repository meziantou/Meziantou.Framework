namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The RegExp built-ins are created on first use (the global property, a regular expression literal, or a method of
/// String.prototype that creates a regular expression), as the binary data built-ins. The methods of String.prototype
/// are created with the realm.
/// </summary>
internal sealed partial class Intrinsics
{
    private JsFunction? _regExp;
    private JsObject? _regExpPrototype;
    private JsFunction? _regExpPrototypeExec;
    private JsObject? _regExpStringIteratorPrototype;
    private bool _isRegExpInitializing;

    /// <summary>%RegExp%</summary>
    public JsFunction RegExp => EnsureRegExp()._regExp!;

    /// <summary>%RegExp.prototype%</summary>
    public JsObject RegExpPrototype => EnsureRegExp()._regExpPrototype!;

    /// <summary>%RegExp.prototype.exec%</summary>
    public JsFunction RegExpPrototypeExec => EnsureRegExp()._regExpPrototypeExec!;

    /// <summary>%RegExpStringIteratorPrototype%</summary>
    public JsObject RegExpStringIteratorPrototype => EnsureRegExp()._regExpStringIteratorPrototype!;

    /// <summary>The legacy static properties of %RegExp% (RegExp.$1, RegExp.lastMatch...).</summary>
    public RegExpLegacyStaticProperties RegExpLegacyStatics { get; } = new();

    partial void InitializeRegExp()
    {
        AddLazyGlobal("RegExp", static intrinsics => intrinsics.RegExp);
        StringRegExpBuiltins.Initialize(this);
    }

    /// <summary>Creates the RegExp built-ins, if they are not created yet.</summary>
    private Intrinsics EnsureRegExp()
    {
        if (_regExp is null && !_isRegExpInitializing)
        {
            _isRegExpInitializing = true;
            (_regExp, _regExpPrototype, _regExpPrototypeExec, _regExpStringIteratorPrototype) = RegExpBuiltins.Initialize(this);
        }

        return this;
    }
}
