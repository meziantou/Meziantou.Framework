using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

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
        context.RegisterCompilationStartAction(context =>
        {
            var fastEnumAttribute = context.Compilation.GetTypeByMetadataName("Meziantou.Framework.Annotations.FastEnumAttribute");
            if (fastEnumAttribute is null)
                return;

            context.RegisterOperationAction(context => AnalyzeAttributeOperation(context, fastEnumAttribute), OperationKind.Attribute);
        });
    }

    private static void AnalyzeAttributeOperation(OperationAnalysisContext context, INamedTypeSymbol fastEnumAttribute)
    {
        if (context.Operation is not IAttributeOperation attributeOperation)
            return;

        if (attributeOperation.Operation is not IObjectCreationOperation attribute)
            return;

        if (!SymbolEqualityComparer.Default.Equals(attribute.Constructor?.ContainingType, fastEnumAttribute))
            return;

        AnalyzeAttribute(context, attribute);
    }

    private static void AnalyzeAttribute(OperationAnalysisContext context, IObjectCreationOperation attribute)
    {
        if (attribute.Arguments.Length != 1)
            return;

        var location = attribute.Syntax.GetLocation();
        var argument = attribute.Arguments[0].Value;
        while (argument is IConversionOperation conversionOperation)
        {
            argument = conversionOperation.Operand;
        }

        if (argument.ConstantValue is { HasValue: true, Value: null })
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidEnumType, location, "(null)"));
            return;
        }

        if (argument is not ITypeOfOperation typeOfOperation || typeOfOperation.TypeOperand.TypeKind == TypeKind.Enum)
            return;

        context.ReportDiagnostic(Diagnostic.Create(InvalidEnumType, location, typeOfOperation.TypeOperand.ToDisplayString()));
    }
}
