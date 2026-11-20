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

            return value.Cell is LiveBindingCell live ? live.Getter() : value;
        }

        if (slotValue.IsHole)
            ThrowHelper.ThrowReferenceError("Cannot access '" + name.ToString() + "' before initialization");

        return slotValue.Cell is LiveBindingCell liveBinding ? liveBinding.Getter() : slotValue;
    }

    /// <summary>Reads a binding of the environment of a module: false when the binding is not initialized.</summary>
    public static bool TryReadBinding(JsModule module, int slot, out JsValue value)
    {
        value = module.Environment is { } environment ? environment.Slots[slot] : JsValue.Hole;
        if (value.Cell is ImportBindingCell cell)
            return TryReadBinding(cell.Module, cell.Slot, out value);

        if (value.IsHole)
            return false;

        if (value.Cell is LiveBindingCell live)
        {
            value = live.Getter();
        }

        return true;
    }
}

/// <summary>
/// The value of the slot of a live binding of a <see cref="JsHostDefinedModule"/> (<see cref="JsHostDefinedModule.SetExportGetter"/>):
/// the value of the binding is read from the getter each time the binding is read.
/// </summary>
internal sealed class LiveBindingCell(Func<JsValue> getter) : JsCell(JsValueKind.Undefined)
{
    public Func<JsValue> Getter { get; } = getter;
}
