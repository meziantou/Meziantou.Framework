using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.GlobalizationSensitivity;

/// <summary>
/// Reports the code whose behavior changes in invariant globalization mode, where .NET runs without ICU.
/// </summary>
/// <remarks>
/// CI only runs the tests of a project in invariant globalization mode when the project sets the
/// <c>GlobalizationSensitive</c> MSBuild property. A project that does not set it gets a diagnostic for each ICU-dependent
/// call, which it suppresses with a justification or answers by setting the property. A library that sets it writes
/// <see cref="MarkerMetadataKey"/> into its assembly, so the test projects that test it, or test a library that references
/// it, are required to set it too. The references a test project adds itself, such as test helpers, do not count. The
/// tested assembly is the referenced assembly whose name is the longest prefix of the test assembly name, or the
/// comma-separated assemblies listed in the <c>GlobalizationSensitiveTestedAssemblies</c> MSBuild property. The list cannot use
/// semicolons, which start a comment in the generated .editorconfig file.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GlobalizationSensitivityAnalyzer : DiagnosticAnalyzer
{
    public const string MarkerMetadataKey = "Meziantou.Framework.GlobalizationSensitive";

    private const string CultureReason = "needs culture data, which invariant globalization mode does not have";
    private const string ComparisonReason = "is a linguistic comparison, which invariant globalization mode makes ordinal";
    private const string NormalizationReason = "does nothing in invariant globalization mode on Linux and macOS";
    private const string TimeZoneReason = "needs ICU for time zone names and for the conversion between Windows and IANA IDs";
    private const string IdnReason = "skips part of the IDNA mapping in invariant globalization mode";

    // CompareOptions.OrdinalIgnoreCase and CompareOptions.Ordinal
    private const int OrdinalIgnoreCaseCompareOptions = 0x10000000;
    private const int OrdinalCompareOptions = 0x40000000;

    // The LCID of the invariant culture
    private const int InvariantCultureLcid = 0x007F;

    public static readonly DiagnosticDescriptor IcuDependentApiRule = new(
        id: "MFGS0001",
        title: "API depends on ICU",
        messageFormat: "'{0}' {1}. Suppress this diagnostic with a justification, or set GlobalizationSensitive to true in the project file so CI runs the tests in invariant globalization mode.",
        category: "Globalization",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GlobalizationSensitiveReferenceRule = new(
        id: "MFGS0002",
        title: "Test project tests code that depends on a globalization-sensitive assembly",
        messageFormat: "The tested code depends on globalization-sensitive assemblies ({0}). Set GlobalizationSensitive to true in the project file so CI runs the tests in invariant globalization mode.",
        category: "Globalization",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [IcuDependentApiRule, GlobalizationSensitiveReferenceRule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(context =>
        {
            var options = context.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
            if (IsTrue(options, "build_property.GlobalizationSensitive"))
                return;

            if (IsTrue(options, "build_property.IsTestProject"))
            {
                options.TryGetValue("build_property.GlobalizationSensitiveTestedAssemblies", out var testedAssemblyNames);
                context.RegisterCompilationEndAction(context => ReportGlobalizationSensitiveReferences(context, testedAssemblyNames));
            }

            var knownTypes = new KnownTypes(context.Compilation);
            context.RegisterOperationAction(context => AnalyzeInvocation(context, knownTypes), OperationKind.Invocation);
            context.RegisterOperationAction(context => AnalyzeObjectCreation(context, knownTypes), OperationKind.ObjectCreation);
            context.RegisterOperationAction(context => AnalyzePropertyReference(context, knownTypes), OperationKind.PropertyReference);
            context.RegisterOperationAction(context => AnalyzeFieldReference(context, knownTypes), OperationKind.FieldReference);
        });
    }

    private static bool IsTrue(AnalyzerConfigOptions options, string name)
    {
        return options.TryGetValue(name, out var value) && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static void ReportGlobalizationSensitiveReferences(CompilationAnalysisContext context, string? testedAssemblyNames)
    {
        var compilation = context.Compilation;
        var visited = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<IAssemblySymbol>(GetTestedAssemblies(compilation, testedAssemblyNames));
        var assemblyNames = new List<string>();
        while (pending.Count > 0)
        {
            var assembly = pending.Pop();
            if (!visited.Add(assembly))
                continue;

            if (HasGlobalizationSensitiveMarker(assembly))
            {
                assemblyNames.Add(assembly.Identity.Name);
            }

            // An assembly only references the assemblies its code uses, not every project it references
            foreach (var module in assembly.Modules)
            {
                foreach (var referencedAssembly in module.ReferencedAssemblySymbols)
                {
                    pending.Push(referencedAssembly);
                }
            }
        }

        if (assemblyNames.Count == 0)
            return;

        assemblyNames.Sort(StringComparer.Ordinal);
        context.ReportDiagnostic(Diagnostic.Create(GlobalizationSensitiveReferenceRule, Location.None, string.Join(", ", assemblyNames)));
    }

    private static List<IAssemblySymbol> GetTestedAssemblies(Compilation compilation, string? testedAssemblyNames)
    {
        var referencedAssemblies = new List<IAssemblySymbol>();
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
            {
                referencedAssemblies.Add(assembly);
            }
        }

        if (testedAssemblyNames is not null)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in testedAssemblyNames.Split(','))
            {
                var trimmedName = name.Trim();
                if (trimmedName.Length > 0)
                {
                    names.Add(trimmedName);
                }
            }

            if (names.Count > 0)
                return referencedAssemblies.Where(assembly => names.Contains(assembly.Identity.Name)).ToList();
        }

        // The tests of "Library" are named "Library.Tests", "Library.AotTests" or "LibraryTests"
        var testAssemblyName = compilation.AssemblyName;
        if (testAssemblyName is null)
            return [];

        IAssemblySymbol? testedAssembly = null;
        foreach (var assembly in referencedAssemblies)
        {
            var name = assembly.Identity.Name;
            if (IsTestedAssemblyName(testAssemblyName, name) && (testedAssembly is null || name.Length > testedAssembly.Identity.Name.Length))
            {
                testedAssembly = assembly;
            }
        }

        return testedAssembly is null ? [] : [testedAssembly];
    }

    private static bool IsTestedAssemblyName(string testAssemblyName, string name)
    {
        if (name.Length >= testAssemblyName.Length || !testAssemblyName.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            return false;

        return testAssemblyName[name.Length] == '.' || string.Compare(testAssemblyName, name.Length, "Tests", 0, "Tests".Length, StringComparison.OrdinalIgnoreCase) == 0;
    }

    private static bool HasGlobalizationSensitiveMarker(IAssemblySymbol assembly)
    {
        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: "AssemblyMetadataAttribute" } attributeClass || attributeClass.ContainingNamespace?.ToDisplayString() != "System.Reflection")
                continue;

            if (attribute.ConstructorArguments is [{ Value: MarkerMetadataKey }, { Value: string value }] && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, KnownTypes knownTypes)
    {
        var operation = (IInvocationOperation)context.Operation;
        var method = operation.TargetMethod;
        var type = method.ContainingType?.OriginalDefinition;
        if (type is null)
            return;

        string? reason = null;
        if (type.SpecialType is SpecialType.System_String)
        {
            reason = method.Name switch
            {
                "Normalize" or "IsNormalized" => NormalizationReason,
                _ when IsCultureSensitiveStringMethod(method, operation.Arguments, knownTypes) => ComparisonReason,
                _ => null,
            };
        }
        else if (Is(type, knownTypes.CultureInfo))
        {
            reason = method.Name switch
            {
                "GetCultures" => CultureReason,
                "GetCultureInfo" or "GetCultureInfoByIetfLanguageTag" or "CreateSpecificCulture" when !IsInvariantCultureIdentifier(operation.Arguments) => CultureReason,
                _ => null,
            };
        }
        else if (Is(type, knownTypes.CompareInfo))
        {
            reason = method.Name switch
            {
                "GetCompareInfo" => CultureReason,
                "Compare" or "IndexOf" or "LastIndexOf" or "IsPrefix" or "IsSuffix" or "GetSortKey" or "GetSortKeyLength" or "GetHashCode" or "IsSortable" when method.Parameters.Length > 0 && !HasOrdinalCompareOptions(operation.Arguments, knownTypes) => ComparisonReason,
                _ => null,
            };
        }
        else if (Is(type, knownTypes.StringComparer))
        {
            if (method.Name == "Create")
            {
                reason = ComparisonReason;
            }
        }
        else if (Is(type, knownTypes.StringNormalizationExtensions))
        {
            reason = NormalizationReason;
        }
        else if (Is(type, knownTypes.TimeZoneInfo))
        {
            reason = method.Name switch
            {
                "FindSystemTimeZoneById" or "TryFindSystemTimeZoneById" or "TryConvertIanaIdToWindowsId" or "TryConvertWindowsIdToIanaId" or "ConvertTimeBySystemTimeZoneId" or "GetSystemTimeZones" => TimeZoneReason,
                _ => null,
            };
        }

        if (reason is not null)
        {
            Report(context, operation, method, reason);
        }
    }

    private static bool IsCultureSensitiveStringMethod(IMethodSymbol method, ImmutableArray<IArgumentOperation> arguments, KnownTypes knownTypes)
    {
        if (method.Name is not ("Compare" or "CompareTo" or "StartsWith" or "EndsWith" or "IndexOf" or "LastIndexOf" or "Replace"))
            return false;

        var hasStringComparison = false;
        var hasCulture = false;
        var hasCompareOptions = false;
        foreach (var parameter in method.Parameters)
        {
            if (Is(parameter.Type, knownTypes.StringComparison))
            {
                hasStringComparison = true;
            }
            else if (Is(parameter.Type, knownTypes.CultureInfo))
            {
                hasCulture = true;
            }
            else if (Is(parameter.Type, knownTypes.CompareOptions))
            {
                hasCompareOptions = true;
            }
        }

        // The StringComparison values are reported where they are written
        if (hasStringComparison)
            return false;

        if (hasCompareOptions)
            return !HasOrdinalCompareOptions(arguments, knownTypes);

        if (hasCulture)
            return true;

        return method.Name switch
        {
            // The overloads without a comparison use the current culture
            "Compare" or "CompareTo" => true,

            // The char overloads are ordinal, the string ones use the current culture
            "StartsWith" or "EndsWith" or "IndexOf" or "LastIndexOf" => method.Parameters.Length > 0 && method.Parameters[0].Type.SpecialType is SpecialType.System_String,
            _ => false,
        };
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context, KnownTypes knownTypes)
    {
        var operation = (IObjectCreationOperation)context.Operation;
        if (operation.Constructor is not { } constructor)
            return;

        var type = constructor.ContainingType.OriginalDefinition;
        string? reason = null;
        if (Is(type, knownTypes.CultureInfo))
        {
            if (!IsInvariantCultureIdentifier(operation.Arguments))
            {
                reason = CultureReason;
            }
        }
        else if (Is(type, knownTypes.RegionInfo))
        {
            reason = CultureReason;
        }
        else if (Is(type, knownTypes.IdnMapping))
        {
            reason = IdnReason;
        }
        else if (Is(type, knownTypes.NonGenericComparer) || Is(type, knownTypes.CaseInsensitiveComparer))
        {
            reason = ComparisonReason;
        }

        if (reason is not null)
        {
            Report(context, operation, constructor, reason);
        }
    }

    private static void AnalyzePropertyReference(OperationAnalysisContext context, KnownTypes knownTypes)
    {
        var operation = (IPropertyReferenceOperation)context.Operation;
        var property = operation.Property;
        var type = property.ContainingType.OriginalDefinition;

        string? reason = null;
        if (Is(type, knownTypes.StringComparer))
        {
            reason = property.Name switch
            {
                "CurrentCulture" or "CurrentCultureIgnoreCase" or "InvariantCulture" or "InvariantCultureIgnoreCase" => ComparisonReason,
                _ => null,
            };
        }
        else if (Is(type, knownTypes.GenericComparer))
        {
            if (property.Name == "Default" && property.ContainingType.TypeArguments is [{ SpecialType: SpecialType.System_String }])
            {
                reason = ComparisonReason;
            }
        }
        else if (Is(type, knownTypes.CaseInsensitiveComparer))
        {
            reason = property.Name switch
            {
                "Default" or "DefaultInvariant" => ComparisonReason,
                _ => null,
            };
        }
        else if (Is(type, knownTypes.RegionInfo))
        {
            if (property.Name == "CurrentRegion")
            {
                reason = CultureReason;
            }
        }
        else if (Is(type, knownTypes.TimeZoneInfo))
        {
            reason = property.Name switch
            {
                "DisplayName" or "StandardName" or "DaylightName" => TimeZoneReason,
                _ => null,
            };
        }
        else if (Is(type, knownTypes.Uri))
        {
            if (property.Name == "IdnHost")
            {
                reason = IdnReason;
            }
        }

        if (reason is not null)
        {
            Report(context, operation, property, reason);
        }
    }

    private static void AnalyzeFieldReference(OperationAnalysisContext context, KnownTypes knownTypes)
    {
        var operation = (IFieldReferenceOperation)context.Operation;
        var field = operation.Field;
        var type = field.ContainingType.OriginalDefinition;

        string? reason = null;
        if (Is(type, knownTypes.StringComparison))
        {
            // Matching a value, as in "case StringComparison.InvariantCulture:", does not compare anything
            if (field.Name is "CurrentCulture" or "CurrentCultureIgnoreCase" or "InvariantCulture" or "InvariantCultureIgnoreCase" && !IsInPattern(operation))
            {
                reason = ComparisonReason;
            }
        }
        else if (Is(type, knownTypes.NonGenericComparer))
        {
            if (field.Name is "Default" or "DefaultInvariant")
            {
                reason = ComparisonReason;
            }
        }

        if (reason is not null)
        {
            Report(context, operation, field, reason);
        }
    }

    private static bool IsInPattern(IOperation operation)
    {
        for (var parent = operation.Parent; parent is not null; parent = parent.Parent)
        {
            switch (parent)
            {
                case IPatternOperation:
                case ICaseClauseOperation:
                    return true;
                case IConversionOperation:
                    continue;
                default:
                    return false;
            }
        }

        return false;
    }

    private static bool IsInvariantCultureIdentifier(ImmutableArray<IArgumentOperation> arguments)
    {
        foreach (var argument in arguments)
        {
            if (argument.Parameter?.Ordinal != 0)
                continue;

            var constantValue = argument.Value.ConstantValue;
            return constantValue is { HasValue: true, Value: "" or InvariantCultureLcid };
        }

        return false;
    }

    private static bool HasOrdinalCompareOptions(ImmutableArray<IArgumentOperation> arguments, KnownTypes knownTypes)
    {
        foreach (var argument in arguments)
        {
            if (argument.Parameter is null || !Is(argument.Parameter.Type, knownTypes.CompareOptions))
                continue;

            var constantValue = argument.Value.ConstantValue;
            return constantValue is { HasValue: true, Value: OrdinalCompareOptions or OrdinalIgnoreCaseCompareOptions };
        }

        return false;
    }

    private static bool Is(ITypeSymbol? type, INamedTypeSymbol? expected)
    {
        return expected is not null && SymbolEqualityComparer.Default.Equals(type, expected);
    }

    private static void Report(OperationAnalysisContext context, IOperation operation, ISymbol symbol, string reason)
    {
        var name = symbol is IMethodSymbol { MethodKind: MethodKind.Constructor }
            ? "new " + symbol.ContainingType.Name
            : symbol.ContainingType.Name + "." + symbol.Name;

        context.ReportDiagnostic(Diagnostic.Create(IcuDependentApiRule, operation.Syntax.GetLocation(), name, reason));
    }

    private sealed class KnownTypes(Compilation compilation)
    {
        public INamedTypeSymbol? CultureInfo { get; } = compilation.GetTypeByMetadataName("System.Globalization.CultureInfo");
        public INamedTypeSymbol? RegionInfo { get; } = compilation.GetTypeByMetadataName("System.Globalization.RegionInfo");
        public INamedTypeSymbol? CompareInfo { get; } = compilation.GetTypeByMetadataName("System.Globalization.CompareInfo");
        public INamedTypeSymbol? CompareOptions { get; } = compilation.GetTypeByMetadataName("System.Globalization.CompareOptions");
        public INamedTypeSymbol? IdnMapping { get; } = compilation.GetTypeByMetadataName("System.Globalization.IdnMapping");
        public INamedTypeSymbol? StringComparison { get; } = compilation.GetTypeByMetadataName("System.StringComparison");
        public INamedTypeSymbol? StringComparer { get; } = compilation.GetTypeByMetadataName("System.StringComparer");
        public INamedTypeSymbol? StringNormalizationExtensions { get; } = compilation.GetTypeByMetadataName("System.StringNormalizationExtensions");
        public INamedTypeSymbol? TimeZoneInfo { get; } = compilation.GetTypeByMetadataName("System.TimeZoneInfo");
        public INamedTypeSymbol? Uri { get; } = compilation.GetTypeByMetadataName("System.Uri");
        public INamedTypeSymbol? GenericComparer { get; } = compilation.GetTypeByMetadataName("System.Collections.Generic.Comparer`1");
        public INamedTypeSymbol? NonGenericComparer { get; } = compilation.GetTypeByMetadataName("System.Collections.Comparer");
        public INamedTypeSymbol? CaseInsensitiveComparer { get; } = compilation.GetTypeByMetadataName("System.Collections.CaseInsensitiveComparer");
    }
}
