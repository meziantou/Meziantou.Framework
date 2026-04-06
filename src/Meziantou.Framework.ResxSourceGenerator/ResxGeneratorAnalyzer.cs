using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.ResxSourceGenerator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ResxGeneratorAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor InvalidResx = new(
        id: "MFRG0001",
        title: "Couldn't parse Resx file",
        messageFormat: "Couldn't parse Resx file '{0}'",
        category: "ResxGenerator",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidPropertiesForResourceName = new(
        id: "MFRG0003",
        title: "Couldn't compute resource name",
        messageFormat: "Couldn't compute resource name for file '{0}'",
        category: "ResxGenerator",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InconsistentProperties = new(
        id: "MFRG0004",
        title: "Inconsistent properties",
        messageFormat: "Property '{0}' values for '{1}' are inconsistent",
        category: "ResxGenerator",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(InvalidResx, InvalidPropertiesForResourceName, InconsistentProperties);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var resxGroups = ResxGeneratorCommon.GetResxGroups(context.Options.AdditionalFiles.Where(static text => text.Path.EndsWith(".resx", StringComparison.OrdinalIgnoreCase))).ToArray();
        if (resxGroups.Length == 0)
            return;

        var options = context.Options.AnalyzerConfigOptionsProvider;
        var assemblyName = context.Compilation.AssemblyName;

        foreach (var resxGroup in resxGroups)
        {
            string? GetMetadataValue(string name, string? globalName)
            {
                var result = ResxGeneratorCommon.GetMetadataValue(options, name, globalName, resxGroup, out var inconsistentFilePath);
                if (inconsistentFilePath is not null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InconsistentProperties, location: null, name, inconsistentFilePath));
                }

                return result;
            }

            var rootNamespaceConfiguration = GetMetadataValue("RootNamespace", "RootNamespace");
            var projectDirConfiguration = GetMetadataValue("ProjectDir", "ProjectDir");
            _ = GetMetadataValue("Namespace", "DefaultResourcesNamespace");
            var defaultResourceNameConfiguration = GetMetadataValue("DefaultResourceName", globalName: null);
            var resourceNameConfiguration = GetMetadataValue("ResourceName", globalName: null);
            _ = GetMetadataValue("ClassName", globalName: null);
            _ = GetMetadataValue("Visibility", globalName: "DefaultResourcesVisibility");
            _ = GetMetadataValue("GenerateKeyNamesType", globalName: null);
            var generateResourcesTypeConfiguration = GetMetadataValue("GenerateResourcesType", globalName: null);

            var rootNamespace = rootNamespaceConfiguration ?? assemblyName ?? "";
            var projectDir = projectDirConfiguration ?? assemblyName ?? "";
            var defaultResourceName = defaultResourceNameConfiguration ?? ResxGeneratorCommon.ComputeResourceName(rootNamespace, projectDir, resxGroup.Key);
            var resourceName = resourceNameConfiguration ?? defaultResourceName;
            var generateResourcesType = ResxGeneratorCommon.ParseBoolean(generateResourcesTypeConfiguration, defaultValue: true);
            if (resourceName is null && generateResourcesType)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidPropertiesForResourceName, location: null, resxGroup.First().Path));
            }

            foreach (var resxFile in resxGroup)
            {
                if (!ResxGeneratorCommon.IsValidResxFile(resxFile, context.CancellationToken))
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidResx, location: null, resxFile.Path));
                }
            }
        }
    }
}
