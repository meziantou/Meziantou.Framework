namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>
/// The value of the slot of an import binding (CreateImportBinding): an indirection to a binding of the environment of
/// another module. The environment of the target module may not exist yet (a cycle), so the module is stored.
/// </summary>
internal sealed class ImportBindingCell(JsModule module, int slot) : JsCell(JsValueKind.Undefined)
{
    public JsModule Module { get; } = module;

    public int Slot { get; } = slot;

    /// <summary>Reads an import binding: the slot holds an indirection, or the namespace object of a module.</summary>
    public static JsValue Read(JsValue slotValue, in PropertyKey name)
    {
        if (slotValue.Cell is ImportBindingCell cell)
        {
            var value = cell.Module.Environment is { } environment ? environment.Slots[cell.Slot] : JsValue.Hole;
            if (value.IsHole)
                ThrowHelper.ThrowReferenceError("Cannot access '" + name.ToString() + "' before initialization");

            return value;
        }

        if (slotValue.IsHole)
            ThrowHelper.ThrowReferenceError("Cannot access '" + name.ToString() + "' before initialization");

        return slotValue;
    }
}
