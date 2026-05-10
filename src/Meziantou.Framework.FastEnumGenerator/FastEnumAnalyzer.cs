using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.FastEnumGenerator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FastEnumAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor InvalidEnumType = new(
        id: "MFEG0001",
        title: "FastEnum target type is invalid",
        messageFormat: "The type '{0}' is not a valid enum type for FastEnum generation",
        category: "FastEnumGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(InvalidEnumType);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(InitializeCore);
    }

    private static void InitializeCore(CompilationStartAnalysisContext context)
    {
        var fastEnumAttribute = context.Compilation.GetTypeByMetadataName("Meziantou.Framework.Annotations.FastEnumAttribute");
        if (fastEnumAttribute is null)
            return;

        context.RegisterCompilationEndAction(context => AnalyzeCompilation(context, fastEnumAttribute));
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context, INamedTypeSymbol fastEnumAttribute)
    {
        foreach (var attribute in context.Compilation.Assembly.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, fastEnumAttribute))
                continue;

            AnalyzeAttribute(context, attribute);
        }
    }

    private static void AnalyzeAttribute(CompilationAnalysisContext context, AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length != 1)
            return;

        var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? Location.None;
        var enumType = attribute.ConstructorArguments[0].Value;
        if (enumType is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidEnumType, location, "(null)"));
            return;
        }

        if (enumType is not ITypeSymbol typeSymbol || typeSymbol.TypeKind == TypeKind.Enum)
            return;

        context.ReportDiagnostic(Diagnostic.Create(InvalidEnumType, location, typeSymbol.ToDisplayString()));
    }
}
