using System.Collections.Immutable;
using Meziantou.Framework.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.StronglyTypedId;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StronglyTypedIdAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor UnsupportedType = new(
        id: "MFSTID0001",
        title: "Not supported type",
        messageFormat: "The type '{0}' is not supported",
        category: "StronglyTypedId",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedGuidGenerationStrategy = new(
        id: "MFSTID0002",
        title: "Guid generation strategy is not supported by the target framework",
        messageFormat: "'Guid.CreateVersion7()' is not available in the target framework, so 'GuidGenerationStrategy.Version7' cannot be used",
        category: "StronglyTypedId",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnusedGuidGenerationStrategy = new(
        id: "MFSTID0003",
        title: "Guid generation strategy is only applicable to Guid",
        messageFormat: "'GuidGenerationStrategy' has no effect as the underlying type is '{0}' instead of 'System.Guid'",
        category: "StronglyTypedId",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedGenericType = new(
        id: "MFSTID0004",
        title: "Generic types are not supported",
        messageFormat: "The type '{0}' cannot be a strongly-typed id because it is generic or nested in a generic type",
        category: "StronglyTypedId",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "An attribute argument cannot use a type parameter (CS0416), so the generated converters could not be associated with the type.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(UnsupportedType, UnsupportedGuidGenerationStrategy, UnusedGuidGenerationStrategy, UnsupportedGenericType);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(InitializeCore);
    }

    private static void InitializeCore(CompilationStartAnalysisContext context)
    {
        var nonGenericAttribute = context.Compilation.GetTypeByMetadataName(StronglyTypedIdSourceGenerator.StronglyTypedIdAttributeName);
        var genericAttribute = context.Compilation.GetTypeByMetadataName(StronglyTypedIdSourceGenerator.StronglyTypedIdGenericAttributeName);
        if (nonGenericAttribute is null && genericAttribute is null)
            return;

        var supportGuidCreateVersion7 = context.Compilation.SupportGuidCreateVersion7();
        if (!supportGuidCreateVersion7 && context.Compilation.GetTypeByMetadataName(StronglyTypedIdSourceGenerator.StronglyTypedIdDefaultsAttributeName) is { } defaultsAttribute)
        {
            context.RegisterCompilationEndAction(context => AnalyzeAssemblyDefaults(context, defaultsAttribute));
        }

        context.RegisterSymbolAction(context => AnalyzeSymbol(context, nonGenericAttribute, genericAttribute, supportGuidCreateVersion7), SymbolKind.NamedType);
    }

    private static void AnalyzeAssemblyDefaults(CompilationAnalysisContext context, INamedTypeSymbol defaultsAttribute)
    {
        foreach (var attribute in context.Compilation.Assembly.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, defaultsAttribute))
                continue;

            if (GetGuidGenerationStrategy(attribute) is not StronglyTypedIdSourceGenerator.GuidGenerationStrategy.Version7)
                continue;

            var location = GetNamedArgumentLocation(attribute, StronglyTypedIdSourceGenerator.GuidGenerationStrategyPropertyName, context.CancellationToken)
                ?? attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation();
            if (location is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(UnsupportedGuidGenerationStrategy, location));
            }
        }
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context, INamedTypeSymbol? nonGenericAttribute, INamedTypeSymbol? genericAttribute, bool supportGuidCreateVersion7)
    {
        var symbol = (INamedTypeSymbol)context.Symbol;
        foreach (var attribute in symbol.GetAttributes())
        {
            var typeSymbol = TryGetIdTypeSymbol(attribute, nonGenericAttribute, genericAttribute);
            if (typeSymbol is null)
                continue;

            if (StronglyTypedIdSourceGenerator.IsGenericTypeOrNestedInGenericType(symbol))
            {
                var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? symbol.Locations.FirstOrDefault();
                if (location is not null)
                {
                    context.ReportDiagnostic(UnsupportedGenericType, location, symbol.ToDisplayString());
                }

                continue;
            }

            var idType = StronglyTypedIdSourceGenerator.GetIdType(context.Compilation, typeSymbol);
            if (idType is StronglyTypedIdSourceGenerator.IdType.Unknown)
            {
                var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? symbol.Locations.FirstOrDefault();
                if (location is not null)
                {
                    context.ReportDiagnostic(UnsupportedType, location, typeSymbol.ToDisplayString());
                }

                continue;
            }

            var guidGenerationStrategy = GetGuidGenerationStrategy(attribute);
            if (guidGenerationStrategy is null)
                continue;

            if (idType is not StronglyTypedIdSourceGenerator.IdType.System_Guid)
            {
                ReportGuidGenerationStrategyDiagnostic(UnusedGuidGenerationStrategy, typeSymbol.ToDisplayString());
            }
            else if (!supportGuidCreateVersion7 && guidGenerationStrategy is StronglyTypedIdSourceGenerator.GuidGenerationStrategy.Version7)
            {
                ReportGuidGenerationStrategyDiagnostic(UnsupportedGuidGenerationStrategy);
            }

            void ReportGuidGenerationStrategyDiagnostic(DiagnosticDescriptor descriptor, params object?[] messageArgs)
            {
                var location = GetNamedArgumentLocation(attribute, StronglyTypedIdSourceGenerator.GuidGenerationStrategyPropertyName, context.CancellationToken)
                    ?? attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                    ?? symbol.Locations.FirstOrDefault();
                if (location is not null)
                {
                    context.ReportDiagnostic(descriptor, location, messageArgs);
                }
            }
        }
    }

    private static StronglyTypedIdSourceGenerator.GuidGenerationStrategy? GetGuidGenerationStrategy(AttributeData attribute)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key == StronglyTypedIdSourceGenerator.GuidGenerationStrategyPropertyName && namedArgument.Value.Value is int value)
                return (StronglyTypedIdSourceGenerator.GuidGenerationStrategy)value;
        }

        return null;
    }

    private static Location? GetNamedArgumentLocation(AttributeData attribute, string name, CancellationToken cancellationToken)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is not AttributeSyntax { ArgumentList: not null } attributeSyntax)
            return null;

        foreach (var argument in attributeSyntax.ArgumentList.Arguments)
        {
            if (argument.NameEquals?.Name.Identifier.ValueText == name)
                return argument.GetLocation();
        }

        return null;
    }

    private static ITypeSymbol? TryGetIdTypeSymbol(AttributeData attribute, INamedTypeSymbol? nonGenericAttribute, INamedTypeSymbol? genericAttribute)
    {
        if (genericAttribute is not null &&
            attribute.AttributeClass is { Arity: 1 } genericClass &&
            SymbolEqualityComparer.Default.Equals(genericClass.OriginalDefinition, genericAttribute) &&
            genericClass.TypeArguments.Length == 1)
        {
            return genericClass.TypeArguments[0];
        }

        if (nonGenericAttribute is null ||
            !SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, nonGenericAttribute) ||
            attribute.ConstructorArguments.Length != 6 ||
            attribute.ConstructorArguments[0].Value is not ITypeSymbol type)
        {
            return null;
        }

        return type;
    }
}
