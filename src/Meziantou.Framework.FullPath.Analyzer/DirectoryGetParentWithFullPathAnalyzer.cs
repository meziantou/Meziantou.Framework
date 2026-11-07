using System.Collections.Immutable;
using Meziantou.Framework.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

/// <summary>
/// Reports <c>Directory.GetParent</c> applied to a <c>FullPath</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DirectoryGetParentWithFullPathAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: FullPathAnalyzerCommon.DirectoryGetParentWithFullPathDiagnosticId,
        title: "Use FullPath.Parent instead of Directory.GetParent",
        messageFormat: "Use FullPath.Parent instead of calling Directory.GetParent",
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
            if (!analyzerContext.IsValid || analyzerContext.DirectoryType is null)
                return;

            context.RegisterOperationAction(context => Analyze(context, analyzerContext), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, FullPathContext analyzerContext)
    {
        var invocationOperation = (IInvocationOperation)context.Operation;
        if (invocationOperation.TargetMethod is not { IsStatic: true, Name: "GetParent" } targetMethod)
            return;

        if (!SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, analyzerContext.DirectoryType))
            return;

        if (invocationOperation.Arguments.Length != 1 || !analyzerContext.IsFullPathType(invocationOperation.Arguments[0].Value))
            return;

        context.ReportDiagnostic(Descriptor, invocationOperation);
    }
}
