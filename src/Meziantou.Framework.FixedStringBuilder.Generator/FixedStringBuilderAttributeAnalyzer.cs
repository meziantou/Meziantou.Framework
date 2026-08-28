using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.FixedStringBuilder.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FixedStringBuilderAttributeAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor MissingOrInvalidArgumentCount = new(
        id: "MFFSG0001",
        title: "FixedStringBuilderAttribute requires one argument",
        messageFormat: "FixedStringBuilderAttribute must have a single integer argument",
        category: "FixedStringBuilderGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ArgumentMustBeInt = new(
        id: "MFFSG0002",
        title: "FixedStringBuilderAttribute argument type is invalid",
        messageFormat: "FixedStringBuilderAttribute argument must be an integer constant",
        category: "FixedStringBuilderGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor LengthMustBePositive = new(
        id: "MFFSG0003",
        title: "FixedStringBuilderAttribute length must be positive",
        messageFormat: "FixedStringBuilderAttribute length must be greater than 0",
        category: "FixedStringBuilderGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor LengthIsTooLarge = new(
        id: "MFFSG0004",
        title: "FixedStringBuilderAttribute length is too large",
        messageFormat: "FixedStringBuilderAttribute length must be less than or equal to {0}",
        category: "FixedStringBuilderGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [MissingOrInvalidArgumentCount, ArgumentMustBeInt, LengthMustBePositive, LengthIsTooLarge];

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static context =>
        {
            // Resolve the attribute once per compilation so each declaration can be compared against the symbol
            // itself. When the generator did not run there is no attribute and nothing to analyze.
            var attributeSymbol = context.Compilation.GetTypeByMetadataName("FixedStringBuilderAttribute");
            if (attributeSymbol is null)
                return;

            context.RegisterSyntaxNodeAction(context => AnalyzeTypeDeclaration(context, attributeSymbol), SyntaxKind.StructDeclaration);
        });
    }

    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context, INamedTypeSymbol attributeSymbol)
    {
        if (context.Node is not TypeDeclarationSyntax typeDeclarationSyntax)
            return;

        foreach (var attributeList in typeDeclarationSyntax.AttributeLists)
        {
            foreach (var attributeSyntax in attributeList.Attributes)
            {
                var symbolInfo = context.SemanticModel.GetSymbolInfo(attributeSyntax, context.CancellationToken);

                // The symbol does not bind when the arguments do not match any constructor, which is exactly what
                // MFFSG0001 reports, so the candidates are considered too.
                if ((symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()) is not IMethodSymbol attributeConstructor ||
                    !SymbolEqualityComparer.Default.Equals(attributeConstructor.ContainingType, attributeSymbol))
                {
                    continue;
                }

                var arguments = attributeSyntax.ArgumentList?.Arguments;
                if (arguments is null || arguments.Value.Count != 1)
                {
                    context.ReportDiagnostic(Diagnostic.Create(MissingOrInvalidArgumentCount, attributeSyntax.GetLocation()));
                    continue;
                }

                var valueExpression = arguments.Value[0].Expression;
                var value = context.SemanticModel.GetConstantValue(valueExpression, context.CancellationToken);
                if (!value.HasValue || value.Value is not int length)
                {
                    context.ReportDiagnostic(Diagnostic.Create(ArgumentMustBeInt, valueExpression.GetLocation()));
                    continue;
                }

                if (length <= 0)
                {
                    context.ReportDiagnostic(Diagnostic.Create(LengthMustBePositive, valueExpression.GetLocation()));
                }
                else if (length > FixedStringBuilderSourceGenerator.MaximumLength)
                {
                    context.ReportDiagnostic(Diagnostic.Create(LengthIsTooLarge, valueExpression.GetLocation(), FixedStringBuilderSourceGenerator.MaximumLength));
                }
            }
        }
    }
}
