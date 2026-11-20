using Meziantou.Framework.JavaScriptEngine.Interpreter;
using Meziantou.Framework.JavaScriptEngine.Modules;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A binding exported by a module (a ResolvedBinding Record, https://tc39.es/ecma262/#resolvedbinding-record): a binding
/// of the environment of <see cref="Module"/>, or its namespace object. Returned by <see cref="JsHostDefinedModule.GetImport"/>.
/// </summary>
public sealed class JsModuleBinding
{
    private readonly ResolvedBinding _binding;

    internal JsModuleBinding(ResolvedBinding binding)
    {
        _binding = binding;
    }

    /// <summary>Gets the module whose environment holds the binding (the module that declares it, after the re-exports).</summary>
    public JsModule Module => _binding.Module;

    /// <summary>Gets the name of the binding in the environment of <see cref="Module"/>, or null when the binding is the namespace object of the module.</summary>
    public string? BindingName => _binding.FilteredNamespaceExport is null ? _binding.BindingName : null;

    /// <summary>Gets whether the binding is a namespace object (<c>export * as ns from</c>).</summary>
    public bool IsNamespace => _binding.IsNamespace || _binding.FilteredNamespaceExport is not null;

    /// <summary>
    /// Reads the value of the binding (GetBindingValue). Returns false when the binding is not initialized yet (e.g. a
    /// <c>let</c> or a <c>const</c> of a module that is not evaluated, the temporal dead zone).
    /// </summary>
    public bool TryGetValue(out JsValue value)
    {
        using var scope = Module.Realm.Engine.Enter();
        if (_binding.FilteredNamespaceExport is { } filteredNamespaceExport)
        {
            value = ((JsSourceTextModule)_binding.Module).GetFilteredNamespaceExport(filteredNamespaceExport);
            return true;
        }

        if (_binding.IsNamespace)
        {
            value = ModuleOperations.GetModuleNamespaceOrSource(_binding.Module, _binding.NamespacePhase);
            return true;
        }

        return ImportBindingCell.TryReadBinding(_binding.Module, _binding.Module.GetBindingSlot(_binding.BindingName!), out value);
    }

    /// <summary>Reads the value of the binding (GetBindingValue).</summary>
    /// <exception cref="JsException">The binding is not initialized (a ReferenceError).</exception>
    public JsValue GetValue()
    {
        using var scope = Module.Realm.Engine.Enter();
        if (!TryGetValue(out var value))
            return ThrowHelper.ThrowReferenceError<JsValue>("Cannot access '" + (BindingName ?? "*namespace*") + "' before initialization");

        return value;
    }
}
