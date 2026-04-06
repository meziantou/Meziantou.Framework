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

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [MissingOrInvalidArgumentCount, ArgumentMustBeInt, LengthMustBePositive];

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(static context => AnalyzeTypeDeclaration(context), SyntaxKind.StructDeclaration);
    }

    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not TypeDeclarationSyntax typeDeclarationSyntax)
            return;

        foreach (var attributeList in typeDeclarationSyntax.AttributeLists)
        {
            foreach (var attributeSyntax in attributeList.Attributes)
            {
                if (!IsCandidate(attributeSyntax.Name))
                    continue;

                var symbolInfo = context.SemanticModel.GetSymbolInfo(attributeSyntax, context.CancellationToken).Symbol as IMethodSymbol;
                if (symbolInfo is not null &&
                    (symbolInfo.ContainingType.Name is not "FixedStringBuilderAttribute" || !symbolInfo.ContainingType.ContainingNamespace.IsGlobalNamespace))
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
            }
        }
    }

    private static bool IsCandidate(NameSyntax nameSyntax)
    {
        var name = nameSyntax switch
        {
            IdentifierNameSyntax identifierNameSyntax => identifierNameSyntax.Identifier.ValueText,
            QualifiedNameSyntax qualifiedNameSyntax => qualifiedNameSyntax.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax aliasQualifiedNameSyntax => aliasQualifiedNameSyntax.Name.Identifier.ValueText,
            _ => null,
        };

        return name is "FixedStringBuilder" or "FixedStringBuilderAttribute";
    }
}
