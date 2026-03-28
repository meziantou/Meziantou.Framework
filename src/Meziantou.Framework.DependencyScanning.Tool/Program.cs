using System.Collections.Concurrent;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Meziantou.Framework;
using Meziantou.Framework.DependencyScanning;
using Meziantou.Framework.Globbing;

[assembly: InternalsVisibleTo("Meziantou.Framework.DependencyScanning.Tool.Tests")]

namespace Meziantou.Framework.DependencyScanning.Tool;

internal static class Program
{
    private static readonly char[] DependencyTypeDelimiters = [',', ';', ' '];
    private static readonly JsonSerializerOptions ListJsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private enum OutputFormat
    {
        Text,
        Json,
    }

    public static Task<int> Main(string[] args)
    {
        return MainImpl(args, configure: null);
    }

    internal static Task<int> MainImpl(string[] args, Action<InvocationConfiguration>? configure)
    {
        var rootCommand = new RootCommand();
        AddUpdateCommand(rootCommand);
        AddListCommand(rootCommand);
        var invocationConfiguration = new InvocationConfiguration();
        configure?.Invoke(invocationConfiguration);
        return rootCommand.Parse(args).InvokeAsync(invocationConfiguration);
    }

    private static void AddUpdateCommand(RootCommand rootCommand)
    {
        var rootDirectoryOption = CreateRootDirectoryOption();
        var filesOption = CreateFilesOption();
        var dependencyTypesOption = CreateDependencyTypesOption();
        var updateLockFilesOption = new Option<bool>("--update-lock-files") { Description = "Update lock files when dependencies are updated" };

        var updateCommand = new Command("update")
        {
            Description = "Update dependencies",
        };
        updateCommand.Options.Add(rootDirectoryOption);
        updateCommand.Options.Add(filesOption);
        updateCommand.Options.Add(dependencyTypesOption);
        updateCommand.Options.Add(updateLockFilesOption);

        updateCommand.SetAction((parseResult, cancellationToken) =>
        {
            return UpdateAsync(
                parseResult.GetValue(rootDirectoryOption),
                parseResult.GetValue(filesOption),
                parseResult.GetValue(dependencyTypesOption),
                parseResult.GetValue(updateLockFilesOption),
                parseResult.InvocationConfiguration.Output,
                parseResult.InvocationConfiguration.Error,
                cancellationToken);
        });

        rootCommand.Subcommands.Add(updateCommand);
    }

    private static void AddListCommand(RootCommand rootCommand)
    {
        var rootDirectoryOption = CreateRootDirectoryOption();
        var filesOption = CreateFilesOption();
        var dependencyTypesOption = CreateDependencyTypesOption();
        var formatOption = new Option<OutputFormat>("--format")
        {
            Description = $"Output format. Available values: {nameof(OutputFormat.Text)}, {nameof(OutputFormat.Json)}",
        };

        var listCommand = new Command("list")
        {
            Description = "List dependencies",
        };
        listCommand.Options.Add(rootDirectoryOption);
        listCommand.Options.Add(filesOption);
        listCommand.Options.Add(dependencyTypesOption);
        listCommand.Options.Add(formatOption);

        listCommand.SetAction((parseResult, cancellationToken) =>
        {
            return ListAsync(
                parseResult.GetValue(rootDirectoryOption),
                parseResult.GetValue(filesOption),
                parseResult.GetValue(dependencyTypesOption),
                parseResult.GetValue(formatOption),
                parseResult.InvocationConfiguration.Output,
                parseResult.InvocationConfiguration.Error,
                cancellationToken);
        });

        rootCommand.Subcommands.Add(listCommand);
    }

    private static Option<string?> CreateRootDirectoryOption() => new("--directory") { Description = "Root directory" };

    private static Option<string[]?> CreateFilesOption() => new("--files") { Description = "Glob patterns to find files to scan" };

    private static Option<DependencyType[]?> CreateDependencyTypesOption() => new("--dependency-type")
    {
        Description = $"Dependency types to include. Available values: {string.Join(", ", Enum.GetNames<DependencyType>())}",
        CustomParser = ParseDependencyTypes,
    };

    private static DependencyType[]? ParseDependencyTypes(ArgumentResult result)
    {
        var values = new List<DependencyType>();
        foreach (var token in result.Tokens)
        {
            if (string.IsNullOrWhiteSpace(token.Value))
                continue;

            foreach (var value in token.Value.Split(DependencyTypeDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Enum.TryParse(value, ignoreCase: true, out DependencyType dependencyType))
                {
                    result.AddError($"Invalid dependency type '{value}'");
                    return null;
                }

                values.Add(dependencyType);
            }
        }

        return [.. values.Distinct()];
    }

