namespace Meziantou.Framework.JavaScriptEngine.Modules;

/// <summary>An ImportEntry Record (https://tc39.es/ecma262/#importentry-record). A null import name is a namespace import.</summary>
internal sealed record ImportEntry(ModuleRequest ModuleRequest, string? ImportName, string LocalName)
{
    public bool IsNamespace => ImportName is null;
}

/// <summary>The kind of the [[ImportName]] of an export entry.</summary>
internal enum ExportImportKind
{
    /// <summary>No import name (a local export).</summary>
    None,

    /// <summary>A named binding of the requested module.</summary>
    Name,

    /// <summary><c>export * as ns from</c>: the namespace object of the requested module.</summary>
    All,

    /// <summary><c>export * from</c>: every export of the requested module, except default.</summary>
    AllButDefault,
}

/// <summary>An ExportEntry Record (https://tc39.es/ecma262/#exportentry-record).</summary>
internal sealed record ExportEntry(string? ExportName, ModuleRequest? ModuleRequest, ExportImportKind ImportKind, string? ImportName, string? LocalName);

/// <summary>
/// A ResolvedBinding Record: a binding of a module; a null binding name is the namespace object of the module (deferred
/// when <see cref="NamespacePhase"/> is <see cref="ModulePhase.Defer"/>).
/// </summary>
internal sealed class ResolvedBinding(JsModule module, string? bindingName, ModulePhase namespacePhase = ModulePhase.Evaluation)
{
    /// <summary>The result of ResolveExport when the name is exported by several star exports.</summary>
    public static ResolvedBinding Ambiguous { get; } = new(module: null!, bindingName: null);

    public JsModule Module { get; } = module;

    public string? BindingName { get; } = bindingName;

    public bool IsNamespace => BindingName is null;

    /// <summary>Gets the phase of the namespace object, when <see cref="IsNamespace"/> is true.</summary>
    public ModulePhase NamespacePhase { get; } = namespacePhase;
}
