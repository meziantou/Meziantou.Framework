using System.Reflection;

namespace Meziantou.Framework.PublicApiGenerator;

public static class PublicApiStubGenerator
{
    public static PublicApiModel ReadModel(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(assemblyPath);
        return PublicApiModelReader.ReadFromMetadata(assemblyPath);
    }

    public static PublicApiModel ReadModel(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return PublicApiModelReader.ReadFromReflection(assembly);
    }

    public static PublicApiModel ReadModel(Module module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var types = EnumerateTypes(module)
            .Where(static type => type.DeclaringType is null)
            .Where(type => ReferenceEquals(type.Module, module));
        return PublicApiModelBuilder.Build(types);
    }

    public static PublicApiModel ReadModel(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var rootType = GetRootType(type);
        return PublicApiModelBuilder.Build([rootType]);
    }

    public static IReadOnlyList<PublicApiGeneratedFile> GenerateFiles(PublicApiModel model, PublicApiFileLayout layout = PublicApiFileLayout.SingleFile)
    {
        ArgumentNullException.ThrowIfNull(model);
        return PublicApiEmitter.Generate(model, layout);
    }

    public static IReadOnlyList<PublicApiGeneratedFile> GenerateFiles(string assemblyPath, PublicApiGeneratorOptions? options = null)
    {
        options ??= new PublicApiGeneratorOptions();
        var model = ReadModel(assemblyPath);
        return GenerateFiles(model, options.FileLayout);
    }

    public static IReadOnlyList<PublicApiGeneratedFile> GenerateFiles(Assembly assembly, PublicApiGeneratorOptions? options = null)
    {
        options ??= new PublicApiGeneratorOptions();
        var model = ReadModel(assembly);
        return GenerateFiles(model, options.FileLayout);
    }

    public static void GenerateToDirectory(string assemblyPath, string outputDirectory, PublicApiGeneratorOptions? options = null)
    {
        var files = GenerateFiles(assemblyPath, options);
        WriteToDirectory(files, outputDirectory);
    }

    public static void GenerateToDirectory(Assembly assembly, string outputDirectory, PublicApiGeneratorOptions? options = null)
    {
        var files = GenerateFiles(assembly, options);
        WriteToDirectory(files, outputDirectory);
    }

    private static void WriteToDirectory(IReadOnlyList<PublicApiGeneratedFile> files, string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);

        Directory.CreateDirectory(outputDirectory);
        foreach (var file in files)
        {
            var path = Path.Combine(outputDirectory, file.RelativePath);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, file.Content);
        }
    }

    private static IEnumerable<Type> EnumerateTypes(Module module)
    {
        try
        {
            return module.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(static type => type is not null).Cast<Type>();
        }
    }

    private static Type GetRootType(Type type)
    {
        var current = type;
        while (current.DeclaringType is not null)
        {
            current = current.DeclaringType;
        }

        return current;
    }
}
