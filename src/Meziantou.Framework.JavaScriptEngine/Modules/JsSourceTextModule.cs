using Meziantou.Framework.JavaScriptEngine.Ast;
using Meziantou.Framework.JavaScriptEngine.Compiler;
using Meziantou.Framework.JavaScriptEngine.Interpreter;
using Meziantou.Framework.JavaScriptEngine.Modules;
using Meziantou.Framework.JavaScriptEngine.Text;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A Source Text Module Record (https://tc39.es/ecma262/#sec-source-text-module-records): a module parsed from JavaScript source text.</summary>
public sealed class JsSourceTextModule : JsModule
{
    private JsSourceTextModule(JsRealm realm, SourceText source, object? hostDefined, CompiledModule code)
        : base(realm, source.Url, hostDefined)
    {
        Source = source;
        Code = code;
    }

    internal SourceText Source { get; }

    internal CompiledModule Code { get; }

    /// <summary>[[RequestedModules]]</summary>
    internal List<ModuleRequest> RequestedModules { get; } = [];

    /// <summary>[[LoadedModules]]</summary>
    internal List<(ModuleRequest Request, JsModule Module)> LoadedModules { get; } = [];

    internal List<ImportEntry> ImportEntries { get; } = [];

    internal List<ExportEntry> LocalExportEntries { get; } = [];

    internal List<ExportEntry> IndirectExportEntries { get; } = [];

    internal List<ExportEntry> StarExportEntries { get; } = [];

    /// <summary>
    /// [[OptionalIndirectExportEntries]] (https://tc39.es/proposal-deferred-reexports/): the entries of the <c>export defer</c>
    /// declarations, and of the <c>export * from</c> declarations that may re-export the <c>export defer</c> bindings of
    /// other modules, in source order.
    /// </summary>
    internal List<ExportEntry> OptionalIndirectExportEntries { get; } = [];

    /// <summary>The filtered namespace objects of the <c>export { a } as ns from</c> declarations (the <c>*ns*</c> bindings), created on first use.</summary>
    private Dictionary<ExportEntry, JsObject>? _filteredNamespaceExports;

    /// <summary>Whether ValidateOwnNamespaceExports(module, ~all~) succeeded (its result does not change).</summary>
    internal bool NamespaceExportsValidated { get; set; }

    /// <summary>[[HasTLA]]</summary>
    internal bool HasTopLevelAwait => Code.HasTopLevelAwait;

    internal int DfsIndex { get; set; } = -1;

    internal int DfsAncestorIndex { get; set; } = -1;

    /// <summary>[[CycleRoot]]</summary>
    internal JsSourceTextModule? CycleRoot { get; set; }

    /// <summary>[[AsyncEvaluationOrder]]: 0 when unset, -1 when done, the order of the asynchronous evaluation otherwise.</summary>
    internal long AsyncEvaluationOrder { get; set; }

    internal bool IsAsyncEvaluation => AsyncEvaluationOrder > 0;

    /// <summary>[[TopLevelCapability]]</summary>
    internal PromiseCapability? TopLevelCapability { get; set; }

    /// <summary>[[AsyncParentModules]]</summary>
    internal List<JsSourceTextModule> AsyncParentModules { get; } = [];

    /// <summary>[[PendingAsyncDependencies]]</summary>
    internal int PendingAsyncDependencies { get; set; }

    /// <summary>The import.meta object ([[ImportMeta]]), created on first use.</summary>
    internal JsObject? ImportMeta { get; set; }

    /// <summary>ParseModule(sourceText, realm, hostDefined)</summary>
    internal static JsSourceTextModule Parse(JsRealm realm, SourceText source, object? hostDefined)
    {
        Ast.Program program;
        CompiledModule code;
        try
        {
            program = Parsing.JsParser.ParseModule(source);
            code = CodeGenerator.CompileModule(program, source);
        }
        catch (JsSyntaxErrorException ex)
        {
            throw ThrowHelper.CreateSyntaxErrorException(ex);
        }

        var module = new JsSourceTextModule(realm, source, hostDefined, code);
        module.CollectEntries(program);
        return module;
    }

    private void CollectEntries(Ast.Program program)
    {
        var exportEntries = new List<ExportEntry>();
        foreach (var statement in program.Body)
        {
            switch (statement)
            {
                case ImportDeclaration import:
                    {
                        var request = AddRequest(import.Source, import.Attributes, import.Phase, GetImportedNames(import));
                        foreach (var specifier in import.Specifiers)
                        {
                            ImportEntries.Add(specifier switch
                            {
                                ImportDefaultSpecifier s when import.Phase == ModulePhase.Source => new ImportEntry(request, ImportName: null, s.Local.Name),
                                ImportDefaultSpecifier s => new ImportEntry(request, "default", s.Local.Name),
                                ImportNamespaceSpecifier s => new ImportEntry(request, ImportName: null, s.Local.Name, GetNames(s.Names)),
                                ImportSpecifier s => new ImportEntry(request, GetName(s.Imported), s.Local.Name),
                                _ => throw new InvalidOperationException("Unknown import specifier"),
                            });
                        }

                        break;
                    }

                case ExportNamedDeclaration { Declaration: { } declaration }:
                    foreach (var name in GetDeclarationNames(declaration))
                    {
                        exportEntries.Add(new ExportEntry(name, moduleRequest: null, ExportImportKind.None, importName: null, name));
                    }

                    break;
                case ExportNamedDeclaration { Source: null } export:
                    foreach (var specifier in export.Specifiers)
                    {
                        exportEntries.Add(new ExportEntry(GetName(specifier.Exported), moduleRequest: null, ExportImportKind.None, importName: null, GetName(specifier.Local)));
                    }

                    break;
                case ExportNamedDeclaration export:
                    {
                        var importedNames = new ImportedNames([.. export.Specifiers.Select(s => GetName(s.Local)).Distinct(StringComparer.Ordinal)], []);
                        var request = export.IsDeferred
                            ? CreateRequest(export.Source!, export.Attributes, ModulePhase.Evaluation, importedNames)
                            : AddRequest(export.Source!, export.Attributes, ModulePhase.Evaluation, importedNames);
                        foreach (var specifier in export.Specifiers)
                        {
                            var entry = new ExportEntry(GetName(specifier.Exported), request, ExportImportKind.Name, GetName(specifier.Local), localName: null);
                            if (export.IsDeferred)
                            {
                                OptionalIndirectExportEntries.Add(entry);
                            }
                            else
                            {
                                exportEntries.Add(entry);
                            }
                        }

                        break;
                    }

                case ExportAllDeclaration exportAll:
                    {
                        var filter = GetNames(exportAll.Names);
                        if (exportAll.IsDeferred)
                        {
                            // A deferred re-export of a namespace object is a deferred namespace, like import defer
                            var request = CreateRequest(exportAll.Source, exportAll.Attributes, ModulePhase.Defer, new ImportedNames([], filter));
                            OptionalIndirectExportEntries.Add(filter is null
                                ? new ExportEntry(GetName(exportAll.Exported!), request, ExportImportKind.All, importName: null, localName: null)
                                : new ExportEntry(GetName(exportAll.Exported!), request, ExportImportKind.FilteredNamespace, importName: null, localName: null, filter));
                        }
                        else if (exportAll.Exported is null)
                        {
                            // export * from may re-export the export defer bindings of the module: it is also an optional entry
                            var request = AddRequest(exportAll.Source, exportAll.Attributes, ModulePhase.Evaluation, ImportedNames.Empty());
                            var entry = new ExportEntry(exportName: null, request, ExportImportKind.AllButDefault, importName: null, localName: null);
                            exportEntries.Add(entry);
                            OptionalIndirectExportEntries.Add(entry);
                        }
                        else
                        {
                            var request = AddRequest(exportAll.Source, exportAll.Attributes, ModulePhase.Evaluation, new ImportedNames([], filter));
                            exportEntries.Add(filter is null
                                ? new ExportEntry(GetName(exportAll.Exported), request, ExportImportKind.All, importName: null, localName: null)
                                : new ExportEntry(GetName(exportAll.Exported), request, ExportImportKind.FilteredNamespace, importName: null, localName: null, filter));
                        }

                        break;
                    }

                case ExportDefaultDeclaration exportDefault:
                    {
                        var localName = exportDefault.Declaration switch
                        {
                            FunctionDeclaration { Function.Id: { } id } => id.Name,
                            ClassDeclaration { Class.Id: { } id } => id.Name,
                            _ => "*default*",
                        };

                        exportEntries.Add(new ExportEntry("default", moduleRequest: null, ExportImportKind.None, importName: null, localName));
                        break;
                    }
            }
        }

        foreach (var entry in exportEntries)
        {
            if (entry.ModuleRequest is null)
            {
                var import = ImportEntries.Find(i => i.LocalName == entry.LocalName);
                if (import is null)
                {
                    LocalExportEntries.Add(entry);
                }
                else if (import.IsNamespace)
                {
                    // The re-export of an imported namespace object
                    IndirectExportEntries.Add(import.NamespaceNamesFilter is null
                        ? new ExportEntry(entry.ExportName, import.ModuleRequest, ExportImportKind.All, importName: null, localName: null)
                        : new ExportEntry(entry.ExportName, import.ModuleRequest, ExportImportKind.FilteredNamespace, importName: null, localName: null, import.NamespaceNamesFilter));
                }
                else
                {
                    // The re-export of an imported binding
                    IndirectExportEntries.Add(new ExportEntry(entry.ExportName, import.ModuleRequest, ExportImportKind.Name, import.ImportName, localName: null));
                }
            }
            else if (entry.ImportKind == ExportImportKind.AllButDefault)
            {
                StarExportEntries.Add(entry);
            }
            else
            {
                IndirectExportEntries.Add(entry);
            }
        }
    }

    /// <summary>The [[ImportedNames]] of the request of an import declaration (DirectImportedNames and FilteredNamespaceImportedNames).</summary>
    private static ImportedNames GetImportedNames(ImportDeclaration import)
    {
        var names = ImportedNames.Empty();

        // The source of a module is not its exports
        if (import.Phase == ModulePhase.Source)
            return names;

        foreach (var specifier in import.Specifiers)
        {
            switch (specifier)
            {
                case ImportDefaultSpecifier:
                    names.Direct = ImportedNames.Merge(names.Direct, ["default"]);
                    break;
                case ImportSpecifier s:
                    names.Direct = ImportedNames.Merge(names.Direct, [GetName(s.Imported)]);
                    break;
                case ImportNamespaceSpecifier s:
                    names.Namespace = ImportedNames.Merge(names.Namespace, GetNames(s.Names));
                    break;
            }
        }

        return names;
    }

    private static List<string>? GetNames(List<Node>? names) => names?.ConvertAll(GetName);

    /// <summary>
    /// Adds a request to [[RequestedModules]] (ModuleRequests): the requests of the same module with the same phase are
    /// merged (a module imported deferred and not deferred has a request for each phase), and so are their imported names.
    /// </summary>
    private ModuleRequest AddRequest(StringLiteral source, List<ImportAttribute> attributes, ModulePhase phase, ImportedNames importedNames)
    {
        var request = CreateRequest(source, attributes, phase, importedNames);
        foreach (var existing in RequestedModules)
        {
            if (existing.Phase == phase && existing.Equals(request))
            {
                existing.ImportedNames.Direct = ImportedNames.Merge(existing.ImportedNames.Direct, importedNames.Direct);
                existing.ImportedNames.Namespace = ImportedNames.Merge(existing.ImportedNames.Namespace, importedNames.Namespace);
                return existing;
            }
        }

        RequestedModules.Add(request);
        return request;
    }

    /// <summary>Creates a request that is not in [[RequestedModules]] (the request of an <c>export defer</c> declaration).</summary>
    private static ModuleRequest CreateRequest(StringLiteral source, List<ImportAttribute> attributes, ModulePhase phase, ImportedNames importedNames)
        => ModuleRequest.Create(source.Value, attributes.Select(a => new KeyValuePair<string, string>(GetName(a.Key), a.Value.Value)), phase, importedNames);

    private static string GetName(Node node) => node switch
    {
        Identifier identifier => identifier.Name,
        StringLiteral literal => literal.Value,
        _ => throw new InvalidOperationException("Unexpected module export name: " + node.Type),
    };

    private static IEnumerable<string> GetDeclarationNames(Node declaration)
    {
        switch (declaration)
        {
            case VariableDeclaration variables:
                foreach (var declarator in variables.Declarations)
                {
                    foreach (var name in ScopeAnalyzer.GetBoundNames(declarator.Id))
                    {
                        yield return name.Name;
                    }
                }

                break;
            case FunctionDeclaration function:
                yield return function.Function.Id!.Name;
                break;
            case ClassDeclaration classDeclaration:
                yield return classDeclaration.Class.Id!.Name;
                break;
        }
    }

    /// <summary>GetImportedModule(referrer, request)</summary>
    internal JsModule GetImportedModule(ModuleRequest request)
    {
        foreach (var (loadedRequest, module) in LoadedModules)
        {
            if (loadedRequest.Equals(request))
                return module;
        }

        throw new InvalidOperationException("The module '" + request.Specifier + "' is not loaded");
    }

    internal override List<string> GetExportedNames(List<JsModule> exportStarSet)
    {
        if (exportStarSet.Contains(this))
            return [];

        exportStarSet.Add(this);
        var names = GetOwnExportedNames();
        foreach (var entry in StarExportEntries)
        {
            var requested = GetImportedModule(entry.ModuleRequest!);
            foreach (var name in requested.GetExportedNames(exportStarSet))
            {
                if (name != "default" && !names.Contains(name, StringComparer.Ordinal))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    /// <summary>GetOwnExportedNames(module): the names exported by the module, except those of its star exports.</summary>
    private List<string> GetOwnExportedNames()
    {
        var names = new List<string>(LocalExportEntries.Count + IndirectExportEntries.Count + OptionalIndirectExportEntries.Count);
        foreach (var entry in LocalExportEntries)
        {
            names.Add(entry.ExportName!);
        }

        foreach (var entry in IndirectExportEntries)
        {
            names.Add(entry.ExportName!);
        }

        foreach (var entry in OptionalIndirectExportEntries)
        {
            if (entry.ExportName is not null)
            {
                names.Add(entry.ExportName);
            }
        }

        return names;
    }

    internal override ResolvedBinding? ResolveExport(string exportName, List<(JsModule Module, string ExportName)> resolveSet)
    {
        foreach (var (module, name) in resolveSet)
        {
            // A circular import request
            if (module == this && name == exportName)
                return null;
        }

        resolveSet.Add((this, exportName));
        foreach (var entry in LocalExportEntries)
        {
            if (entry.ExportName == exportName)
                return new ResolvedBinding(this, entry.LocalName);
        }

        if (ResolveIndirectExport(IndirectExportEntries, exportName, resolveSet, out var resolution) || ResolveIndirectExport(OptionalIndirectExportEntries, exportName, resolveSet, out resolution))
            return resolution;

        // A default export cannot be provided by export *
        if (exportName == "default")
            return null;

        ResolvedBinding? starResolution = null;
        foreach (var entry in StarExportEntries)
        {
            var imported = GetImportedModule(entry.ModuleRequest!);
            resolution = imported.ResolveExport(exportName, resolveSet);
            if (resolution == ResolvedBinding.Ambiguous)
                return resolution;

            if (resolution is null)
                continue;

            if (starResolution is null)
            {
                starResolution = resolution;
            }
            else if (resolution.Module != starResolution.Module || !string.Equals(resolution.BindingName, starResolution.BindingName, StringComparison.Ordinal) || resolution.NamespacePhase != starResolution.NamespacePhase)
            {
                return ResolvedBinding.Ambiguous;
            }
        }

        return starResolution;
    }

    private bool ResolveIndirectExport(List<ExportEntry> entries, string exportName, List<(JsModule Module, string ExportName)> resolveSet, out ResolvedBinding? resolution)
    {
        foreach (var entry in entries)
        {
            if (entry.ExportName != exportName)
                continue;

            switch (entry.ImportKind)
            {
                case ExportImportKind.All:
                    resolution = new ResolvedBinding(GetImportedModule(entry.ModuleRequest!), bindingName: null, entry.ModuleRequest!.Phase);
                    return true;

                case ExportImportKind.FilteredNamespace:
                    // export { a } as ns from introduces the internal binding *ns* of this module
                    resolution = new ResolvedBinding(this, "*" + exportName + "*", filteredNamespaceExport: entry);
                    return true;

                default:
                    resolution = GetImportedModule(entry.ModuleRequest!).ResolveExport(entry.ImportName!, resolveSet);
                    return true;
            }
        }

        resolution = null;
        return false;
    }

    /// <summary>The value of the internal binding of a filtered namespace export (<c>export { a } as ns from</c>), created on first use.</summary>
    internal JsObject GetFilteredNamespaceExport(ExportEntry entry)
    {
        _filteredNamespaceExports ??= [];
        if (!_filteredNamespaceExports.TryGetValue(entry, out var ns))
        {
            ns = ModuleOperations.GetModuleNamespace(GetImportedModule(entry.ModuleRequest!), entry.ModuleRequest!.Phase, entry.NamespaceNamesFilter);
            _filteredNamespaceExports.Add(entry, ns);
        }

        return ns;
    }

    /// <summary>
    /// GetOptionalIndirectExportsModuleRequests(importedNames): the requests of the <c>export defer</c> declarations that
    /// provide <paramref name="importedNames"/> (null for all of them), and of the <c>export * from</c> declarations that
    /// may re-export them from other modules.
    /// </summary>
    internal List<ModuleRequest> GetOptionalIndirectExportsModuleRequests(List<string>? importedNames)
    {
        var requests = new List<ModuleRequest>();
        if (OptionalIndirectExportEntries.Count == 0)
            return requests;

        List<string>? ownExportedNames = null;
        foreach (var entry in OptionalIndirectExportEntries)
        {
            List<string>? direct;
            List<string>? ns;
            var isStarExport = false;
            if (entry.ImportKind == ExportImportKind.AllButDefault)
            {
                // export * from: the names that the module does not export itself may be export defer bindings of the module
                ownExportedNames ??= GetOwnExportedNames();
                ns = [];
                direct = ImportedNames.Exclude(ImportedNames.Exclude(importedNames, ["default"]), ownExportedNames);
                if (direct is { Count: 0 })
                    continue;

                isStarExport = true;
            }
            else if (ImportedNames.Contains(importedNames, entry.ExportName!))
            {
                (direct, ns) = entry.ImportKind switch
                {
                    ExportImportKind.Name => ([entry.ImportName!], []),
                    ExportImportKind.FilteredNamespace => ([], entry.NamespaceNamesFilter),
                    _ => (new List<string>(), (List<string>?)null),
                };
            }
            else
            {
                continue;
            }

            var next = entry.ModuleRequest!;
            var existing = requests.Find(r => r.Equals(next) && r.Phase == next.Phase && r.IsStarExport == isStarExport);
            if (existing is null)
            {
                requests.Add(new ModuleRequest(next.Specifier, next.Attributes, next.Phase, new ImportedNames(direct, ns)) { IsStarExport = isStarExport });
            }
            else
            {
                existing.ImportedNames.Direct = ImportedNames.Merge(existing.ImportedNames.Direct, direct);
                existing.ImportedNames.Namespace = ImportedNames.Merge(existing.ImportedNames.Namespace, ns);
            }
        }

        return requests;
    }

    internal override int GetBindingSlot(string bindingName) => Code.Environment.IndexOf(bindingName);
}
