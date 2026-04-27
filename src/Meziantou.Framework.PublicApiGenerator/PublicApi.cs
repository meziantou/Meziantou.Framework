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

    public static IReadOnlyList<PublicApiFile> Generate(IReadOnlyList<AssemblySource> assemblySources, PublicApiOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(assemblySources);

        options ??= new PublicApiOptions();
        if (assemblySources.Count == 0)
        {
            throw new ArgumentException("At least one assembly source must be provided.", nameof(assemblySources));
        }

        if (assemblySources.Count == 1)
        {
            return Generate(ReadModel(assemblySources[0]), options);
        }

        var modelsBySymbol = BuildModelsBySymbol(assemblySources);
        var mergedModel = PublicApiMultiTargetModelMerger.Merge(modelsBySymbol);
        return Generate(mergedModel, options);
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

    public static void GenerateToDirectory(IReadOnlyList<AssemblySource> assemblySources, string outputDirectory, PublicApiOptions? options = null)
    {
        var files = Generate(assemblySources, options);
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

    private static PublicApiModel ReadModel(AssemblySource assemblySource)
    {
        ArgumentNullException.ThrowIfNull(assemblySource);

        if (assemblySource.Assembly is not null)
        {
            return ReadModel(assemblySource.Assembly);
        }

        if (!string.IsNullOrEmpty(assemblySource.Path))
        {
            return ReadModel(assemblySource.Path);
        }

        throw new InvalidOperationException("AssemblySource must provide either an Assembly instance or an assembly path.");
    }

    private static IReadOnlyList<PublicApiFile> Generate(PublicApiModel model, PublicApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        return PublicApiEmitter.Generate(model, options);
    }

    private static Dictionary<string, PublicApiModel> BuildModelsBySymbol(IReadOnlyList<AssemblySource> assemblySources)
    {
        var modelsBySymbol = new Dictionary<string, PublicApiModel>(StringComparer.Ordinal);
        foreach (var assemblySource in assemblySources)
        {
            if (assemblySource is null)
                throw new ArgumentException("Assembly sources cannot contain null values.", nameof(assemblySources));

            var targetFramework = ResolveTargetFramework(assemblySource);
            var symbol = PublicApiTargetFramework.ToPreprocessorSymbol(targetFramework);
            if (!modelsBySymbol.TryAdd(symbol, ReadModel(assemblySource)))
            {
                throw new ArgumentException($"Multiple assembly sources resolve to the same preprocessor symbol '{symbol}'.", nameof(assemblySources));
            }
        }

        return modelsBySymbol;
    }

    private static string ResolveTargetFramework(AssemblySource assemblySource)
    {
        if (!string.IsNullOrWhiteSpace(assemblySource.TargetFrameworkMoniker))
        {
            return PublicApiTargetFramework.ToTargetFramework(assemblySource.TargetFrameworkMoniker);
        }

        if (assemblySource.Assembly is not null)
        {
            return PublicApiTargetFramework.GetTargetFrameworkFromAssembly(assemblySource.Assembly);
        }

        if (!string.IsNullOrEmpty(assemblySource.Path))
        {
            return PublicApiTargetFramework.GetTargetFrameworkFromAssembly(assemblySource.Path);
        }

        throw new InvalidOperationException("AssemblySource must provide either an Assembly instance or an assembly path.");
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
