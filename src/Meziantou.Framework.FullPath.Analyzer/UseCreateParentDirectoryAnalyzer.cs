using System.Collections.Immutable;
using Meziantou.Framework.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

/// <summary>
/// Reports <c>Directory.CreateDirectory(fullPath.Parent)</c>, for which a dedicated method exists.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseCreateParentDirectoryAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: FullPathAnalyzerCommon.UseCreateParentDirectoryDiagnosticId,
        title: "Use FullPath.CreateParentDirectory instead of Directory.CreateDirectory",
        messageFormat: "Use FullPath.CreateParentDirectory instead of calling Directory.CreateDirectory with the parent path",
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
        if (invocationOperation.TargetMethod is not { IsStatic: true, Name: "CreateDirectory" } targetMethod)
            return;

        if (!SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, analyzerContext.DirectoryType))
            return;

        if (invocationOperation.Arguments.Length == 0)
            return;

        // CreateParentDirectory returns void, so the DirectoryInfo must not be used
        if (invocationOperation.Parent is not IExpressionStatementOperation)
            return;

        if (analyzerContext.UnwrapToFullPath(invocationOperation.Arguments[0].Value) is not IPropertyReferenceOperation { Property.Name: "Parent", Instance: { } instance } property)
            return;

        if (!analyzerContext.IsFullPathType(property.Property.ContainingType) || !analyzerContext.IsFullPathType(instance.Type))
            return;

        context.ReportDiagnostic(Descriptor, invocationOperation);
    }
}
