---
title: Introduction
description: A source-only package of extension methods and types for writing Roslyn analyzers, code fixes and source generators.
---

`Meziantou.Framework.Roslyn` is a set of helpers for the projects that use the Roslyn APIs: analyzers, code fixes,
suppressors and source generators. The package ships C# source files instead of an assembly: they are compiled into
your project as `internal` types, so an analyzer has no extra assembly to load and no version conflict with another
analyzer that uses the package.

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

In this analyzer, `GetBestTypeByMetadataName`, `IsOrInheritsFrom` and the `ReportDiagnostic` overload come from the
package.

## What you can do with it

- **Query symbols and types**: inheritance, interfaces, assignability, attributes, overrides, visibility... See
  [Symbols and types](03-SymbolsAndTypes.md).
- **Navigate operations and syntax**: unwrap conversions, find the value of a local, parenthesize an expression. See
  [Operations and syntax](04-OperationsAndSyntax.md).
- **Recognize generated code** as the analyzer driver does. See [Generated code](05-GeneratedCode.md).
- **Report diagnostics** on the right part of a declaration or an invocation, and filter them globally. See
  [Reporting diagnostics](06-ReportingDiagnostics.md).
- **Write code for several Roslyn versions** with the `ROSLYN_*` and `CSHARP*` constants. See
  [Build configuration](07-BuildConfiguration.md).

## Next steps

- [Getting started](02-GettingStarted.md): install the package in an analyzer project.
