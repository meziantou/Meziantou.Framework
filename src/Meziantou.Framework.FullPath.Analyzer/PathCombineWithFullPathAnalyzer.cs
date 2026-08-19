using System.Collections.Immutable;
using Meziantou.Framework.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PathCombineWithFullPathAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: FullPathAnalyzerCommon.PathCombineWithFullPathDiagnosticId,
        title: "Use '/' operator instead of Path.Combine or Path.Join",
        messageFormat: "Use FullPath '/' operations instead of calling Path.{0}",
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
            if (!analyzerContext.IsValid || analyzerContext.PathType is null)
                return;

            context.RegisterOperationAction(context => Analyze(context, analyzerContext), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, FullPathContext analyzerContext)
    {
        var invocationOperation = (IInvocationOperation)context.Operation;
        if (invocationOperation.TargetMethod is not { IsStatic: true, Name: "Combine" or "Join" } targetMethod)
            return;

        if (!SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, analyzerContext.PathType))
            return;

        foreach (var argument in invocationOperation.Arguments)
        {
            if (!analyzerContext.IsFullPathType(argument.Value))
                continue;

            context.ReportDiagnostic(Descriptor, invocationOperation, targetMethod.Name);
            return;
        }
    }
}
