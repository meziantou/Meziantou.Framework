using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Meziantou.Framework.Collections;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.Csv.SourceGeneration;

/// <summary>
/// Generates the metadata of the types declared with <c>CsvSerializableAttribute</c> on a class derived from <c>CsvSerializerContext</c>.
/// The generated code calls <c>CsvMetadataServices</c> with the same members, constructors and converters as the reflection-based metadata.
/// </summary>
[Generator]
public sealed class CsvSerializerContextGenerator : IIncrementalGenerator
{
    private const string SerializationNamespace = "Meziantou.Framework.Csv.Serialization";
    private const string CsvSerializableAttributeMetadataName = SerializationNamespace + ".CsvSerializableAttribute";
    private const string Category = "Meziantou.Framework.Csv.SourceGeneration";
    private const string Global = "global::" + SerializationNamespace;
    private const string Metadata = Global + ".Metadata";

    internal static readonly DiagnosticDescriptor ContextMustBePartial = new(
        id: "MFCSV001",
        title: "CSV serializer context must be partial",
        messageFormat: "Type '{0}' derives from CsvSerializerContext and must be declared partial, as must its containing types",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ContextMustDeriveFromCsvSerializerContext = new(
        id: "MFCSV002",
        title: "CsvSerializableAttribute is only valid on a CSV serializer context",
        messageFormat: "Type '{0}' has CsvSerializableAttribute but does not derive from Meziantou.Framework.Csv.Serialization.CsvSerializerContext",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ContextMustNotBeGeneric = new(
        id: "MFCSV003",
        title: "CSV serializer context must not be generic",
        messageFormat: "Type '{0}' derives from CsvSerializerContext and must not be generic",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor TypeNotSupported = new(
        id: "MFCSV004",
        title: "The type cannot be serialized",
        messageFormat: "Type '{0}' cannot be serialized: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor InvalidConverter = new(
        id: "MFCSV005",
        title: "Invalid converter type",
        messageFormat: "The converter '{0}' of member '{1}' must derive from CsvConverter<T> or CsvConverterFactory and have a public parameterless constructor",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor MemberNotSupported = new(
        id: "MFCSV006",
        title: "The member cannot be serialized",
        messageFormat: "Member '{0}' cannot be serialized: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateColumnName = new(
        id: "MFCSV007",
        title: "Duplicate column name",
        messageFormat: "Type '{0}' has more than one member with the column name '{1}': only the first one is read",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateTypeInfoPropertyName = new(
        id: "MFCSV008",
        title: "Duplicate type info property name",
        messageFormat: "The context '{0}' declares more than one type with the property name '{1}': set TypeInfoPropertyName",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly HashSet<string> BuiltInTypes = new(StringComparer.Ordinal)
    {
        "string", "bool", "char", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong", "nint", "nuint", "float", "double", "decimal",
        "byte[]", "global::System.IntPtr", "global::System.UIntPtr", "global::System.Int128", "global::System.UInt128", "global::System.Half",
        "global::System.Numerics.BigInteger", "global::System.Guid", "global::System.DateTime", "global::System.DateTimeOffset", "global::System.DateOnly",
        "global::System.TimeOnly", "global::System.TimeSpan", "global::System.Uri", "global::System.Version",
    };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var contexts = context.SyntaxProvider.ForAttributeWithMetadataName(
            CsvSerializableAttributeMetadataName,
            (node, _) => node is ClassDeclarationSyntax,
            (ctx, cancellationToken) => CreateContextOutput(ctx, cancellationToken));

        var outputs = contexts.Collect().Select((contexts, _) => Deduplicate(contexts));

        context.RegisterSourceOutput(outputs, (spc, outputs) =>
        {
            foreach (var output in outputs)
            {
                if (output.HintName is not null && output.Source is not null)
                {
                    spc.AddSource(output.HintName, output.Source);
                }
            }
        });

        var diagnostics = outputs.Select((outputs, _) => outputs.SelectMany(output => output.Diagnostics).Distinct().ToImmutableEquatableArray());
        context.RegisterSourceOutput(diagnostics.Combine(context.CompilationProvider), (spc, input) =>
        {
            if (input.Left.Length == 0)
                return;

            var trees = new Dictionary<string, SyntaxTree?>(StringComparer.Ordinal);
            foreach (var tree in input.Right.SyntaxTrees)
            {
                trees[tree.FilePath] = trees.ContainsKey(tree.FilePath) ? null : tree;
            }

            foreach (var diagnostic in input.Left)
            {
                spc.ReportDiagnostic(diagnostic.ToDiagnostic(trees));
            }
        });
    }

    private static ImmutableEquatableArray<ContextOutput> Deduplicate(ImmutableArray<ContextOutput?> contexts)
    {
        // A context is found once per partial declaration with the attribute
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ContextOutput>();
        foreach (var output in contexts)
        {
            if (output is not null && keys.Add(output.Key))
            {
                result.Add(output);
            }
        }

        return result.ToImmutableEquatableArray();
    }

    private static ContextOutput? CreateContextOutput(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.TargetSymbol is not INamedTypeSymbol contextSymbol)
            return null;

        var compilation = context.SemanticModel.Compilation;
        var key = contextSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var diagnostics = new List<DiagnosticInfo>();
        var location = contextSymbol.Locations.FirstOrDefault();

        var baseContext = compilation.GetTypeByMetadataName(SerializationNamespace + ".CsvSerializerContext");
        if (baseContext is null || !InheritsFrom(contextSymbol, baseContext))
        {
            diagnostics.Add(DiagnosticInfo.Create(ContextMustDeriveFromCsvSerializerContext, location, contextSymbol.Name));
            return new ContextOutput(key, null, null, diagnostics.ToImmutableEquatableArray());
        }

        if (!IsPartial(contextSymbol, cancellationToken))
        {
            diagnostics.Add(DiagnosticInfo.Create(ContextMustBePartial, location, contextSymbol.Name));
            return new ContextOutput(key, null, null, diagnostics.ToImmutableEquatableArray());
        }

        if (contextSymbol.IsGenericType || HasGenericContainingType(contextSymbol))
        {
            diagnostics.Add(DiagnosticInfo.Create(ContextMustNotBeGeneric, location, contextSymbol.Name));
            return new ContextOutput(key, null, null, diagnostics.ToImmutableEquatableArray());
        }

        var emitter = new ContextEmitter(compilation, contextSymbol, diagnostics, cancellationToken);
        var source = emitter.Emit();
        var hintName = (key.StartsWith("global::", StringComparison.Ordinal) ? key.Substring("global::".Length) : key).Replace('<', '_').Replace('>', '_') + ".g.cs";
        return new ContextOutput(key, hintName, source, diagnostics.ToImmutableEquatableArray());
    }

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
        }

        return false;
    }

    private static bool IsPartial(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            foreach (var reference in current.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration && !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                    return false;
            }
        }

        return true;
    }

    private static bool HasGenericContainingType(INamedTypeSymbol type)
    {
        for (var current = type.ContainingType; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType)
                return true;
        }

        return false;
    }

