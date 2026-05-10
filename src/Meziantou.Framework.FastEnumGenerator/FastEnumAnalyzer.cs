using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.FastEnumGenerator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FastEnumAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor InvalidEnumType = new(
        id: "MFEG0001",
        title: "FastEnum target type is invalid",
        messageFormat: "The type '{0}' is not a valid enum type for FastEnum generation",
        category: "FastEnumGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(InvalidEnumType);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(static context => AnalyzeAttribute(context), SyntaxKind.Attribute);
    }

    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not AttributeSyntax attributeSyntax)
            return;

        if (!IsCandidate(attributeSyntax.Name))
            return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(attributeSyntax, context.CancellationToken).Symbol as IMethodSymbol;
        if (symbolInfo is not null &&
            (symbolInfo.ContainingType.Name is not "FastEnumAttribute" and not "FastEnumToStringAttribute" ||
             symbolInfo.ContainingType.ContainingNamespace.ToDisplayString() != "Meziantou.Framework.Annotations"))
        {
            return;
        }

        var arguments = attributeSyntax.ArgumentList?.Arguments;
        if (arguments is null || arguments.Value.Count != 1)
            return;

        var argumentExpression = arguments.Value[0].Expression;
        if (argumentExpression.IsKind(SyntaxKind.NullLiteralExpression))
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidEnumType, argumentExpression.GetLocation(), "(null)"));
            return;
        }

        if (argumentExpression is not TypeOfExpressionSyntax typeOfExpression)
            return;

        var typeSymbol = context.SemanticModel.GetTypeInfo(typeOfExpression.Type, context.CancellationToken).Type;
        if (typeSymbol is null || typeSymbol.TypeKind == TypeKind.Enum)
            return;

        context.ReportDiagnostic(Diagnostic.Create(InvalidEnumType, typeOfExpression.Type.GetLocation(), typeSymbol.ToDisplayString()));
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

        return name is "FastEnum" or "FastEnumAttribute" or "FastEnumToString" or "FastEnumToStringAttribute";
    }
}
