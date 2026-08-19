using System.Collections.Immutable;
using Meziantou.Framework.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

/// <summary>
/// Reports path-producing BCL methods that have a <c>FullPath</c> equivalent.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseFullPathFactoryAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: FullPathAnalyzerCommon.UseFullPathFactoryDiagnosticId,
        title: "Use the FullPath equivalent of Path.GetTempPath or Environment.GetFolderPath",
        messageFormat: "Use FullPath.{0} instead of calling {1}.{0}",
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
        var targetMethod = invocationOperation.TargetMethod;
        if (analyzerContext.GetFullPathFactoryEquivalentTypeName(targetMethod) is not { } declaringTypeName)
            return;

        context.ReportDiagnostic(Descriptor, invocationOperation, targetMethod.Name, declaringTypeName);
    }
}
