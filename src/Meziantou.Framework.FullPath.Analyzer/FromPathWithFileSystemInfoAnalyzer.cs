using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

/// <summary>
/// Reports <c>FullPath.FromPath(fileSystemInfo.FullName)</c>, for which a dedicated factory exists.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FromPathWithFileSystemInfoAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: FullPathAnalyzerCommon.FromPathWithFileSystemInfoDiagnosticId,
        title: "Use FullPath.FromFileSystemInfo instead of FullPath.FromPath",
        messageFormat: "Use FullPath.FromFileSystemInfo instead of passing FullName to FullPath.FromPath",
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
            if (!analyzerContext.IsValid || analyzerContext.FileSystemInfoType is null)
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

        if (invocationOperation.Arguments[0].Value is not IPropertyReferenceOperation { Property.Name: "FullName", Instance: not null } propertyReference)
            return;

        if (!analyzerContext.IsFileSystemInfo(propertyReference.Instance.Type))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Descriptor, invocationOperation.Syntax.GetLocation()));
    }
}
