using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Meziantou.Framework.Roslyn;

namespace Meziantou.Framework.Analyzers.FullPath;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MethodShouldReturnFullPathAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: FullPathAnalyzerCommon.MethodShouldReturnFullPathDiagnosticId,
        title: "Return FullPath instead of string",
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

        // Accessors are reported by PropertyShouldReturnFullPathAnalyzer, which points at the property itself
        if (methodSymbol.AssociatedSymbol is not null)
            return;

        if (methodSymbol.ReturnType.SpecialType != SpecialType.System_String)
            return;

        if (!methodSymbol.CanChangeDeclaredType())
            return;

        var hasReturnValue = false;
        var allReturnsAreFullPath = true;
        foreach (var operationBlock in context.OperationBlocks)
        {
            analyzerContext.AnalyzeReturnOperations(operationBlock, ref hasReturnValue, ref allReturnsAreFullPath);
        }

        if (!hasReturnValue && context.OperationBlocks.Length == 1 && context.OperationBlocks[0] is not IBlockOperation)
        {
            hasReturnValue = true;
            allReturnsAreFullPath &= analyzerContext.IsFullPathType(context.OperationBlocks[0]);
        }

        if (!hasReturnValue || !allReturnsAreFullPath)
            return;

        ReportDiagnostic(context, methodSymbol);
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
            analyzerContext.AnalyzeReturnOperations(localFunctionOperation.Body, ref hasReturnValue, ref allReturnsAreFullPath);
        }

        if (!hasReturnValue || !allReturnsAreFullPath)
            return;

        ReportDiagnostic(context, localFunctionOperation.Symbol);
    }

    private static void ReportDiagnostic(DiagnosticReporter reporter, IMethodSymbol methodSymbol)
    {
        var location = methodSymbol.GetFirstSourceLocation();
        if (location is null)
            return;

        reporter.ReportDiagnostic(Descriptor, location, [methodSymbol.Name]);
    }
}
