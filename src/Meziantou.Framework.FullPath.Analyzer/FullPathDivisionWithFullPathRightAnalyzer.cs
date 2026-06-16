using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FullPathDivisionWithFullPathRightAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: FullPathAnalyzerCommon.DivisionWithFullPathRightDiagnosticId,
        title: "Use the right FullPath operand directly",
        messageFormat: "Use the right FullPath operand directly instead of combining with '/'",
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

            context.RegisterOperationAction(context => Analyze(context, analyzerContext), OperationKind.Binary);
        });
    }

    private static void Analyze(OperationAnalysisContext context, FullPathContext analyzerContext)
    {
        var binaryOperation = (IBinaryOperation)context.Operation;
        if (binaryOperation.OperatorKind != BinaryOperatorKind.Divide)
            return;

        if (!analyzerContext.IsFullPathType(binaryOperation.RightOperand))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Descriptor, binaryOperation.Syntax.GetLocation()));
    }
}