    private static async Task<int> UpdateAsync(string? rootDirectory, string[]? filePatterns, DependencyType[]? dependencyTypes, bool updateLockFiles, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var globs = CreateGlobs(filePatterns, error);
        if (globs is null)
        {
            return 1;
        }

        var rootPath = string.IsNullOrEmpty(rootDirectory) ? FullPath.CurrentDirectory() : FullPath.FromPath(rootDirectory);
        var dependencyTypeSet = dependencyTypes is { Length: > 0 } ? dependencyTypes.ToHashSet() : null;

        if (dependencyTypeSet is not null)
        {
            output.WriteLine("Updating: " + string.Join(',', dependencyTypeSet.OrderBy(value => value)));
        }

        WriteSearchScope(output, globs);
        var dependencies = await ScanDependenciesAsync(rootPath, globs, cancellationToken).ConfigureAwait(false);
        WriteDependenciesAsText(output, dependencies);
        var filteredDependencies = FilterDependencies(dependencies, dependencyTypeSet);

        PackageUpdater[] updaters =
        [
            new DockerPackageUpdater(),
            new NpmPackageUpdater(),
            new NuGetPackageUpdater(),
            new DotNetSdkUpdater(),
        ];

        var updatedDependencies = new ConcurrentBag<Dependency>();
        var updatableDependencies = filteredDependencies.Where(static dependency => dependency.VersionLocation?.IsUpdatable is true);
        await Parallel.ForEachAsync(
            updatableDependencies,
            new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = 1 },
            async (dependency, localCancellationToken) =>
            {
                if (dependencyTypeSet is not null && !dependencyTypeSet.Contains(dependency.Type))
                    return;

                string? updatedVersion = null;
                foreach (var updater in updaters)
                {
                    updatedVersion = await updater.UpdateAsync(dependency, localCancellationToken).ConfigureAwait(false);
                    if (updatedVersion is not null)
                        break;
                }

                if (updatedVersion is not null)
                {
                    output.WriteLine($"Updated {dependency} -> {updatedVersion}");
                    updatedDependencies.Add(dependency);
                }
            }).ConfigureAwait(false);

        if (updateLockFiles)
        {
            foreach (var updater in updaters)
            {
                await updater.UpdateLockFileAsync(rootPath, updatedDependencies, cancellationToken).ConfigureAwait(false);
            }
        }

        return 0;
    }

    private static async Task<int> ListAsync(string? rootDirectory, string[]? filePatterns, DependencyType[]? dependencyTypes, OutputFormat format, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var globs = CreateGlobs(filePatterns, error);
        if (globs is null)
        {
            return 1;
        }

        var rootPath = string.IsNullOrEmpty(rootDirectory) ? FullPath.CurrentDirectory() : FullPath.FromPath(rootDirectory);
        var dependencyTypeSet = dependencyTypes is { Length: > 0 } ? dependencyTypes.ToHashSet() : null;
        var dependencies = await ScanDependenciesAsync(rootPath, globs, cancellationToken).ConfigureAwait(false);
        var filteredDependencies = FilterDependencies(dependencies, dependencyTypeSet);

        if (format is OutputFormat.Json)
        {
            WriteDependenciesAsJson(output, filteredDependencies);
        }
        else
        {
            WriteSearchScope(output, globs);
            WriteDependenciesAsText(output, filteredDependencies);
        }

        return 0;
    }

    private static Dependency[] FilterDependencies(Dependency[] dependencies, HashSet<DependencyType>? dependencyTypeSet)
    {
        if (dependencyTypeSet is null)
            return dependencies;

        return dependencies.Where(dependency => dependencyTypeSet.Contains(dependency.Type)).ToArray();
    }

    private static async Task<Dependency[]> ScanDependenciesAsync(FullPath rootPath, GlobCollection globs, CancellationToken cancellationToken)
    {
        var options = new ScannerOptions
        {
            RecurseSubdirectories = true,
            ShouldScanFilePredicate = globs.IsMatch,
            ShouldRecursePredicate = globs.IsPartialMatch,
        };

        return (await DependencyScanner.ScanDirectoryAsync(rootPath, options, cancellationToken).ConfigureAwait(false))
            .OrderBy(dep => dep.Type)
            .ThenBy(dep => dep.Name, StringComparer.Ordinal)
            .ThenBy(dep => dep.Version, StringComparer.Ordinal)
            .ToArray();
    }

    private static void WriteSearchScope(TextWriter output, GlobCollection globs)
    {
        output.WriteLine("Searching in:");
        foreach (var glob in globs)
        {
            output.WriteLine("- " + glob);
        }
    }

    private static void WriteDependenciesAsText(TextWriter output, Dependency[] dependencies)
    {
        output.WriteLine($"{dependencies.Length} dependencies found");
        foreach (var dependency in dependencies)
        {
            output.WriteLine("- " + dependency);
        }
    }

    private static void WriteDependenciesAsJson(TextWriter output, IEnumerable<Dependency> dependencies)
    {
        var serializedDependencies = dependencies.Select(static dependency =>
        {
            var location = dependency.VersionLocation ?? dependency.NameLocation;
            return new DependencyOutput(
                Type: dependency.Type.ToString(),
                Name: dependency.Name,
                Version: dependency.Version,
                FilePath: location?.FilePath,
                IsUpdatable: dependency.VersionLocation?.IsUpdatable is true);
        });
        output.WriteLine(JsonSerializer.Serialize(serializedDependencies, ListJsonSerializerOptions));
    }

    private static GlobCollection? CreateGlobs(string[]? patterns, TextWriter error)
    {
        if (patterns is null || patterns.Length is 0)
        {
            return new GlobCollection(
                Glob.Parse("**/*", GlobOptions.None),
                Glob.Parse("!**/node_modules/**/*", GlobOptions.None),
                Glob.Parse("!**/.playwright/package/**/*", GlobOptions.None));
        }

        var parsedPatterns = new List<IGlobEvaluatable>(patterns.Length);
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            if (!Glob.TryParse(pattern, GlobOptions.IgnoreCase, out var parsedPattern))
            {
                error.WriteLine($"Glob pattern '{pattern}' is invalid");
                return null;
            }

            parsedPatterns.Add(parsedPattern);
        }

        return new GlobCollection([.. parsedPatterns]);
    }

    private sealed record DependencyOutput(string Type, string? Name, string? Version, string? FilePath, bool IsUpdatable);
}
