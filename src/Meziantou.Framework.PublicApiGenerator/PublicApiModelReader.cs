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
        var visibleTypes = assembly.Types.Select(static type => type.FullName).ToHashSet(StringComparer.Ordinal);
        return attribute =>
        {
            if (CSharpSyntaxFacts.IsDisplayedAttribute(attribute, PublicApiDeclarationStyle.Compilable))
                return true;

            var attributeType = attribute.AttributeType;
            return CSharpSyntaxFacts.IsAdditionalAttribute(
                attributeType.FullName,
                isNestedOrGeneric: attributeType.ContainingType is not null || !attributeType.TypeArguments.IsEmpty,
                isDeclaredInAssembly: attributeType.AssemblyName is null || string.Equals(attributeType.AssemblyName, assembly.Name, StringComparison.Ordinal),
                isVisibleType: visibleTypes.Contains(attributeType.FullName));
        };
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
