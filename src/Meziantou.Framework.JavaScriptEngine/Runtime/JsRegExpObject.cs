namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A RegExp instance (https://tc39.es/ecma262/#sec-properties-of-regexp-instances): [[OriginalSource]],
/// [[OriginalFlags]], [[RegExpRecord]], [[RegExpMatcher]], and the [[Realm]] and [[LegacyFeaturesEnabled]] slots of the
/// legacy RegExp features.
/// </summary>
internal sealed class JsRegExpObject : JsObject
{
    public static readonly PropertyKey LastIndexKey = new("lastIndex");

    public JsRegExpObject(JsObject prototype, JsRealm realm, bool legacyFeaturesEnabled)
        : base(prototype, ObjectClass.RegExp)
    {
        Realm = realm;
        LegacyFeaturesEnabled = legacyFeaturesEnabled;

        // RegExpAlloc: lastIndex is writable, not enumerable, not configurable
        SetOwnSlot(LastIndexKey, JsValue.Undefined, PropertyFlags.Writable);
    }

    /// <summary>Gets the realm the object was created in ([[Realm]]).</summary>
    public JsRealm Realm { get; }

    /// <summary>Gets whether the object was created by %RegExp% itself rather than a subclass ([[LegacyFeaturesEnabled]]).</summary>
    public bool LegacyFeaturesEnabled { get; }

    /// <summary>[[OriginalSource]]</summary>
    public string OriginalSource { get; private set; } = "";

    /// <summary>[[OriginalFlags]]</summary>
    public string OriginalFlags { get; private set; } = "";

    /// <summary>The parsed [[OriginalFlags]].</summary>
    public RegExpFlags RecordFlags { get; private set; }

    /// <summary>[[RegExpMatcher]]; null until RegExpInitialize succeeds.</summary>
    public RegExpProgram? Matcher { get; private set; }

    internal void Initialize(string source, string flagsText, RegExpFlags flags, RegExpProgram matcher)
    {
        OriginalSource = source;
        OriginalFlags = flagsText;
        RecordFlags = flags;
        Matcher = matcher;
    }
}
