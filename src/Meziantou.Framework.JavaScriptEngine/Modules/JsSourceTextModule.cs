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
        try
        {
            program = Parsing.JsParser.ParseModule(source);
        }
        catch (JsSyntaxErrorException ex)
        {
            throw ThrowHelper.CreateSyntaxErrorException(ex);
        }

        var code = CodeGenerator.CompileModule(program, source);
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
                        var request = AddRequest(import.Source, import.Attributes, import.Phase);
                        foreach (var specifier in import.Specifiers)
                        {
                            ImportEntries.Add(specifier switch
                            {
                                ImportDefaultSpecifier s when import.Phase == ModulePhase.Source => new ImportEntry(request, ImportName: null, s.Local.Name),
                                ImportDefaultSpecifier s => new ImportEntry(request, "default", s.Local.Name),
                                ImportNamespaceSpecifier s => new ImportEntry(request, ImportName: null, s.Local.Name),
                                ImportSpecifier s => new ImportEntry(request, GetName(s.Imported), s.Local.Name),
                                _ => throw new InvalidOperationException("Unknown import specifier"),
                            });
                        }

                        break;
                    }

                case ExportNamedDeclaration { Declaration: { } declaration }:
                    foreach (var name in GetDeclarationNames(declaration))
                    {
                        exportEntries.Add(new ExportEntry(name, ModuleRequest: null, ExportImportKind.None, ImportName: null, name));
                    }

                    break;
                case ExportNamedDeclaration { Source: null } export:
                    foreach (var specifier in export.Specifiers)
                    {
                        exportEntries.Add(new ExportEntry(GetName(specifier.Exported), ModuleRequest: null, ExportImportKind.None, ImportName: null, GetName(specifier.Local)));
                    }

                    break;
                case ExportNamedDeclaration export:
                    {
                        var request = AddRequest(export.Source!, export.Attributes);
                        foreach (var specifier in export.Specifiers)
                        {
                            exportEntries.Add(new ExportEntry(GetName(specifier.Exported), request, ExportImportKind.Name, GetName(specifier.Local), LocalName: null));
                        }

                        break;
                    }

                case ExportAllDeclaration exportAll:
                    {
                        var request = AddRequest(exportAll.Source, exportAll.Attributes);
                        exportEntries.Add(exportAll.Exported is null
                            ? new ExportEntry(ExportName: null, request, ExportImportKind.AllButDefault, ImportName: null, LocalName: null)
                            : new ExportEntry(GetName(exportAll.Exported), request, ExportImportKind.All, ImportName: null, LocalName: null));
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

                        exportEntries.Add(new ExportEntry("default", ModuleRequest: null, ExportImportKind.None, ImportName: null, localName));
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
                    IndirectExportEntries.Add(new ExportEntry(entry.ExportName, import.ModuleRequest, ExportImportKind.All, ImportName: null, LocalName: null));
                }
                else
                {
                    // The re-export of an imported binding
                    IndirectExportEntries.Add(new ExportEntry(entry.ExportName, import.ModuleRequest, ExportImportKind.Name, import.ImportName, LocalName: null));
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

    /// <summary>
    /// Adds a request to [[RequestedModules]] (ModuleRequests): the requests of the same module with the same phase are
    /// merged (a module imported deferred and not deferred has a request for each phase).
    /// </summary>
    private ModuleRequest AddRequest(StringLiteral source, List<ImportAttribute> attributes, ModulePhase phase = ModulePhase.Evaluation)
    {
        var request = ModuleRequest.Create(source.Value, attributes.Select(a => new KeyValuePair<string, string>(GetName(a.Key), a.Value.Value)), phase);
        foreach (var existing in RequestedModules)
        {
            if (existing.Phase == phase && existing.Equals(request))
                return existing;
        }

        RequestedModules.Add(request);
        return request;
    }

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
        var names = new List<string>();
        foreach (var entry in LocalExportEntries)
        {
            names.Add(entry.ExportName!);
        }

        foreach (var entry in IndirectExportEntries)
        {
            names.Add(entry.ExportName!);
        }

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

        foreach (var entry in IndirectExportEntries)
        {
            if (entry.ExportName == exportName)
            {
                var imported = GetImportedModule(entry.ModuleRequest!);
                if (entry.ImportKind == ExportImportKind.All)
                    return new ResolvedBinding(imported, bindingName: null, entry.ModuleRequest!.Phase);

                return imported.ResolveExport(entry.ImportName!, resolveSet);
            }
        }

        // A default export cannot be provided by export *
        if (exportName == "default")
            return null;

        ResolvedBinding? starResolution = null;
        foreach (var entry in StarExportEntries)
        {
            var imported = GetImportedModule(entry.ModuleRequest!);
            var resolution = imported.ResolveExport(exportName, resolveSet);
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

    internal override int GetBindingSlot(string bindingName) => Code.Environment.IndexOf(bindingName);
}
