namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A JavaScript function: an object with a [[Call]] internal method, and possibly a [[Construct]] internal method.</summary>
public abstract class JsFunction : JsObject
{
    private protected JsFunction(JsRealm realm, JsObject? prototype)
        : base(prototype, ObjectClass.Function)
    {
        Realm = realm;
        Flags |= ObjectFlags.Callable;
    }

    /// <summary>Gets the realm of the function ([[Realm]]).</summary>
    public JsRealm Realm { get; }

    /// <summary>Defines the <c>length</c> property (SetFunctionLength).</summary>
    internal void SetFunctionLength(double length) => DefineBuiltIn("length", length, PropertyFlags.Configurable);

    /// <summary>Defines the <c>name</c> property (SetFunctionName).</summary>
    internal void SetFunctionName(PropertyKey name, string? prefix = null)
    {
        string value;
        if (name.IsSymbol)
        {
            var description = name.AsSymbol().Description;
            value = description is null ? "" : "[" + description + "]";
        }
        else
        {
            value = name.AsString();
        }

        if (prefix is not null)
        {
            value = prefix + " " + value;
        }

        DefineBuiltIn("name", value, PropertyFlags.Configurable);
    }
}
