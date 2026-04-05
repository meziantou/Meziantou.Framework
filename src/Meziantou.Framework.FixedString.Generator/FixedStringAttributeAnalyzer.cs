using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.FixedString.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FixedStringAttributeAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor MissingOrInvalidArgumentCount = new(
        id: "MFFSG0001",
        title: "FixedStringAttribute requires one argument",
        messageFormat: "FixedStringAttribute must have a single integer argument.",
        category: "FixedStringGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ArgumentMustBeInt = new(
        id: "MFFSG0002",
        title: "FixedStringAttribute argument type is invalid",
        messageFormat: "FixedStringAttribute argument must be an integer constant.",
        category: "FixedStringGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor LengthMustBePositive = new(
        id: "MFFSG0003",
        title: "FixedStringAttribute length must be positive",
        messageFormat: "FixedStringAttribute length must be greater than 0.",
        category: "FixedStringGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [MissingOrInvalidArgumentCount, ArgumentMustBeInt, LengthMustBePositive];

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(static context => AnalyzeNamedType(context), SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol namedTypeSymbol || namedTypeSymbol.TypeKind != TypeKind.Struct)
            return;

        foreach (var declaration in namedTypeSymbol.DeclaringSyntaxReferences)
        {
            if (declaration.GetSyntax(context.CancellationToken) is not TypeDeclarationSyntax typeDeclarationSyntax)
                continue;

            var semanticModel = context.Compilation.GetSemanticModel(typeDeclarationSyntax.SyntaxTree);
            foreach (var attributeList in typeDeclarationSyntax.AttributeLists)
            {
                foreach (var attributeSyntax in attributeList.Attributes)
                {
                    if (!IsCandidate(attributeSyntax.Name))
                        continue;

                    var symbolInfo = semanticModel.GetSymbolInfo(attributeSyntax, context.CancellationToken).Symbol as IMethodSymbol;
                    if (symbolInfo is not null &&
                        (symbolInfo.ContainingType.Name is not "FixedStringAttribute" || !symbolInfo.ContainingType.ContainingNamespace.IsGlobalNamespace))
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
                    var value = semanticModel.GetConstantValue(valueExpression, context.CancellationToken);
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

        return name is "FixedString" or "FixedStringAttribute";
    }
}
