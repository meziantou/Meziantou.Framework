namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A Private Name (https://tc39.es/ecma262/#sec-private-names): unique per evaluation of a class.</summary>
internal sealed class PrivateName(string description)
{
    /// <summary>Gets the name, without the '#'.</summary>
    public string Description { get; } = description;

    public override string ToString() => "#" + Description;
}
