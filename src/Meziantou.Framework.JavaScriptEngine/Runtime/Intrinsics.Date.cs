using Meziantou.Framework.JavaScriptEngine.Intl;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The Date built-ins are created on first use (the global property, or an intrinsic used by the host), with their ECMA-402 methods.</summary>
internal sealed partial class Intrinsics
{
    private JsObject? _datePrototype;
    private JsFunction? _date;
    private bool _isDateInitializing;

    /// <summary>%Date.prototype% (an ordinary object, not a Date)</summary>
    public JsObject DatePrototype => EnsureDate()._datePrototype!;

    /// <summary>%Date%</summary>
    public JsFunction Date => EnsureDate()._date!;

    partial void InitializeDate() => AddLazyGlobal("Date", static intrinsics => intrinsics.Date);

    /// <summary>Creates the Date built-ins, if they are not created yet.</summary>
    private Intrinsics EnsureDate()
    {
        if (_date is null && !_isDateInitializing)
        {
            _isDateInitializing = true;
            _datePrototype = new JsObject(ObjectPrototype);
            _date = DateBuiltins.Initialize(this);

            // The ECMA-402 amendments (toLocaleString, toLocaleDateString, toLocaleTimeString)
            DateTimeFormatLocaleSensitiveBuiltins.Initialize(this);
        }

        return this;
    }
}
