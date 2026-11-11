using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.PortableExecutable;

namespace Meziantou.Framework.PublicApiGenerator;

internal static class PublicApiModelReader
{
    public static PublicApiAssembly ReadAssembly(string assemblyPath, PublicApiReadOptions? options)
    {
        ArgumentException.ThrowIfNullOrEmpty(assemblyPath);

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
        if (!peReader.HasMetadata)
            throw new InvalidOperationException($"The file '{assemblyPath}' does not contain .NET metadata.");

        return PublicApiMetadataReader.Read(peReader, options, Path.GetDirectoryName(Path.GetFullPath(assemblyPath)));
    }

    public static PublicApiAssembly ReadAssembly(Stream stream, PublicApiReadOptions? options)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // The image is copied, so the model does not depend on the stream once it is read
        using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen | PEStreamOptions.PrefetchEntireImage);
        if (!peReader.HasMetadata)
            throw new InvalidOperationException("The stream does not contain .NET metadata.");

        return PublicApiMetadataReader.Read(peReader, options);
    }

    public static PublicApiModel ReadFromMetadata(string assemblyPath, bool includeAllAttributes)
    {
        var assembly = ReadAssembly(assemblyPath, options: null);
        return CreateModel(assembly, includeAllAttributes) with { ExternalAssemblyNames = DependencyFileReader.ReadExternalAssemblyNames(assemblyPath) };
    }

    public static PublicApiModel CreateModel(PublicApiAssembly assembly, bool includeAllAttributes)
    {
        var formatter = new CSharpCompilableFormatter(new PublicApiFormattingOptions
        {
            Style = PublicApiDeclarationStyle.Compilable,
            NewLine = Environment.NewLine,
            AttributeFilter = includeAllAttributes ? CreateAllAttributesFilter(assembly) : null,
            WriteParameterlessConstructorsWithAttributes = includeAllAttributes,
        });

        var types = assembly.Types.Select(type =>
        {
            var writer = new DeclarationWriter(Environment.NewLine);
            formatter.WriteType(writer, type);
            var qualifiedName = type.Namespace.Length == 0 ? type.MetadataName : type.Namespace + "." + type.MetadataName;
            return new PublicApiTypeModel(type.Namespace, type.Name, qualifiedName, writer.GetText());
        });

        return new PublicApiModel(
            assembly.IsModuleOnly ? string.Empty : assembly.Name,
            formatter.FormatAssemblyAttributes(assembly, includeAllAttributes),
            [.. types],
            assembly.AssemblyReferences);
    }

    private static Func<PublicApiAttribute, bool> CreateAllAttributesFilter(PublicApiAssembly assembly)
    {
        // Nested types are included, as the base type of an attribute can be nested
        var visibleTypes = GetTypesAndNestedTypes(assembly.Types).ToDictionary(static type => type.FullName, StringComparer.Ordinal);
        return attribute =>
        {
            if (CSharpSyntaxFacts.IsDisplayedAttribute(attribute, PublicApiDeclarationStyle.Compilable))
                return true;

            var attributeType = attribute.AttributeType;
            return CSharpSyntaxFacts.IsAdditionalAttribute(
                attributeType.FullName,
                isNestedOrGeneric: attributeType.ContainingType is not null || !attributeType.TypeArguments.IsEmpty,
                isDeclaredInAssembly: attributeType.AssemblyName is null || string.Equals(attributeType.AssemblyName, assembly.Name, StringComparison.Ordinal),
                isUsableOutsideAssembly: () => visibleTypes.TryGetValue(attributeType.FullName, out var type) && IsUsableOutsideAssembly(attribute, type, visibleTypes));
        };
    }

    private static IEnumerable<PublicApiType> GetTypesAndNestedTypes(IEnumerable<PublicApiType> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nestedType in GetTypesAndNestedTypes(type.NestedTypes))
            {
                yield return nestedType;
            }
        }
    }

    // An argument of an object parameter has the type of its boxed value (e.g. int for [Marker(42)])
    private static bool IsMatchingParameterType(PublicApiTypeReference parameterType, PublicApiTypeReference argumentType)
    {
        return parameterType is PublicApiNamedTypeReference namedType && namedType.IsSystemType("Object") ||
               PublicApiTypeReferenceComparer.IgnoreNullability.Equals(parameterType, argumentType);
    }

    // The model only contains the members visible outside the assembly. Members of base types declared in other assemblies are considered visible.
    private static bool IsUsableOutsideAssembly(PublicApiAttribute attribute, PublicApiType type, Dictionary<string, PublicApiType> visibleTypes)
    {
        if (!attribute.AreArgumentsDecoded)
            return false;

        var hasConstructor = type.Members.Any(member =>
            member is PublicApiMethod { MethodKind: PublicApiMethodKind.Constructor, IsStatic: false, Accessibility: PublicApiAccessibility.Public } constructor &&
            constructor.Parameters.Length == attribute.ConstructorArguments.Length &&
            constructor.Parameters.Zip(attribute.ConstructorArguments).All(pair => IsMatchingParameterType(pair.First.Type, pair.Second.Type)));
        if (!hasConstructor)
            return false;

        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (!HasSettableMember(type, namedArgument.Name, visibleTypes))
                return false;
        }

        return true;

        static bool HasSettableMember(PublicApiType type, string name, Dictionary<string, PublicApiType> visibleTypes)
        {
            for (PublicApiType? current = type; current is not null;)
            {
                foreach (var member in current.Members)
                {
                    if (!string.Equals(member.Name, name, StringComparison.Ordinal))
                        continue;

                    if (member is PublicApiProperty { SetMethod.Accessibility: PublicApiAccessibility.Public } or PublicApiField { Accessibility: PublicApiAccessibility.Public, IsReadOnly: false, IsConst: false })
                        return true;
                }

                if (current.BaseType is not PublicApiNamedTypeReference baseType)
                    return false;

                if (baseType.AssemblyName is not null && !string.Equals(baseType.AssemblyName, type.Assembly.Name, StringComparison.Ordinal))
                    return true;

                current = visibleTypes.GetValueOrDefault(baseType.FullName);
            }

            return false;
        }
    }

    public static PublicApiModel ReadFromReflection(Assembly assembly, bool includeAllAttributes)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var assemblyName = assembly.GetName().Name ?? string.Empty;
        var types = assembly.GetExportedTypes().Where(type => type.DeclaringType is null);
        var assemblyReferences = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .Order(AssemblyReferenceNameComparer.Instance)
            .ToImmutableArray();
        var model = PublicApiModelBuilder.Build(assemblyName, assembly.CustomAttributes, types, assemblyReferences, includeAllAttributes);
        if (string.IsNullOrEmpty(assembly.Location))
            return model;

        return model with { ExternalAssemblyNames = DependencyFileReader.ReadExternalAssemblyNames(assembly.Location) };
    }
}
