using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

/// <summary>
/// Reports containment checks written as a prefix test on the string representation of two <c>FullPath</c> values.
/// </summary>
/// <remarks>
/// A prefix test reports <c>/a/bc</c> as being under <c>/a/b</c>, and reports a path as being under itself.
/// <c>FullPath.IsChildOf</c> checks the directory separator that follows the prefix.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StartsWithInsteadOfIsChildOfAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: FullPathAnalyzerCommon.StartsWithInsteadOfIsChildOfDiagnosticId,
        title: "Use FullPath.IsChildOf instead of StartsWith",
        messageFormat: "Use FullPath.IsChildOf instead of a prefix test, which also matches a sibling whose name starts with the root",
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
            if (!analyzerContext.IsValid)
                return;

            context.RegisterOperationAction(context => Analyze(context, analyzerContext), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, FullPathContext analyzerContext)
    {
        var invocationOperation = (IInvocationOperation)context.Operation;
        if (invocationOperation.TargetMethod is not { Name: "StartsWith" } targetMethod)
            return;

        if (targetMethod.ContainingType?.SpecialType != SpecialType.System_String)
            return;

        if (invocationOperation.Instance is not { } instance || !analyzerContext.IsFullPathType(instance))
            return;

        if (invocationOperation.Arguments.Length == 0 || !analyzerContext.IsFullPathType(invocationOperation.Arguments[0].Value))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Descriptor, invocationOperation.Syntax.GetLocation()));
    }
}
