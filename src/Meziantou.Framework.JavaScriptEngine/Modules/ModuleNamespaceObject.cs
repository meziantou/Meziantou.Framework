using Meziantou.Framework.JavaScriptEngine.Interpreter;

namespace Meziantou.Framework.JavaScriptEngine.Modules;

/// <summary>A module namespace exotic object (https://tc39.es/ecma262/#sec-module-namespace-exotic-objects): the exports of a module, as live bindings.</summary>
internal sealed class ModuleNamespaceObject : JsObject
{
    private readonly JsModule _module;
    private readonly string[] _exports;
    private readonly HashSet<string> _exportSet;

    public ModuleNamespaceObject(JsModule module, List<string> exports)
        : base(prototype: null, ObjectClass.ModuleNamespace)
    {
        _module = module;
        exports.Sort(string.CompareOrdinal);
        _exports = [.. exports];
        _exportSet = new HashSet<string>(exports, StringComparer.Ordinal);
        Flags |= ObjectFlags.Exotic;
        DefineBuiltIn(WellKnownSymbols.ToStringTag, "Module", PropertyFlags.None);
        Flags &= ~ObjectFlags.Extensible;
    }

    private bool IsExport(in PropertyKey key, out string name)
    {
        name = key.AsString();
        return _exportSet.Contains(name);
    }

    internal override JsObject? GetPrototypeOf() => null;

    internal override bool SetPrototypeOf(JsObject? prototype) => prototype is null;

    internal override bool IsExtensible() => false;

    internal override bool PreventExtensions() => true;

    internal override bool GetOwnProperty(in PropertyKey key, out PropertyDescriptor descriptor)
    {
        if (key.IsSymbol)
            return OrdinaryGetOwnProperty(key, out descriptor);

        if (!IsExport(key, out var name))
        {
            descriptor = default;
            return false;
        }

        descriptor = PropertyDescriptor.Data(GetExportValue(name), PropertyFlags.Writable | PropertyFlags.Enumerable);
        return true;
    }

    internal override bool DefineOwnProperty(in PropertyKey key, in PropertyDescriptor descriptor)
    {
        if (key.IsSymbol)
            return OrdinaryDefineOwnProperty(key, descriptor);

        if (!GetOwnProperty(key, out var current))
            return false;

        if (descriptor.HasConfigurable && descriptor.Configurable)
            return false;

        if (descriptor.HasEnumerable && !descriptor.Enumerable)
            return false;

        if (descriptor.IsAccessorDescriptor)
            return false;

        if (descriptor.HasWritable && !descriptor.Writable)
            return false;

        if (descriptor.HasValue)
            return JsOperations.SameValue(descriptor.Value, current.Value);

        return true;
    }

    internal override bool HasProperty(in PropertyKey key)
    {
        if (key.IsSymbol)
            return OrdinaryHasProperty(key);

        return IsExport(key, out _);
    }

    internal override JsValue Get(in PropertyKey key, JsValue receiver)
    {
        if (key.IsSymbol)
            return OrdinaryGet(key, receiver);

        if (!IsExport(key, out var name))
            return default;

        return GetExportValue(name);
    }

    private JsValue GetExportValue(string name)
    {
        var binding = _module.ResolveExport(name, [])!;
        var target = binding.Module;
        if (binding.IsNamespace)
            return ModuleOperations.GetModuleNamespace(target);

        if (target.Environment is not { } environment)
            return ThrowHelper.ThrowReferenceError<JsValue>("Cannot access '" + name + "' before initialization");

        return ImportBindingCell.Read(environment.Slots[target.GetBindingSlot(binding.BindingName!)], new PropertyKey(name));
    }

    internal override bool Set(in PropertyKey key, JsValue value, JsValue receiver) => false;

    internal override bool Delete(in PropertyKey key)
    {
        if (key.IsSymbol)
            return OrdinaryDelete(key);

        return !IsExport(key, out _);
    }

    internal override List<PropertyKey> OwnPropertyKeys()
    {
        var keys = new List<PropertyKey>(_exports.Length + 1);
        foreach (var name in _exports)
        {
            keys.Add(new PropertyKey(name));
        }

        foreach (var key in OrdinaryOwnPropertyKeys())
        {
            if (key.IsSymbol)
            {
                keys.Add(key);
            }
        }

        return keys;
    }
}
