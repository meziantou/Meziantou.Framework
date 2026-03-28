using System.Collections.Concurrent;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Runtime.CompilerServices;
using Meziantou.Framework;
using Meziantou.Framework.DependencyScanning;
using Meziantou.Framework.Globbing;

[assembly: InternalsVisibleTo("Meziantou.Framework.DependencyScanning.Tool.Tests")]

namespace Meziantou.Framework.DependencyScanning.Tool;

internal static class Program
{
    private static readonly char[] DependencyTypeDelimiters = [',', ';', ' '];

    public static Task<int> Main(string[] args)
    {
        return MainImpl(args, configure: null);
    }

    internal static Task<int> MainImpl(string[] args, Action<InvocationConfiguration>? configure)
    {
        var rootCommand = new RootCommand();
        AddUpdateCommand(rootCommand);
        var invocationConfiguration = new InvocationConfiguration();
        configure?.Invoke(invocationConfiguration);
        return rootCommand.Parse(args).InvokeAsync(invocationConfiguration);
    }

    private static void AddUpdateCommand(RootCommand rootCommand)
    {
        var rootDirectoryOption = new Option<string?>("--directory") { Description = "Root directory" };
        var filesOption = new Option<string[]?>("--files") { Description = "Glob patterns to find files to update" };
        var dependencyTypesOption = new Option<DependencyType[]?>("--dependency-type")
        {
            Description = $"Dependency types to update. Available values: {string.Join(", ", [nameof(DependencyType.NuGet), nameof(DependencyType.DotNetSdk), nameof(DependencyType.Npm)])}",
            CustomParser = ParseDependencyTypes,
        };
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
                cancellationToken);
        });

        rootCommand.Subcommands.Add(updateCommand);
    }

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

    private static async Task<int> UpdateAsync(string? rootDirectory, string[]? filePatterns, DependencyType[]? dependencyTypes, bool updateLockFiles, CancellationToken cancellationToken)
    {
        var globs = CreateGlobs(filePatterns);
        if (globs is null)
        {
            return 1;
        }

        var rootPath = string.IsNullOrEmpty(rootDirectory) ? FullPath.CurrentDirectory() : FullPath.FromPath(rootDirectory);
        var dependencyTypeSet = dependencyTypes is { Length: > 0 } ? dependencyTypes.ToHashSet() : null;

        if (dependencyTypeSet is not null)
        {
            Console.WriteLine("Updating: " + string.Join(',', dependencyTypeSet.OrderBy(value => value)));
        }

        Console.WriteLine("Searching in:");
        foreach (var glob in globs)
        {
            Console.WriteLine("- " + glob);
        }

        var options = new ScannerOptions
        {
            RecurseSubdirectories = true,
            ShouldScanFilePredicate = globs.IsMatch,
            ShouldRecursePredicate = globs.IsPartialMatch,
        };

        var dependencies = (await DependencyScanner.ScanDirectoryAsync(rootPath, options, cancellationToken).ConfigureAwait(false))
            .OrderBy(dep => dep.Type)
            .ThenBy(dep => dep.Name, StringComparer.Ordinal)
            .ThenBy(dep => dep.Version, StringComparer.Ordinal)
            .ToArray();
        Console.WriteLine($"{dependencies.Length} dependencies found");
        foreach (var dependency in dependencies)
        {
            Console.WriteLine("- " + dependency);
        }

        PackageUpdater[] updaters =
        [
            new NpmPackageUpdater(),
            new NuGetPackageUpdater(),
            new DotNetSdkUpdater(),
        ];

        var updatedDependencies = new ConcurrentBag<Dependency>();
        var updatableDependencies = dependencies.Where(static dependency => dependency.VersionLocation?.IsUpdatable is true);
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
                    Console.WriteLine($"Updated {dependency} -> {updatedVersion}");
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

        GlobCollection? CreateGlobs(string[]? patterns)
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
                    Console.Error.WriteLine($"Glob pattern '{pattern}' is invalid");
                    return null;
                }

                parsedPatterns.Add(parsedPattern);
            }

            return new GlobCollection([.. parsedPatterns]);
        }
    }
}
