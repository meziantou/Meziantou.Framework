using System.Collections.Immutable;
using Meziantou.Framework.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Roslyn.Samples;

#region Roslyn_Analyzer
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidTaskWaitAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: "SAMPLE001",
        title: "Do not call Task.Wait",
        messageFormat: "Do not call '{0}', await the task instead",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(context =>
        {
            var taskType = context.Compilation.GetBestTypeByMetadataName("System.Threading.Tasks.Task");
            if (taskType is null)
                return;

            context.RegisterOperationAction(context =>
            {
                var operation = (IInvocationOperation)context.Operation;
                if (operation.TargetMethod.Name is "Wait" && operation.TargetMethod.ContainingType.IsOrInheritsFrom(taskType))
                {
                    // Underline "Wait" in "task.Wait()" instead of the whole invocation
                    context.ReportDiagnostic(Rule, operation, DiagnosticInvocationReportOptions.ReportOnMember, operation.TargetMethod.Name);
                }
            }, OperationKind.Invocation);
        });
    }
}
#endregion
