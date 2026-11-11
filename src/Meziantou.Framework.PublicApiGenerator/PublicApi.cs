using System.Collections.Immutable;
using System.Reflection;

namespace Meziantou.Framework.PublicApiGenerator;

public static class PublicApi
{
    public static IReadOnlyList<PublicApiFile> Generate(string assemblyPath, PublicApiOptions? options = null)
    {
        options ??= new PublicApiOptions();
        var model = ReadModel(assemblyPath, options);
        var targetFrameworks = NormalizeAndSortTargetFrameworks([PublicApiTargetFramework.GetTargetFrameworkFromAssembly(assemblyPath)]);
        return Generate(model, options, targetFrameworks);
    }

    public static IReadOnlyList<PublicApiFile> Generate(Assembly assembly, PublicApiOptions? options = null)
    {
        options ??= new PublicApiOptions();
        var model = ReadModel(assembly, options);
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
            var model = ReadModel(assemblySources[0], options);
            var singleTargetFrameworks = NormalizeAndSortTargetFrameworks([ResolveTargetFramework(assemblySources[0])]);
            return Generate(model, options, singleTargetFrameworks);
        }

        var modelsBySymbol = BuildModelsBySymbol(assemblySources, options, out var firstAssemblyName, out var targetFrameworks, out var assemblyReferencesByTargetFramework);
        var mergedModel = PublicApiMultiTargetModelMerger.Merge(modelsBySymbol, firstAssemblyName);
        return PublicApiEmitter.Generate(mergedModel, options, targetFrameworks, MergeAssemblyReferences(assemblyReferencesByTargetFramework, targetFrameworks));
    }

    /// <summary>Reads the public API of an assembly file, without loading it.</summary>
    public static PublicApiAssembly ReadAssembly(string assemblyPath, PublicApiReadOptions? options = null)
    {
        return PublicApiModelReader.ReadAssembly(assemblyPath, options);
    }

    /// <summary>Reads the public API of an assembly from a stream that contains a PE image, without loading it. The stream is not closed.</summary>
    public static PublicApiAssembly ReadAssembly(Stream stream, PublicApiReadOptions? options = null)
    {
        return PublicApiModelReader.ReadAssembly(stream, options);
    }

    /// <summary>
    /// Aggregates the builds of an assembly for several target frameworks, to find the symbols that are specific to some of them and the declarations that differ.
    /// </summary>
    /// <param name="assemblies">The builds of the same assembly. They must have the same name and distinct target frameworks.</param>
    public static PublicApiAggregatedAssembly Aggregate(IReadOnlyList<PublicApiAssembly> assemblies)
    {
        return PublicApiAggregator.Aggregate(assemblies);
    }

    public static void GenerateToDirectory(string assemblyPath, string outputDirectory, PublicApiOptions? options = null)
    {
        var files = Generate(assemblyPath, options);
        WriteToDirectory(files, outputDirectory, options?.DeleteUnexpectedFiles ?? false);
    }

    public static void GenerateToDirectory(Assembly assembly, string outputDirectory, PublicApiOptions? options = null)
    {
        var files = Generate(assembly, options);
        WriteToDirectory(files, outputDirectory, options?.DeleteUnexpectedFiles ?? false);
    }

    public static void GenerateToDirectory(IReadOnlyList<AssemblySource> assemblySources, string outputDirectory, PublicApiOptions? options = null)
    {
        var files = Generate(assemblySources, options);
        WriteToDirectory(files, outputDirectory, options?.DeleteUnexpectedFiles ?? false);
    }

    private static PublicApiModel ReadModel(string assemblyPath, PublicApiOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(assemblyPath);
        return PublicApiModelReader.ReadFromMetadata(assemblyPath, options.IncludeAllAttributes);
    }

    private static PublicApiModel ReadModel(Assembly assembly, PublicApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return PublicApiModelReader.ReadFromReflection(assembly, options.IncludeAllAttributes);
    }

    private static PublicApiModel ReadModel(AssemblySource assemblySource, PublicApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(assemblySource);

        if (assemblySource.Assembly is not null)
        {
            return ReadModel(assemblySource.Assembly, options);
        }

        if (!string.IsNullOrEmpty(assemblySource.Path))
        {
            return ReadModel(assemblySource.Path, options);
        }

        throw new InvalidOperationException("AssemblySource must provide either an Assembly instance or an assembly path.");
    }

    private static IReadOnlyList<PublicApiFile> Generate(PublicApiModel model, PublicApiOptions options, IReadOnlyList<string> targetFrameworks)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(targetFrameworks);
        return PublicApiEmitter.Generate(model, options, targetFrameworks, SelectAssemblyReferences(model, options));
    }

    /// <summary>Gets the referenced assemblies to write: the ones of the <c>.deps.json</c> file, or all of them without it, plus the included ones, minus the excluded ones.</summary>
    private static ImmutableArray<string> SelectAssemblyReferences(PublicApiModel model, PublicApiOptions options)
    {
        if (!options.IncludeAssemblyReferences)
            return [];

        return [.. model.AssemblyReferences.Where(name =>
            (model.ExternalAssemblyNames is null || model.ExternalAssemblyNames.Contains(name) || MatchesAny(name, options.IncludedAssemblyReferences))
            && !MatchesAny(name, options.ExcludedAssemblyReferences))];
    }

    private static bool MatchesAny(string name, IList<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (!string.IsNullOrEmpty(pattern) && System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true))
                return true;
        }

        return false;
    }

    /// <summary>Lists the assemblies referenced by any target framework. A name that only some target frameworks reference is followed by these target frameworks.</summary>
    private static List<string> MergeAssemblyReferences(Dictionary<string, ImmutableArray<string>> assemblyReferencesByTargetFramework, IReadOnlyList<string> targetFrameworks)
    {
        var targetFrameworksByAssemblyReference = new SortedDictionary<string, List<string>>(AssemblyReferenceNameComparer.Instance);
        foreach (var targetFramework in targetFrameworks)
        {
            foreach (var assemblyReference in assemblyReferencesByTargetFramework[targetFramework])
            {
                if (!targetFrameworksByAssemblyReference.TryGetValue(assemblyReference, out var referencingTargetFrameworks))
                {
                    referencingTargetFrameworks = [];
                    targetFrameworksByAssemblyReference.Add(assemblyReference, referencingTargetFrameworks);
                }

                referencingTargetFrameworks.Add(targetFramework);
            }
        }

        var result = new List<string>(targetFrameworksByAssemblyReference.Count);
        foreach (var (assemblyReference, referencingTargetFrameworks) in targetFrameworksByAssemblyReference)
        {
            result.Add(referencingTargetFrameworks.Count == targetFrameworks.Count ? assemblyReference : assemblyReference + " (" + string.Join(", ", referencingTargetFrameworks) + ")");
        }

        return result;
    }

    private static Dictionary<string, PublicApiModel> BuildModelsBySymbol(IReadOnlyList<AssemblySource> assemblySources, PublicApiOptions options, out string firstAssemblyName, out IReadOnlyList<string> targetFrameworks, out Dictionary<string, ImmutableArray<string>> assemblyReferencesByTargetFramework)
    {
        firstAssemblyName = string.Empty;
        targetFrameworks = [];
        assemblyReferencesByTargetFramework = new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal);
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
            var model = ReadModel(assemblySource, options);
            if (isFirstAssemblySource)
            {
                firstAssemblyName = model.AssemblyName;
                isFirstAssemblySource = false;
            }

            if (!modelsBySymbol.TryAdd(symbol, model))
            {
                throw new ArgumentException($"Multiple assembly sources resolve to the same preprocessor symbol '{symbol}'.", nameof(assemblySources));
            }

            assemblyReferencesByTargetFramework[PublicApiTargetFramework.ToTargetFramework(targetFramework)] = SelectAssemblyReferences(model, options);
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

    private static void WriteToDirectory(IReadOnlyList<PublicApiFile> files, string outputDirectory, bool deleteUnexpectedFiles)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);

        Directory.CreateDirectory(outputDirectory);

        // Delete before writing: on a case-insensitive file system, a file whose name only changes by case is then written with its new name
        if (deleteUnexpectedFiles)
        {
            DeleteUnexpectedFiles(files, outputDirectory);
        }

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

    private static void DeleteUnexpectedFiles(IReadOnlyList<PublicApiFile> files, string outputDirectory)
    {
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        var expectedPaths = new HashSet<string>(files.Select(file => Path.GetFullPath(Path.Combine(fullOutputDirectory, file.RelativePath))), StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(fullOutputDirectory, "*", SearchOption.AllDirectories))
        {
            if (!expectedPaths.Contains(path))
            {
                File.Delete(path);
            }
        }

        // Deepest directories first, so that a directory that only contains empty directories is deleted too
        foreach (var directory in Directory.EnumerateDirectories(fullOutputDirectory, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }
}
