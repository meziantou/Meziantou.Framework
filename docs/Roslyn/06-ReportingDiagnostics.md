---
title: Reporting diagnostics
description: Report diagnostics on symbols, operations, syntax and attributes with the right location, and filter them globally.
---

## Report a diagnostic

The package adds `ReportDiagnostic` extension methods to every analysis context. They take a `DiagnosticDescriptor`, the
element to report on and the arguments of the message, and compute the location for you:

<!-- ref:../../samples/Meziantou.Framework.Roslyn.Samples/AvoidTaskWaitAnalyzer.cs;region=Roslyn_Analyzer;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

The element can be a `SyntaxNode`, a `SyntaxToken`, a `Location`, a `SyntaxReference`, an `ISymbol`, an `IOperation`,
an `AttributeData` or a list of locations. Each overload also has a version with a `properties` dictionary, which a code
fix reads from the diagnostic.

## Choose the location

For a symbol, the diagnostic is reported on its first location in source code. Some overloads take an option to report
on a precise part of the declaration, so the IDE underlines what the diagnostic is about:

| Element | Options |
| --- | --- |
| `IMethodSymbol`, `ILocalFunctionOperation` | `DiagnosticMethodReportOptions.ReportOnMethodName`, `ReportOnReturnType` |
| `IPropertySymbol` | `DiagnosticPropertyReportOptions.ReportOnReturnType` |
| `IFieldSymbol` | `DiagnosticFieldReportOptions.ReportOnReturnType` |
| `IParameterSymbol` | `DiagnosticParameterReportOptions.ReportOnType` |
| `IInvocationOperation` | `DiagnosticInvocationReportOptions.ReportOnMember`, `ReportOnArguments` |

The options are flags. With `ReportOnMember | ReportOnArguments`, the location spans from the name of the method to the
end of the argument list. When the option does not apply, for instance `ReportOnMember` on a call without a member
access, the whole element is used.

## Report from shared code

`DiagnosticReporter` wraps any analysis context that can report a diagnostic, and converts implicitly from them:
`SyntaxNodeAnalysisContext`, `SymbolAnalysisContext`, `OperationAnalysisContext`, `OperationBlockAnalysisContext`,
`CompilationAnalysisContext`, `SemanticModelAnalysisContext`, `SyntaxTreeAnalysisContext`, `CodeBlockAnalysisContext`
and `AdditionalFileAnalysisContext`. A helper method that takes a `DiagnosticReporter` can be called from any action,
with the same `ReportDiagnostic` overloads.

## Filter diagnostics

`DiagnosticReporter.CanReportDiagnostic` is a filter applied to every diagnostic reported through a `DiagnosticReporter`
or the `ReportDiagnostic` extension methods. Returning `false` drops the diagnostic. For instance, to never report in
generated files:

<!-- ref:../../tests/Meziantou.Framework.Roslyn.Tests/RoslynHelperTests.cs;region=Roslyn_CanReportDiagnostic;format=md-fenced-code-block;dedent=true -->
```csharp
DiagnosticReporter.CanReportDiagnostic = (diagnostic, options, cancellationToken)
    => diagnostic.Location.SourceTree?.IsGeneratedCode(options, cancellationToken) is not true;
```
<!-- endref -->

- The filter is `null` by default: nothing is filtered.
- The delegate receives the `Diagnostic`, so `diagnostic.Descriptor` and `diagnostic.Location.SourceTree` are available,
  and the `AnalyzerOptions` and `CancellationToken` of the context that reports it.
- Set it once, when the analyzer is initialized. As the types of the package are compiled into your assembly, the filter
  only applies to your analyzers.
- A diagnostic reported directly through a Roslyn method, such as `SymbolAnalysisContext.ReportDiagnostic(Diagnostic)`,
  does not go through the filter.