    internal static string Fqn(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    internal static string Literal(string? value) => value is null ? "null" : SymbolDisplay.FormatLiteral(value, quote: true);

    internal static string Literal(int value) => value.ToString(CultureInfo.InvariantCulture);

    internal static bool IsBuiltIn(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            return IsBuiltIn(nullable.TypeArguments[0]);

        return BuiltInTypes.Contains(Fqn(type));
    }

    private sealed record ContextOutput(string Key, string? HintName, string? Source, ImmutableEquatableArray<DiagnosticInfo> Diagnostics);

    internal sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
    {
        public static LocationInfo? Create(Location? location)
            => location is { SourceTree: { } tree } ? new LocationInfo(tree.FilePath, location.SourceSpan, location.GetLineSpan().Span) : null;

        public Location ToLocation(Dictionary<string, SyntaxTree?> trees)
            => trees.TryGetValue(FilePath, out var tree) && tree is not null ? Location.Create(tree, TextSpan) : Location.Create(FilePath, TextSpan, LineSpan);
    }

    internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, ImmutableEquatableArray<string> Arguments)
    {
        public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, Location? location, params object?[] arguments)
            => new(descriptor, LocationInfo.Create(location), arguments.Select(argument => argument?.ToString() ?? string.Empty).ToImmutableEquatableArray());

