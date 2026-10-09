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

        return PublicApiMetadataReader.Read(peReader, options);
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

    public static PublicApiModel ReadFromMetadata(string assemblyPath)
    {
        var assembly = ReadAssembly(assemblyPath, options: null);
        return CreateModel(assembly);
    }

    public static PublicApiModel CreateModel(PublicApiAssembly assembly)
    {
        var formatter = new CSharpCompilableFormatter(new PublicApiFormattingOptions
        {
            Style = PublicApiDeclarationStyle.Compilable,
            NewLine = Environment.NewLine,
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
            formatter.FormatAssemblyAttributes(assembly),
            [.. types]);
    }

    public static PublicApiModel ReadFromReflection(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var assemblyName = assembly.GetName().Name ?? string.Empty;
        var types = assembly.GetExportedTypes().Where(type => type.DeclaringType is null);
        return PublicApiModelBuilder.Build(assemblyName, assembly.CustomAttributes, types);
    }
}
