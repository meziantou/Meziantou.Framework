namespace Meziantou.Framework.JavaScriptEngine.Modules;

/// <summary>
/// An ImportEntry Record (https://tc39.es/ecma262/#importentry-record). A null import name is a namespace import, or the
/// import of the source of the module when the phase of the request is <see cref="ModulePhase.Source"/>. A namespace import
/// with a <see cref="NamespaceNamesFilter"/> is a filtered namespace (<c>import { a, b } as ns from</c>,
/// https://tc39.es/proposal-deferred-reexports/).
/// </summary>
internal sealed record ImportEntry(ModuleRequest ModuleRequest, string? ImportName, string LocalName, List<string>? NamespaceNamesFilter = null)
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

    /// <summary><c>export { a, b } as ns from</c>: a namespace object with some exports of the requested module (https://tc39.es/proposal-deferred-reexports/).</summary>
    FilteredNamespace,
}

/// <summary>
/// An ExportEntry Record (https://tc39.es/ecma262/#exportentry-record). <see cref="NamespaceNamesFilter"/> is the names of
/// a <see cref="ExportImportKind.FilteredNamespace"/> export.
/// </summary>
/// <remarks>
/// The entries are compared by reference: a filtered namespace export is identified by its entry.
/// </remarks>
internal sealed class ExportEntry(string? exportName, ModuleRequest? moduleRequest, ExportImportKind importKind, string? importName, string? localName, List<string>? namespaceNamesFilter = null)
{
    public string? ExportName { get; } = exportName;

    public ModuleRequest? ModuleRequest { get; } = moduleRequest;

    public ExportImportKind ImportKind { get; } = importKind;

    public string? ImportName { get; } = importName;

    public string? LocalName { get; } = localName;

    public List<string>? NamespaceNamesFilter { get; } = namespaceNamesFilter;
}

/// <summary>
/// A ResolvedBinding Record: a binding of a module; a null binding name is the namespace object of the module (deferred
/// when <see cref="NamespacePhase"/> is <see cref="ModulePhase.Defer"/>), or its source (<see cref="ModulePhase.Source"/>).
/// The filtered namespace exported by <c>export { a } as ns from</c> is the binding <c>*ns*</c> of the exporting module,
/// whose value is created from <see cref="FilteredNamespaceExport"/>.
/// </summary>
internal sealed class ResolvedBinding(JsModule module, string? bindingName, ModulePhase namespacePhase = ModulePhase.Evaluation, ExportEntry? filteredNamespaceExport = null)
{
    /// <summary>The result of ResolveExport when the name is exported by several star exports.</summary>
    public static ResolvedBinding Ambiguous { get; } = new(module: null!, bindingName: null);

    public JsModule Module { get; } = module;

    public string? BindingName { get; } = bindingName;

    public bool IsNamespace => BindingName is null;

    /// <summary>Gets the phase of the namespace object, when <see cref="IsNamespace"/> is true.</summary>
    public ModulePhase NamespacePhase { get; } = namespacePhase;

    /// <summary>Gets the export entry of a filtered namespace export of <see cref="Module"/>, or null.</summary>
    public ExportEntry? FilteredNamespaceExport { get; } = filteredNamespaceExport;
}