        public Diagnostic ToDiagnostic(Dictionary<string, SyntaxTree?> trees) => Diagnostic.Create(Descriptor, Location?.ToLocation(trees), [.. Arguments]);
    }

    private sealed class ContextEmitter
    {
        private readonly Compilation _compilation;
        private readonly INamedTypeSymbol _context;
        private readonly List<DiagnosticInfo> _diagnostics;
        private readonly CancellationToken _cancellationToken;
        private readonly StringBuilder _accessors = new();
        private readonly INamedTypeSymbol? _converterBase;
        private readonly INamedTypeSymbol? _converterOfT;
        private readonly INamedTypeSymbol? _converterFactory;
        private readonly INamedTypeSymbol? _spanParsable;
        private readonly INamedTypeSymbol? _spanFormattable;
        private readonly string _extern;
        private int _accessorIndex;
        private bool _includeFields;

        public ContextEmitter(Compilation compilation, INamedTypeSymbol context, List<DiagnosticInfo> diagnostics, CancellationToken cancellationToken)
        {
            _compilation = compilation;
            _context = context;
            _diagnostics = diagnostics;
            _cancellationToken = cancellationToken;
            _converterBase = compilation.GetTypeByMetadataName(SerializationNamespace + ".CsvConverter");
            _converterOfT = compilation.GetTypeByMetadataName(SerializationNamespace + ".CsvConverter`1");
            _converterFactory = compilation.GetTypeByMetadataName(SerializationNamespace + ".CsvConverterFactory");
            _spanParsable = compilation.GetTypeByMetadataName("System.ISpanParsable`1");
            _spanFormattable = compilation.GetTypeByMetadataName("System.ISpanFormattable");

            // Under the updated memory safety rules, an extern member must be marked safe or unsafe, and older language versions reject both
            _extern = context.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree.Options.Features.ContainsKey("updated-memory-safety-rules") == true ? "safe extern " : "extern ";
        }

        public string Emit()
        {
            var contextName = _context.Name;
            var contextFqn = Fqn(_context);
            var location = _context.Locations.FirstOrDefault();

            var optionsCode = new StringBuilder();
            optionsCode.Append("new ").Append(Global).Append(".CsvSerializerOptions");
            var optionsAttribute = _context.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == SerializationNamespace + ".CsvSourceGenerationOptionsAttribute");
            var optionAssignments = new List<string>();
            if (optionsAttribute is not null)
            {
                foreach (var argument in optionsAttribute.NamedArguments)
                {
                    var value = argument.Value.Value;
                    switch (argument.Key)
                    {
                        case "Separator" when value is char separator:
                            optionAssignments.Add("Separator = " + SymbolDisplay.FormatLiteral(separator, quote: true));
                            break;
                        case "HasHeaderRow" when value is bool hasHeaderRow:
                            optionAssignments.Add("HasHeaderRow = " + (hasHeaderRow ? "true" : "false"));
                            break;
                        case "NewLine" when value is string newLine:
                            optionAssignments.Add("NewLine = " + Literal(newLine));
                            break;
                        case "PropertyNameCaseInsensitive" when value is bool caseInsensitive:
                            optionAssignments.Add("PropertyNameCaseInsensitive = " + (caseInsensitive ? "true" : "false"));
                            break;
                        case "IncludeFields" when value is bool includeFields:
                            _includeFields = includeFields;
                            optionAssignments.Add("IncludeFields = " + (includeFields ? "true" : "false"));
                            break;
                        case "PropertyNamingPolicy" when value is int policy && GetNamingPolicyName(policy) is { } policyName:
                            optionAssignments.Add("PropertyNamingPolicy = " + Global + ".CsvNamingPolicy." + policyName);
                            break;
                        case "MissingColumnHandling" when value is int missing:
                            optionAssignments.Add("MissingColumnHandling = (" + Global + ".CsvMissingColumnHandling)" + Literal(missing));
                            break;
                        case "UnmappedColumnHandling" when value is int unmapped:
                            optionAssignments.Add("UnmappedColumnHandling = (" + Global + ".CsvUnmappedColumnHandling)" + Literal(unmapped));
                            break;
                    }
                }
            }

            optionsCode.Append(optionAssignments.Count == 0 ? "()" : "() { " + string.Join(", ", optionAssignments) + " }");

            var types = new List<(string PropertyName, string TypeFqn, string Method)>();
            var methods = new StringBuilder();
            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            var typeIndex = 0;
            foreach (var attribute in _context.GetAttributes())
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (attribute.AttributeClass?.ToDisplayString() != CsvSerializableAttributeMetadataName)
                    continue;

                if (attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value is not ITypeSymbol type)
                    continue;

                var attributeLocation = attribute.ApplicationSyntaxReference?.GetSyntax(_cancellationToken).GetLocation() ?? location;
                var propertyName = attribute.NamedArguments.FirstOrDefault(a => a.Key == "TypeInfoPropertyName").Value.Value as string ?? type.Name;
                if (!propertyNames.Add(propertyName))
                {
                    _diagnostics.Add(DiagnosticInfo.Create(DuplicateTypeInfoPropertyName, attributeLocation, contextName, propertyName));
                    continue;
                }

                var methodName = "Create_" + typeIndex.ToString(CultureInfo.InvariantCulture) + "_" + propertyName;
                typeIndex++;
                var method = EmitType(type, methodName, attributeLocation);
                if (method is null)
                    continue;

                methods.Append(method);
                types.Add((propertyName, Fqn(type), methodName));
            }

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable disable");
            sb.AppendLine("#pragma warning disable CS0612, CS0618 // Obsolete members");
            sb.AppendLine();
            var hasNamespace = !_context.ContainingNamespace.IsGlobalNamespace;
            if (hasNamespace)
            {
                sb.Append("namespace ").Append(_context.ContainingNamespace.ToDisplayString()).AppendLine();
                sb.AppendLine("{");
            }

            var containingTypes = new List<INamedTypeSymbol>();
            for (var current = _context.ContainingType; current is not null; current = current.ContainingType)
            {
                containingTypes.Insert(0, current);
            }

            foreach (var containingType in containingTypes)
            {
                sb.Append("partial ").Append(GetTypeKeyword(containingType)).Append(' ').Append(containingType.Name).AppendLine();
                sb.AppendLine("{");
            }

            sb.Append("partial class ").Append(contextName).AppendLine();
            sb.AppendLine("{");
            // A static initializer creates one instance even when several threads get it first, so the metadata of the default context is shared
            sb.AppendLine("    /// <summary>Gets the default instance of the context, with the options of its CsvSourceGenerationOptionsAttribute.</summary>");
            sb.Append("    public static ").Append(contextFqn).Append(" Default { get; } = new ").Append(contextFqn).AppendLine("(CreateDefaultOptions());");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Initializes a new instance of the context with the default options.</summary>");
            sb.Append("    public ").Append(contextName).AppendLine("()");
            sb.AppendLine("        : base(null)");
            sb.AppendLine("    {");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>Initializes a new instance of the context with the specified options.</summary>");
            sb.Append("    public ").Append(contextName).Append('(').Append(Global).AppendLine(".CsvSerializerOptions options)");
            sb.AppendLine("        : base(options)");
            sb.AppendLine("    {");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.Append("    private static ").Append(Global).Append(".CsvSerializerOptions CreateDefaultOptions() => ").Append(optionsCode).AppendLine(";");
            foreach (var (propertyName, typeFqn, _) in types)
            {
                sb.AppendLine();
                sb.Append("    /// <summary>Gets the metadata of <see cref=\"").Append(typeFqn.Replace('<', '{').Replace('>', '}')).AppendLine("\"/>.</summary>");
                sb.Append("    public ").Append(Metadata).Append(".CsvTypeInfo<").Append(typeFqn).Append("> ").Append(propertyName).Append(" => GetOrCreateTypeInfo<").Append(typeFqn).AppendLine(">();");
            }

            sb.AppendLine();
            sb.AppendLine("    /// <inheritdoc/>");
            sb.Append("    public override ").Append(Metadata).Append(".CsvTypeInfo GetTypeInfo(global::System.Type type, ").Append(Global).AppendLine(".CsvSerializerOptions options)");
            sb.AppendLine("    {");
            foreach (var (_, typeFqn, methodName) in types)
            {
                sb.Append("        if (type == typeof(").Append(typeFqn).Append(")) return ").Append(methodName).AppendLine("(options);");
            }

            sb.AppendLine("        return null;");
            sb.AppendLine("    }");
            sb.Append(methods);
            sb.Append(_accessors);
            sb.AppendLine("}");
            for (var i = 0; i < containingTypes.Count; i++)
            {
                sb.AppendLine("}");
            }

            if (hasNamespace)
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private static string GetTypeKeyword(INamedTypeSymbol type)
        {
            if (type.IsRecord)
                return type.IsValueType ? "record struct" : "record";

            return type.TypeKind switch
            {
                TypeKind.Struct => "struct",
                TypeKind.Interface => "interface",
                _ => "class",
            };
        }

        private static string? GetNamingPolicyName(int value) => value switch
        {
            1 => "CamelCase",
            2 => "SnakeCaseLower",
            3 => "SnakeCaseUpper",
            4 => "KebabCaseLower",
            5 => "KebabCaseUpper",
            6 => "PascalCase",
            _ => null,
        };

        private string? EmitType(ITypeSymbol type, string methodName, Location? location)
        {
            var typeName = type.ToDisplayString();
            if (type is not INamedTypeSymbol namedType || type.IsAbstract || type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || namedType.IsUnboundGenericType || type.IsRefLikeType || ContainsTypeParameters(namedType))
            {
                _diagnostics.Add(DiagnosticInfo.Create(TypeNotSupported, location, typeName, "it must be a concrete class or struct"));
                return null;
            }

            if (!_compilation.IsSymbolAccessibleWithin(type, _context))
            {
                _diagnostics.Add(DiagnosticInfo.Create(TypeNotSupported, location, typeName, "it is not accessible from the context"));
                return null;
            }

            var isGeneric = namedType.IsGenericType;
            var typeFqn = Fqn(type);
            var isValueType = type.IsValueType;
            var members = GetMembers(namedType);

            // Constructor
            if (!TrySelectConstructor(namedType, out var constructor, out var error))
            {
                _diagnostics.Add(DiagnosticInfo.Create(TypeNotSupported, location, typeName, error));
                return null;
            }

            var parameters = constructor?.Parameters ?? ImmutableArray<IParameterSymbol>.Empty;
            var parameterMembers = new int[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                parameterMembers[i] = members.FindIndex(member => string.Equals(member.Name, parameter.Name, StringComparison.OrdinalIgnoreCase) && SymbolEqualityComparer.Default.Equals(member.Type, parameter.Type));
            }

            var body = new StringBuilder();
            body.AppendLine();
            body.Append("    private static ").Append(Metadata).Append(".CsvTypeInfo<").Append(typeFqn).Append("> ").Append(methodName).Append('(').Append(Global).AppendLine(".CsvSerializerOptions options)");
            body.AppendLine("    {");
            body.Append("        var properties = new ").Append(Metadata).AppendLine(".CsvPropertyInfo[]");
            body.AppendLine("        {");

            var columnNames = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < members.Count; i++)
            {
                var parameterIndex = Array.IndexOf(parameterMembers, i);
                var property = EmitProperty(namedType, typeFqn, members[i], parameterIndex, location);
                if (property is null)
                    return null;

                body.Append(property);
                CheckDuplicateColumnName(typeName, members[i], columnNames, location);
            }

            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameterMembers[i] >= 0)
                    continue;

                var parameter = parameters[i];
                var member = new MemberData(parameter.Name, parameter.Type, Symbol: null, Include: false);
                var property = EmitProperty(namedType, typeFqn, member, i, location);
                if (property is null)
                    return null;

                body.Append(property);
            }

            body.AppendLine("        };");

            body.Append("        return ").Append(Metadata).Append(".CsvMetadataServices.CreateObjectInfo(options, new ").Append(Metadata).Append(".CsvObjectInfoValues<").Append(typeFqn).AppendLine(">");
            body.AppendLine("        {");
            if (constructor is not null)
            {
                var needsAccessor = !_compilation.IsSymbolAccessibleWithin(constructor, _context) || (HasRequiredMembers(namedType) && !SetsRequiredMembers(constructor));
                string creation;
                if (needsAccessor)
                {
                    if (isGeneric)
                    {
                        _diagnostics.Add(DiagnosticInfo.Create(TypeNotSupported, location, typeName, "the constructor of a generic type must be accessible and must set the required members"));
                        return null;
                    }

                    var accessorName = NewAccessorName("Create");
                    _accessors.AppendLine();
                    _accessors.AppendLine("    [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Constructor)]");
                    _accessors.Append("    private static ").Append(_extern).Append(typeFqn).Append(' ').Append(accessorName).Append('(');
                    _accessors.Append(string.Join(", ", parameters.Select((p, index) => Fqn(p.Type) + " p" + index.ToString(CultureInfo.InvariantCulture))));
                    _accessors.AppendLine(");");
                    creation = accessorName + "(";
                }
                else
                {
                    creation = "new " + typeFqn + "(";
                }

                if (parameters.Length == 0)
                {
                    body.Append("            ObjectCreator = () => ").Append(creation).AppendLine("),");
                }
                else
                {
                    body.Append("            ObjectWithParameterizedConstructorCreator = args => ").Append(creation);
                    // The values are read from typed slots: they are not boxed
                    body.Append(string.Join(", ", parameters.Select((p, index) => "args.GetValue<" + Fqn(p.Type) + ">(" + index.ToString(CultureInfo.InvariantCulture) + ")")));
                    body.AppendLine("),");
                    body.Append("            ConstructorParameters = new ").Append(Metadata).AppendLine(".CsvParameterInfoValues[]");
                    body.AppendLine("            {");
                    for (var i = 0; i < parameters.Length; i++)
                    {
                        var parameter = parameters[i];
                        body.Append("                new ").Append(Metadata).Append(".CsvParameterInfoValues { Name = ").Append(Literal(parameter.Name));
                        body.Append(", ParameterType = typeof(").Append(Fqn(parameter.Type)).Append("), Position = ").Append(Literal(i));
                        if (parameter.HasExplicitDefaultValue)
                        {
                            body.Append(", HasDefaultValue = true, DefaultValue = ").Append(FormatDefaultValue(parameter));
                        }

                        body.AppendLine(" },");
                    }

                    body.AppendLine("            },");
                }
            }
            else if (isValueType)
            {
                body.AppendLine("            ObjectCreator = () => default,");
            }

            body.AppendLine("            Properties = properties,");
            body.AppendLine("        });");
            body.AppendLine("    }");
            return body.ToString();
        }

        private static string FormatDefaultValue(IParameterSymbol parameter)
        {
            var type = Fqn(parameter.Type);
            var value = parameter.ExplicitDefaultValue;
            if (value is null)
                return "(object)default(" + type + ")";

            var literal = value switch
            {
                string s => SymbolDisplay.FormatLiteral(s, quote: true),
                char c => SymbolDisplay.FormatLiteral(c, quote: true),
                bool b => b ? "true" : "false",
                float f => f.ToString("R", CultureInfo.InvariantCulture) + "F",
                double d => d.ToString("R", CultureInfo.InvariantCulture) + "D",
                decimal m => m.ToString(CultureInfo.InvariantCulture) + "M",
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "default",
            };

            if (parameter.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            {
                type = Fqn(nullable.TypeArguments[0]);
            }

            return "(object)(" + type + ")(" + literal + ")";
        }

        private static bool ContainsTypeParameters(INamedTypeSymbol type)
        {
            foreach (var argument in type.TypeArguments)
            {
                if (argument is ITypeParameterSymbol || (argument is INamedTypeSymbol named && ContainsTypeParameters(named)))
                    return true;
            }

            return type.ContainingType is not null && ContainsTypeParameters(type.ContainingType);
        }

        private static bool HasRequiredMembers(INamedTypeSymbol type)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                foreach (var member in current.GetMembers().OrderBy(m => m is IFieldSymbol ? 1 : 0))
                {
                    if (member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true })
                        return true;
                }
            }

            return false;
        }

        private static bool SetsRequiredMembers(IMethodSymbol constructor)
            => constructor.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute");

        private static bool TrySelectConstructor(INamedTypeSymbol type, out IMethodSymbol? constructor, out string error)
        {
            error = "";
            var constructors = type.InstanceConstructors.Where(c => !(type.IsValueType && c.IsImplicitlyDeclared)).ToArray();
            var annotated = constructors.Where(c => HasAttribute(c, "CsvConstructorAttribute")).ToArray();
            if (annotated.Length > 1)
            {
                constructor = null;
                error = "it has more than one constructor with CsvConstructorAttribute";
                return false;
            }

            if (annotated.Length == 1)
            {
                constructor = annotated[0];
                return true;
            }

            var publicConstructors = constructors.Where(c => c.DeclaredAccessibility == Accessibility.Public).ToArray();
            constructor = publicConstructors.FirstOrDefault(c => c.Parameters.Length == 0);
            if (constructor is not null)
                return true;

            if (type.IsValueType)
                return true;

            if (publicConstructors.Length == 1)
            {
                constructor = publicConstructors[0];
                return true;
            }

            error = "it has no public parameterless constructor and more than one public constructor: use CsvConstructorAttribute to select one";
            return false;
        }

        private static bool HasAttribute(ISymbol symbol, string attributeName)
            => symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == SerializationNamespace + "." + attributeName);

        private static AttributeData? GetAttribute(ISymbol? symbol, string attributeName)
            => symbol?.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == SerializationNamespace + "." + attributeName);

        private List<MemberData> GetMembers(INamedTypeSymbol type)
        {
            var hierarchy = new List<INamedTypeSymbol>();
            for (var current = type; current is not null && current.SpecialType is not (SpecialType.System_Object or SpecialType.System_ValueType); current = current.BaseType)
            {
                hierarchy.Insert(0, current);
            }

            var result = new List<MemberData>();
            foreach (var current in hierarchy)
            {
                foreach (var member in current.GetMembers().OrderBy(m => m is IFieldSymbol ? 1 : 0))
                {
                    if (member.IsStatic || member.IsImplicitlyDeclared || HasAttribute(member, "CsvIgnoreAttribute"))
                        continue;

                    var include = HasAttribute(member, "CsvIncludeAttribute");
                    ITypeSymbol memberType;
                    switch (member)
                    {
                        case IPropertySymbol property:
                            if (property.IsIndexer || property.RefKind is not RefKind.None)
                                continue;

                            var isPublic = property.GetMethod?.DeclaredAccessibility == Accessibility.Public || property.SetMethod?.DeclaredAccessibility == Accessibility.Public;
                            if (!isPublic && !include)
                                continue;

                            memberType = property.Type;
                            break;

                        case IFieldSymbol field:
                            if (field.IsConst || field.RefKind is not RefKind.None)
                                continue;

                            if (!(include || (field.DeclaredAccessibility == Accessibility.Public && _includeFields)))
                                continue;

                            memberType = field.Type;
                            break;

                        default:
                            continue;
                    }

                    if (memberType.IsRefLikeType || memberType.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer)
                        continue;

                    var data = new MemberData(member.Name, memberType, member, include);
                    var existing = result.FindIndex(m => m.Name == member.Name);
                    if (existing >= 0)
                    {
                        result[existing] = data;
                    }
                    else
                    {
                        result.Add(data);
                    }
                }
            }

            return result;
        }

        private void CheckDuplicateColumnName(string typeName, MemberData member, HashSet<string> columnNames, Location? location)
        {
            var explicitName = GetAttribute(member.Symbol, "CsvPropertyNameAttribute")?.ConstructorArguments.FirstOrDefault().Value as string;
            if (explicitName is not null && !columnNames.Add(explicitName))
            {
                _diagnostics.Add(DiagnosticInfo.Create(DuplicateColumnName, location, typeName, explicitName));
            }
            else if (explicitName is null)
            {
                columnNames.Add(member.Name);
            }
        }

        private string? EmitProperty(INamedTypeSymbol declaringType, string declaringFqn, MemberData member, int parameterIndex, Location? location)
        {
            var valueFqn = Fqn(member.Type);
            var memberDisplayName = declaringType.ToDisplayString() + "." + member.Name;
            if (!_compilation.IsSymbolAccessibleWithin(member.Type, _context))
            {
                _diagnostics.Add(DiagnosticInfo.Create(MemberNotSupported, location, memberDisplayName, "its type is not accessible from the context"));
                return null;
            }

            string? getter = null;
            string? setter = null;
            var isRequired = false;
            var refPrefix = declaringType.IsValueType ? "ref " : "";
            switch (member.Symbol)
            {
                case IPropertySymbol property:
                    isRequired = property.IsRequired;
                    if (property.GetMethod is { } getMethod && (getMethod.DeclaredAccessibility == Accessibility.Public || member.Include))
                    {
                        if (_compilation.IsSymbolAccessibleWithin(getMethod, _context))
                        {
                            getter = "obj => obj." + EscapeIdentifier(property.Name);
                        }
                        else if (!TryCreateMethodAccessor(property.ContainingType, getMethod.Name, valueFqn, [], out var accessorName, location, memberDisplayName))
                        {
                            return null;
                        }
                        else
                        {
                            getter = "obj => " + accessorName + "(" + refPrefix + "obj)";
                        }
                    }

                    if (parameterIndex < 0 && property.SetMethod is { } setMethod && (setMethod.DeclaredAccessibility == Accessibility.Public || member.Include))
                    {
                        if (!setMethod.IsInitOnly && _compilation.IsSymbolAccessibleWithin(setMethod, _context))
                        {
                            setter = "(ref " + declaringFqn + " obj, " + valueFqn + " value) => obj." + EscapeIdentifier(property.Name) + " = value";
                        }
                        else if (!TryCreateMethodAccessor(property.ContainingType, setMethod.Name, "void", [valueFqn], out var accessorName, location, memberDisplayName))
                        {
                            return null;
                        }
                        else
                        {
                            setter = "(ref " + declaringFqn + " obj, " + valueFqn + " value) => " + accessorName + "(" + refPrefix + "obj, value)";
                        }
                    }

                    break;

                case IFieldSymbol field:
                    isRequired = field.IsRequired;
                    var accessible = _compilation.IsSymbolAccessibleWithin(field, _context);
                    string? fieldAccessor = null;
                    if (!accessible && !TryCreateFieldAccessor(field, valueFqn, out fieldAccessor, location, memberDisplayName))
                        return null;

                    var fieldExpression = accessible ? "obj." + EscapeIdentifier(field.Name) : fieldAccessor + "(" + refPrefix + "obj)";
                    getter = "obj => " + fieldExpression;
                    if (parameterIndex < 0 && !field.IsReadOnly)
                    {
                        setter = "(ref " + declaringFqn + " obj, " + valueFqn + " value) => " + fieldExpression + " = value";
                    }

                    break;
            }

            isRequired |= member.Symbol is not null && HasAttribute(member.Symbol, "CsvRequiredAttribute");

            var columnName = GetAttribute(member.Symbol, "CsvPropertyNameAttribute")?.ConstructorArguments.FirstOrDefault().Value as string;
            var order = GetAttribute(member.Symbol, "CsvPropertyOrderAttribute")?.ConstructorArguments.FirstOrDefault().Value as int?;
            var columnIndex = GetAttribute(member.Symbol, "CsvColumnIndexAttribute")?.ConstructorArguments.FirstOrDefault().Value as int?;
            var format = GetAttribute(member.Symbol, "CsvFormatAttribute")?.ConstructorArguments.FirstOrDefault().Value as string;

            string? converter = null;
            if (GetAttribute(member.Symbol, "CsvConverterAttribute") is { } converterAttribute)
            {
                if (converterAttribute.ConstructorArguments.FirstOrDefault().Value is not INamedTypeSymbol converterType || !IsValidConverter(converterType))
                {
                    _diagnostics.Add(DiagnosticInfo.Create(InvalidConverter, location, (converterAttribute.ConstructorArguments.FirstOrDefault().Value as ITypeSymbol)?.ToDisplayString() ?? "null", memberDisplayName));
                    return null;
                }

                converter = "new " + Fqn(converterType) + "()";
            }

            var sb = new StringBuilder();
            sb.Append("            ").Append(Metadata).Append(".CsvMetadataServices.CreatePropertyInfo(options, new ").Append(Metadata).Append(".CsvPropertyInfoValues<").Append(declaringFqn).Append(", ").Append(valueFqn).AppendLine(">");
            sb.AppendLine("            {");
            sb.Append("                MemberName = ").Append(Literal(member.Name)).AppendLine(",");
            if (columnName is not null)
            {
                sb.Append("                ColumnName = ").Append(Literal(columnName)).AppendLine(",");
            }

            if (order is { } orderValue)
            {
                sb.Append("                Order = ").Append(Literal(orderValue)).AppendLine(",");
            }

            if (columnIndex is { } indexValue)
            {
                sb.Append("                ColumnIndex = ").Append(Literal(indexValue)).AppendLine(",");
            }

            if (isRequired)
            {
                sb.AppendLine("                IsRequired = true,");
            }

            if (format is not null)
            {
                sb.Append("                Format = ").Append(Literal(format)).AppendLine(",");
            }

            if (getter is not null)
            {
                sb.Append("                Getter = ").Append(getter).AppendLine(",");
            }

            if (setter is not null)
            {
                sb.Append("                Setter = ").Append(setter).AppendLine(",");
            }

            if (converter is not null)
            {
                sb.Append("                Converter = ").Append(converter).AppendLine(",");
            }

            if (GetDefaultConverter(member.Type) is { } defaultConverter)
            {
                sb.Append("                DefaultConverter = ").Append(defaultConverter).AppendLine(",");
            }

            if (parameterIndex >= 0)
            {
                sb.Append("                ParameterIndex = ").Append(Literal(parameterIndex)).AppendLine(",");
            }

            sb.AppendLine("            }),");
            return sb.ToString();
        }

        private bool IsValidConverter(INamedTypeSymbol converterType)
        {
            if (converterType.IsAbstract || converterType.IsUnboundGenericType || ContainsTypeParameters(converterType))
                return false;

            if (_converterBase is null)
                return false;

            var isConverter = false;
            for (var current = converterType.BaseType; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, _converterOfT) || SymbolEqualityComparer.Default.Equals(current, _converterFactory))
                {
                    isConverter = true;
                    break;
                }
            }

            return isConverter && converterType.InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);
        }

        private string? GetDefaultConverter(ITypeSymbol type)
        {
            if (IsBuiltIn(type))
                return null;

            var typeFqn = Fqn(type);
            if (type.TypeKind is TypeKind.Enum)
                return Metadata + ".CsvMetadataServices.GetEnumConverter<" + typeFqn + ">()";

            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            {
                var underlyingType = nullable.TypeArguments[0];
                var underlyingFqn = Fqn(underlyingType);
                var inner = GetDefaultConverter(underlyingType) ?? "null";
                return Metadata + ".CsvMetadataServices.GetNullableConverter<" + underlyingFqn + ">(" + Metadata + ".CsvMetadataServices.GetConverter<" + underlyingFqn + ">(options, " + inner + "))";
            }

            if (_spanParsable is not null && _spanFormattable is not null
                && type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, _spanParsable) && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], type))
                && type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, _spanFormattable)))
            {
                return Metadata + ".CsvMetadataServices.GetSpanParsableConverter<" + typeFqn + ">()";
            }

            return null;
        }

        private bool TryCreateMethodAccessor(INamedTypeSymbol declaringType, string methodName, string returnType, string[] parameterTypes, out string accessorName, Location? location, string memberDisplayName)
        {
            accessorName = "";
            if (declaringType.IsGenericType)
            {
                _diagnostics.Add(DiagnosticInfo.Create(MemberNotSupported, location, memberDisplayName, "an accessor that is not accessible from the context, or an init accessor, is not supported on a generic type"));
                return false;
            }

            accessorName = NewAccessorName(methodName);
            var declaringFqn = Fqn(declaringType);
            _accessors.AppendLine();
            _accessors.Append("    [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = ").Append(Literal(methodName)).AppendLine(")]");
            _accessors.Append("    private static ").Append(_extern).Append(returnType).Append(' ').Append(accessorName).Append('(').Append(declaringType.IsValueType ? "ref " : "").Append(declaringFqn).Append(" obj");
            for (var i = 0; i < parameterTypes.Length; i++)
            {
                _accessors.Append(", ").Append(parameterTypes[i]).Append(" value").Append(i.ToString(CultureInfo.InvariantCulture));
            }

            _accessors.AppendLine(");");
            return true;
        }

        private bool TryCreateFieldAccessor(IFieldSymbol field, string valueFqn, out string accessorName, Location? location, string memberDisplayName)
        {
            accessorName = "";
            var declaringType = field.ContainingType;
            if (declaringType.IsGenericType)
            {
                _diagnostics.Add(DiagnosticInfo.Create(MemberNotSupported, location, memberDisplayName, "a field that is not accessible from the context is not supported on a generic type"));
                return false;
            }

            accessorName = NewAccessorName("Field_" + field.Name);
            _accessors.AppendLine();
            _accessors.Append("    [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = ").Append(Literal(field.Name)).AppendLine(")]");
            _accessors.Append("    private static ").Append(_extern).Append("ref ").Append(valueFqn).Append(' ').Append(accessorName).Append('(').Append(declaringType.IsValueType ? "ref " : "").Append(Fqn(declaringType)).AppendLine(" obj);");
            return true;
        }

        private string NewAccessorName(string name)
        {
            var sanitized = new StringBuilder();
            foreach (var c in name)
            {
                sanitized.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            return "__" + sanitized + "_" + (_accessorIndex++).ToString(CultureInfo.InvariantCulture);
        }

        private static string EscapeIdentifier(string name) => SyntaxFacts.GetKeywordKind(name) is SyntaxKind.None ? name : "@" + name;
    }

    private sealed record MemberData(string Name, ITypeSymbol Type, ISymbol? Symbol, bool Include);
}
