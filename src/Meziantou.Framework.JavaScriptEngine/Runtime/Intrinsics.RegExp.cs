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

    // The accessors of %RegExp.prototype% read by ToString(Get(R, "flags")), in the order of RegExpBuiltins.FlagAccessorKeys
    private AccessorPair[]? _regExpFlagAccessors;

    // The state of IsRegExpFlagsIntact, valid while the epoch of the inline caches does not change: the positions of the
    // accessors, whose values are compared (a value can change without a change of structure)
    private int _regExpFlagsEpoch = -1;
    private int[]? _regExpFlagAccessorPositions;
    private bool _regExpFlagAccessorsInTable;

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
            (_regExp, _regExpPrototype, _regExpPrototypeExec, _regExpStringIteratorPrototype, _regExpFlagAccessors) = RegExpBuiltins.Initialize(this);
        }

        return this;
    }

    /// <summary>
    /// Gets whether the accessors of %RegExp.prototype% that ToString(Get(R, "flags")) runs ("flags", and the flags that
    /// its getter reads) are the built-in ones: a RegExp instance that has no own property but lastIndex and whose
    /// prototype is %RegExp.prototype% then has the flags of its [[OriginalFlags]]. The prototype is flagged as a
    /// dependency of the inline caches: a change of its structure invalidates the positions of the accessors.
    /// </summary>
    public bool IsRegExpFlagsIntact()
    {
        var prototype = RegExpPrototype;
        var epoch = Realm.Engine.InlineCacheEpoch;
        if (epoch != _regExpFlagsEpoch)
        {
            _regExpFlagAccessorPositions = FindRegExpFlagAccessors(prototype, out _regExpFlagAccessorsInTable);
            _regExpFlagsEpoch = epoch;
        }

        if (_regExpFlagAccessorPositions is not { } positions)
            return false;

        var accessors = _regExpFlagAccessors!;
        for (var i = 0; i < positions.Length; i++)
        {
            if (!ReferenceEquals(prototype.GetValueForCache(positions[i], _regExpFlagAccessorsInTable).Cell, accessors[i]))
                return false;
        }

        return true;
    }

    /// <summary>Finds the positions of the accessors of the flags in %RegExp.prototype%, or null when one is not an accessor.</summary>
    private static int[]? FindRegExpFlagAccessors(JsObject prototype, out bool inTable)
    {
        prototype.MarkInlineCacheHolder();
        inTable = prototype.Map is PropertyTable;
        var keys = RegExpBuiltins.FlagAccessorKeys;
        var positions = new int[keys.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            if (!prototype.TryFindOwnForCache(keys[i], out positions[i], out var flags) || (flags & PropertyFlags.Accessor) == 0)
                return null;
        }

        return positions;
    }
}
