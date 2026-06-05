using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PathGetFullPathOnFullPathAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: FullPathAnalyzerCommon.PathGetFullPathDiagnosticId,
        title: "Path.GetFullPath is redundant on FullPath",
        messageFormat: "Use the FullPath value directly instead of calling Path.GetFullPath",
        category: "FullPath",
        defaultSeverity: DiagnosticSeverity.Warning,
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
        // Path.GetFullPath(FullPath)
        // Path.GetFullPath(FullPath, string)
        var invocationOperation = (IInvocationOperation)context.Operation;
        if (invocationOperation.TargetMethod is { IsStatic: true, Name: "GetFullPath" } targetMethod && SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, analyzerContext.PathType))
        {
            if (analyzerContext.IsFullPathType(invocationOperation.Arguments[0].Value))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptor, invocationOperation.Syntax.GetLocation()));
                return;
            }
        }
    }
}
