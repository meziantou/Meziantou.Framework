using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IsTypeAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: RuleIdentifiers.IsTypeInvalidTypeDiagnosticId,
        title: "Do not use Assert.IsType with static or abstract types",
        messageFormat: "{0}",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Descriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(context =>
        {
            var assertType = context.Compilation.GetTypeByMetadataName(AssertionsAnalyzerHelpers.AssertMetadataName);
            if (assertType is null)
                return;

            context.RegisterOperationAction(context => Analyze(context, assertType), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol assertType)
    {
        var invocationOperation = (IInvocationOperation)context.Operation;
        if (!AssertionsAnalyzerHelpers.IsAssertIsTypeInvocation(invocationOperation, assertType))
            return;

        if (!TryGetExpectedType(invocationOperation, out var expectedType))
            return;

        if (IsStaticClass(expectedType))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptor, invocationOperation.Syntax.GetLocation(), $"Type '{expectedType.ToDisplayString()}' is static and cannot be used with Assert.IsType"));
            return;
        }

        if (expectedType.IsAbstract)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptor, invocationOperation.Syntax.GetLocation(), $"Type '{expectedType.ToDisplayString()}' is abstract and cannot be used with Assert.IsType. Use Assert.IsAssignableTo instead"));
        }
    }

    private static bool TryGetExpectedType(IInvocationOperation invocationOperation, out INamedTypeSymbol expectedType)
    {
        if (invocationOperation.TargetMethod.TypeArguments is [INamedTypeSymbol typeArgument])
        {
            expectedType = typeArgument;
            return true;
        }

        foreach (var argument in invocationOperation.Arguments)
        {
            if (argument.Parameter?.Ordinal is not 0)
                continue;

            var operation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(argument.Value);
            if (operation is ITypeOfOperation { TypeOperand: INamedTypeSymbol typeOfSymbol })
            {
                expectedType = typeOfSymbol;
                return true;
            }
        }

        expectedType = null!;
        return false;
    }

    private static bool IsStaticClass(INamedTypeSymbol type)
    {
        if (type.IsStatic || type is { TypeKind: TypeKind.Class, IsAbstract: true, IsSealed: true })
            return true;

        foreach (var syntaxReference in type.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is ClassDeclarationSyntax declaration &&
                declaration.Modifiers.Any(SyntaxKind.StaticKeyword))
            {
                return true;
            }
        }

        return false;
    }
}
