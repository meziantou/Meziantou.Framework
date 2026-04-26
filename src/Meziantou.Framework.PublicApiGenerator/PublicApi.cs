using System.Reflection;

namespace Meziantou.Framework.PublicApiGenerator;

public static class PublicApi
{
    public static IReadOnlyList<PublicApiFile> Generate(string assemblyPath, PublicApiOptions? options = null)
    {
        options ??= new PublicApiOptions();
        var model = ReadModel(assemblyPath);
        return Generate(model, options);
    }

    public static IReadOnlyList<PublicApiFile> Generate(Assembly assembly, PublicApiOptions? options = null)
    {
        options ??= new PublicApiOptions();
        var model = ReadModel(assembly);
        return Generate(model, options);
    }

    public static void GenerateToDirectory(string assemblyPath, string outputDirectory, PublicApiOptions? options = null)
    {
        var files = Generate(assemblyPath, options);
        WriteToDirectory(files, outputDirectory);
    }

    public static void GenerateToDirectory(Assembly assembly, string outputDirectory, PublicApiOptions? options = null)
    {
        var files = Generate(assembly, options);
        WriteToDirectory(files, outputDirectory);
    }

    private static PublicApiModel ReadModel(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(assemblyPath);
        return PublicApiModelReader.ReadFromMetadata(assemblyPath);
    }

    private static PublicApiModel ReadModel(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return PublicApiModelReader.ReadFromReflection(assembly);
    }

    private static IReadOnlyList<PublicApiFile> Generate(PublicApiModel model, PublicApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        return PublicApiEmitter.Generate(model, options);
    }

    private static void WriteToDirectory(IReadOnlyList<PublicApiFile> files, string outputDirectory)
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
}
