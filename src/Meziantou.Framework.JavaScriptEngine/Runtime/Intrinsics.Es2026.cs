namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    /// <summary>The Array.fromAsync function (a property of %Array%).</summary>
    public JsFunction ArrayFromAsync { get; private set; } = null!;

    partial void InitializeEs2026Additions()
    {
        ArrayFromAsync = Es2026Builtins.Initialize(this);

        // %Array% is created by InitializeArray, which runs before
        if (ArrayPrototype.GetOwnProperty(new PropertyKey("constructor"), out var descriptor) && descriptor.Value.Cell is JsObject array)
        {
            array.DefineBuiltIn("fromAsync", ArrayFromAsync);
        }
    }
}
