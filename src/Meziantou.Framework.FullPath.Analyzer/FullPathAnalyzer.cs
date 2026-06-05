using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FullPathAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor DoNotCallPathGetFullPathOnFullPath = new(
        id: FullPathAnalyzerCommon.PathGetFullPathDiagnosticId,
        title: "Path.GetFullPath is redundant on FullPath",
        messageFormat: "Use the FullPath value directly instead of calling Path.GetFullPath",
        category: "FullPath",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DoNotCombineFullPathWithFullPath = new(
        id: FullPathAnalyzerCommon.DivisionWithFullPathRightDiagnosticId,
        title: "Combining with a FullPath right operand is redundant",
        messageFormat: "Use the right FullPath operand directly instead of combining with '/'",
        category: "FullPath",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [DoNotCallPathGetFullPathOnFullPath, DoNotCombineFullPathWithFullPath];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(InitializeCore);
    }

    private static void InitializeCore(CompilationStartAnalysisContext context)
    {
        var fullPathType = context.Compilation.GetTypeByMetadataName(FullPathAnalyzerCommon.FullPathMetadataName);
        if (fullPathType is null)
            return;

        var pathType = context.Compilation.GetTypeByMetadataName(FullPathAnalyzerCommon.PathMetadataName);
        if (pathType is null)
            return;

        context.RegisterOperationAction(context => AnalyzeInvocation(context, pathType, fullPathType), OperationKind.Invocation);
        context.RegisterOperationAction(context => AnalyzeBinary(context, fullPathType), OperationKind.Binary);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, INamedTypeSymbol pathType, INamedTypeSymbol fullPathType)
    {
        var invocationOperation = (IInvocationOperation)context.Operation;
        if (!FullPathAnalyzerCommon.TryGetPathGetFullPathInvocationMatch(invocationOperation, pathType, fullPathType, out _))
            return;

        context.ReportDiagnostic(Diagnostic.Create(DoNotCallPathGetFullPathOnFullPath, invocationOperation.Syntax.GetLocation()));
    }

    private static void AnalyzeBinary(OperationAnalysisContext context, INamedTypeSymbol fullPathType)
    {
        var binaryOperation = (IBinaryOperation)context.Operation;
        if (!FullPathAnalyzerCommon.TryGetFullPathDivisionMatch(binaryOperation, fullPathType, out _))
            return;

        context.ReportDiagnostic(Diagnostic.Create(DoNotCombineFullPathWithFullPath, binaryOperation.Syntax.GetLocation()));
    }
}
