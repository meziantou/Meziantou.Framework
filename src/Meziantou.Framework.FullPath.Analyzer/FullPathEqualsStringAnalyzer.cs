using System.Collections.Immutable;
using Meziantou.Framework.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

/// <summary>
/// Reports <c>fullPath.Equals(someString)</c>, which always returns <see langword="false"/>.
/// </summary>
/// <remarks>
/// A <see cref="string"/> argument makes the call bind to <c>FullPath.Equals(object)</c>, whose body is
/// <c>obj is FullPath path &amp;&amp; Equals(path)</c>. A <see cref="string"/> is never a <c>FullPath</c>.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FullPathEqualsStringAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: FullPathAnalyzerCommon.FullPathEqualsStringDiagnosticId,
        title: "FullPath.Equals with a string argument is always false",
        messageFormat: "This calls FullPath.Equals(object) with a string and always returns false",
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
        if (invocationOperation.TargetMethod is not { Name: "Equals", Parameters.Length: 1 } targetMethod)
            return;

        if (targetMethod.Parameters[0].Type.SpecialType != SpecialType.System_Object)
            return;

        // The receiver must be a FullPath itself; fullPath.Value.Equals(x) is a string comparison
        if (invocationOperation.Instance is not { } instance || !analyzerContext.IsFullPathType(instance.Type))
            return;

        // An 'object' argument may hold a boxed FullPath at run time, a string never can
        if (analyzerContext.UnwrapToFullPath(invocationOperation.Arguments[0].Value).Type?.SpecialType != SpecialType.System_String)
            return;

        context.ReportDiagnostic(Descriptor, invocationOperation);
    }
}
