using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MethodShouldReturnFullPathAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: FullPathAnalyzerCommon.MethodShouldReturnFullPathDiagnosticId,
        title: "Method should return FullPath",
        messageFormat: "Method '{0}' returns FullPath values and should return FullPath instead of string",
        category: "FullPath",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Descriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(context =>
        {
            var analyzerContext = new FullPathContext(context.Compilation);
            if (!analyzerContext.IsValid)
                return;

            context.RegisterOperationBlockAction(context => AnalyzeOperationBlock(context, analyzerContext));
            context.RegisterOperationAction(context => AnalyzeLocalFunction(context, analyzerContext), OperationKind.LocalFunction);
        });
    }

    private static void AnalyzeOperationBlock(OperationBlockAnalysisContext context, FullPathContext analyzerContext)
    {
        if (context.OwningSymbol is not IMethodSymbol methodSymbol)
            return;

        if (methodSymbol.MethodKind is MethodKind.LocalFunction)
            return;

        if (methodSymbol.ReturnType.SpecialType != SpecialType.System_String)
            return;
        var hasReturnValue = false;
        var allReturnsAreFullPath = true;
        foreach (var operationBlock in context.OperationBlocks)
        {
            AnalyzeReturnOperations(operationBlock, analyzerContext, ref hasReturnValue, ref allReturnsAreFullPath);
        }

        if (!hasReturnValue && context.OperationBlocks.Length == 1 && context.OperationBlocks[0] is not IBlockOperation)
        {
            hasReturnValue = true;
            allReturnsAreFullPath &= analyzerContext.IsFullPathType(context.OperationBlocks[0]);
        }

        if (!hasReturnValue || !allReturnsAreFullPath)
            return;

        ReportDiagnostic(context.ReportDiagnostic, methodSymbol);
    }

    private static void AnalyzeLocalFunction(OperationAnalysisContext context, FullPathContext analyzerContext)
    {
        var localFunctionOperation = (ILocalFunctionOperation)context.Operation;
        if (localFunctionOperation.Symbol.ReturnType.SpecialType != SpecialType.System_String)
            return;

        var hasReturnValue = false;
        var allReturnsAreFullPath = true;
        if (localFunctionOperation.Body is not null)
        {
            AnalyzeReturnOperations(localFunctionOperation.Body, analyzerContext, ref hasReturnValue, ref allReturnsAreFullPath);
        }

        if (!hasReturnValue || !allReturnsAreFullPath)
            return;

        ReportDiagnostic(context.ReportDiagnostic, localFunctionOperation.Symbol);
    }

    private static void AnalyzeReturnOperations(IOperation operation, FullPathContext analyzerContext, ref bool hasReturnValue, ref bool allReturnsAreFullPath)
    {
        if (!allReturnsAreFullPath)
            return;

        if (operation is ILocalFunctionOperation)
            return;

        if (operation is IReturnOperation returnOperation && returnOperation.ReturnedValue is not null)
        {
            hasReturnValue = true;
            if (!analyzerContext.IsFullPathType(returnOperation.ReturnedValue))
            {
                allReturnsAreFullPath = false;
                return;
            }
        }

        foreach (var childOperation in operation.ChildOperations)
        {
            AnalyzeReturnOperations(childOperation, analyzerContext, ref hasReturnValue, ref allReturnsAreFullPath);
        }
    }

    private static void ReportDiagnostic(Action<Diagnostic> reportDiagnostic, IMethodSymbol methodSymbol)
    {
        Location? location = null;
        foreach (var candidateLocation in methodSymbol.Locations)
        {
            if (!candidateLocation.IsInSource)
                continue;

            location = candidateLocation;
            break;
        }

        if (location is null)
            return;

        reportDiagnostic(Diagnostic.Create(Descriptor, location, methodSymbol.Name));
    }
}
