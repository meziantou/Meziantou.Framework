using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PathGetRelativePathWithFullPathAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: FullPathAnalyzerCommon.PathGetRelativePathWithFullPathDiagnosticId,
        title: "Use FullPath.MakePathRelativeTo instead of Path.GetRelativePath",
        messageFormat: "Use FullPath.MakePathRelativeTo instead of calling Path.GetRelativePath",
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
        if (invocationOperation.TargetMethod is not { IsStatic: true, Name: "GetRelativePath" } targetMethod)
            return;

        if (!SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, analyzerContext.PathType))
            return;

        if (invocationOperation.Arguments.Length != 2)
            return;

        if (!analyzerContext.IsFullPathType(invocationOperation.Arguments[0].Value))
            return;

        if (!analyzerContext.IsFullPathType(invocationOperation.Arguments[1].Value))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Descriptor, invocationOperation.Syntax.GetLocation()));
    }
}
