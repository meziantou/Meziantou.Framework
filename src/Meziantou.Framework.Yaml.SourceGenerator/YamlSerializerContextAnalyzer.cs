using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.Yaml.SourceGeneration;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class YamlSerializerContextAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => YamlSerializerContextGenerator.SupportedDiagnosticDescriptors;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeClassDeclaration, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeClassDeclaration(SyntaxNodeAnalysisContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        if (classDeclaration.AttributeLists.Count == 0)
            return;

        var model = YamlSerializerContextGenerator.TryCreateContextModel(context.SemanticModel, classDeclaration);
        if (model is null)
            return;

        var validationResult = YamlSerializerContextGenerator.ValidateContext(context.SemanticModel.Compilation, model);
        foreach (var diagnostic in validationResult.Diagnostics)
        {
            context.ReportDiagnostic(diagnostic);
        }
    }
}
