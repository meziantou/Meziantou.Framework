using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

/// <summary>
/// Reports <c>FullPath.FromPath</c> calls whose argument already did the work.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RedundantFromPathAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: FullPathAnalyzerCommon.RedundantFromPathDiagnosticId,
        title: "Simplify the FullPath.FromPath call",
        messageFormat: "Use {0} instead",
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

            context.RegisterOperationAction(context => Analyze(context, analyzerContext), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, FullPathContext analyzerContext)
    {
        var invocationOperation = (IInvocationOperation)context.Operation;
        if (invocationOperation.TargetMethod is not { Name: "FromPath" } targetMethod || !analyzerContext.IsFullPathMember(targetMethod))
            return;

        if (invocationOperation.Arguments.Length != 1)
            return;

        var argument = invocationOperation.Arguments[0].Value;

        // FullPath.FromPath(fullPath) round-trips through string
        if (analyzerContext.IsFullPathType(argument))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptor, invocationOperation.Syntax.GetLocation(), "the FullPath value directly"));
            return;
        }

        if (analyzerContext.UnwrapToFullPath(argument) is not IInvocationOperation { TargetMethod: { IsStatic: true } innerMethod } innerInvocation)
            return;

        if (!SymbolEqualityComparer.Default.Equals(innerMethod.ContainingType, analyzerContext.PathType))
            return;

        // FullPath.FromPath already calls Path.GetFullPath
        if (innerMethod.Name is "GetFullPath" && innerInvocation.Arguments.Length == 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptor, invocationOperation.Syntax.GetLocation(), "FullPath.FromPath with the original path"));
            return;
        }

        if (innerMethod.Name is "Combine" or "Join")
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptor, invocationOperation.Syntax.GetLocation(), "FullPath.Combine"));
        }
    }
}
