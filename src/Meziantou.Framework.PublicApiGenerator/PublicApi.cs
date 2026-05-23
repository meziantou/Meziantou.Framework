using System.Reflection;

namespace Meziantou.Framework.PublicApiGenerator;

public static class PublicApi
{
    public static IReadOnlyList<PublicApiFile> Generate(string assemblyPath, PublicApiOptions? options = null)
    {
        options ??= new PublicApiOptions();
        var model = ReadModel(assemblyPath);
        var targetFrameworks = NormalizeAndSortTargetFrameworks([PublicApiTargetFramework.GetTargetFrameworkFromAssembly(assemblyPath)]);
        return Generate(model, options, targetFrameworks);
    }

    public static IReadOnlyList<PublicApiFile> Generate(Assembly assembly, PublicApiOptions? options = null)
    {
        options ??= new PublicApiOptions();
        var model = ReadModel(assembly);
        var targetFrameworks = NormalizeAndSortTargetFrameworks([PublicApiTargetFramework.GetTargetFrameworkFromAssembly(assembly)]);
        return Generate(model, options, targetFrameworks);
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
            var model = ReadModel(assemblySources[0]);
            var singleTargetFrameworks = NormalizeAndSortTargetFrameworks([ResolveTargetFramework(assemblySources[0])]);
            return Generate(model, options, singleTargetFrameworks);
        }

        var modelsBySymbol = BuildModelsBySymbol(assemblySources, out var firstAssemblyName, out var targetFrameworks);
        var mergedModel = PublicApiMultiTargetModelMerger.Merge(modelsBySymbol, firstAssemblyName);
        return Generate(mergedModel, options, targetFrameworks);
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

    private static IReadOnlyList<PublicApiFile> Generate(PublicApiModel model, PublicApiOptions options, IReadOnlyList<string> targetFrameworks)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(targetFrameworks);
        return PublicApiEmitter.Generate(model, options, targetFrameworks);
    }

    private static Dictionary<string, PublicApiModel> BuildModelsBySymbol(IReadOnlyList<AssemblySource> assemblySources, out string firstAssemblyName, out IReadOnlyList<string> targetFrameworks)
    {
        firstAssemblyName = string.Empty;
        targetFrameworks = [];
        var modelsBySymbol = new Dictionary<string, PublicApiModel>(StringComparer.Ordinal);
        var resolvedTargetFrameworks = new List<string>(assemblySources.Count);
        var isFirstAssemblySource = true;
        foreach (var assemblySource in assemblySources)
        {
            if (assemblySource is null)
                throw new ArgumentException("Assembly sources cannot contain null values.", nameof(assemblySources));

            var targetFramework = ResolveTargetFramework(assemblySource);
            resolvedTargetFrameworks.Add(targetFramework);
            var symbol = PublicApiTargetFramework.ToPreprocessorSymbol(targetFramework);
            var model = ReadModel(assemblySource);
            if (isFirstAssemblySource)
            {
                firstAssemblyName = model.AssemblyName;
                isFirstAssemblySource = false;
            }

            if (!modelsBySymbol.TryAdd(symbol, model))
            {
                throw new ArgumentException($"Multiple assembly sources resolve to the same preprocessor symbol '{symbol}'.", nameof(assemblySources));
            }
        }

        targetFrameworks = NormalizeAndSortTargetFrameworks(resolvedTargetFrameworks);
        return modelsBySymbol;
    }

    private static IReadOnlyList<string> NormalizeAndSortTargetFrameworks(IEnumerable<string> targetFrameworks)
    {
        ArgumentNullException.ThrowIfNull(targetFrameworks);
        return [.. targetFrameworks
            .Select(PublicApiTargetFramework.ToTargetFramework)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(targetFramework => targetFramework, StringComparer.Ordinal)];
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
