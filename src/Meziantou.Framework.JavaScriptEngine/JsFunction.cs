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

    /// <summary>Gets the name given by SetFunctionName ([[InitialName]]), used by <c>Function.prototype.toString</c>.</summary>
    internal string? InitialName { get; private set; }

    /// <summary>Gets the source text of the function ([[SourceText]]), or null for a function without source text (built-in functions).</summary>
    internal virtual string? GetSourceText() => null;

    /// <summary>Defines the <c>length</c> property (SetFunctionLength).</summary>
    internal void SetFunctionLength(double length) => DefineBuiltIn("length", length, PropertyFlags.Configurable);

    /// <summary>Defines the <c>name</c> property (SetFunctionName) with a name that is a string value.</summary>
    internal void SetFunctionName(JsString name)
    {
        InitialName = name.ToString();
        DefineBuiltIn("name", JsValue.FromJsString(name), PropertyFlags.Configurable);
    }

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

        InitialName = value;
        DefineBuiltIn("name", value, PropertyFlags.Configurable);
    }
}
