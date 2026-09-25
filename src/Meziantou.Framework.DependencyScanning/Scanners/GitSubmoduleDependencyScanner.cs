using System.ComponentModel;
using System.Diagnostics;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>
/// Scans Git .gitmodules files for submodule references. The dependency name is the <c>url</c> as written in .gitmodules, and the version is the
/// commit recorded in the git index, or <see langword="null"/> when the index cannot be read. The submodule <c>name</c>, <c>path</c> and <c>branch</c>,
/// and the <c>url</c> resolved against <c>remote.origin.url</c> when it is relative, are available in <see cref="Dependency.Metadata"/>.
/// </summary>
public sealed class GitSubmoduleDependencyScanner : DependencyScanner
{
    private const string GitDirectoryPrefix = "gitdir:";
    private static readonly char[] DirectorySeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.GitReference];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName(".gitmodules", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        var repositoryDirectory = Path.GetDirectoryName(context.FullPath);
        if (string.IsNullOrEmpty(repositoryDirectory))
            return;

        var submodules = await ParseGitModulesAsync(context).ConfigureAwait(false);
        if (submodules.Count is 0)
            return;

        var (gitDirectory, gitLinks) = await ReadRepositoryGitLinksAsync(context.FileSystem, repositoryDirectory, context.CancellationToken).ConfigureAwait(false);

        string? remoteUrl = null;
        if (submodules.Exists(submodule => IsRelativeUrl(submodule.Url)))
        {
            remoteUrl = await GetOriginUrlAsync(context.FileSystem, gitDirectory, context.CancellationToken).ConfigureAwait(false);
        }

        foreach (var submodule in submodules)
        {
            // Without a readable index (a source archive, a clone made with --no-checkout), the commit is unknown
            string? sha = null;
            if (gitLinks is not null && !gitLinks.TryGetValue(submodule.Path, out sha))
                continue;

            var url = IsRelativeUrl(submodule.Url) ? ResolveRelativeUrl(remoteUrl, submodule.Url) : submodule.Url;
            context.ReportDependency(this, submodule.Url, sha, DependencyType.GitReference,
                nameLocation: new NonUpdatableLocation(context),
                versionLocation: sha is null ? null : new GitSubmoduleVersionLocation(context.FileSystem, context.FullPath, repositoryDirectory, gitDirectory, submodule.Path),
                tags: [],
                metadata: [
                    KeyValuePair.Create<string, object?>("name", submodule.Name),
                    KeyValuePair.Create<string, object?>("path", submodule.Path),
                    KeyValuePair.Create<string, object?>("branch", submodule.Branch),
                    KeyValuePair.Create<string, object?>("url", url),
                ]);
        }
    }

    private static async ValueTask<List<SubmoduleEntry>> ParseGitModulesAsync(ScanFileContext context)
    {
        using var reader = new StreamReader(context.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        var text = await reader.ReadToEndAsync(context.CancellationToken).ConfigureAwait(false);

        // Entries are grouped by submodule name, as git does, so a submodule can be split across several sections
        var submodules = new List<(string Name, string? Path, string? Url, string? Branch)>();
        foreach (var entry in GitConfigParser.Parse(text))
        {
            if (entry is not { Section: "submodule", Subsection: { } name, Value: { } value })
                continue;

            var index = submodules.FindIndex(item => item.Name == name);
            if (index < 0)
            {
                index = submodules.Count;
                submodules.Add((name, null, null, null));
            }

            submodules[index] = entry.Key switch
            {
                "path" => submodules[index] with { Path = GitIndexReader.NormalizeGitPath(value) },
                "url" => submodules[index] with { Url = value },
                "branch" => submodules[index] with { Branch = value },
                _ => submodules[index],
            };
        }

        var result = new List<SubmoduleEntry>(submodules.Count);
        foreach (var submodule in submodules)
        {
            if (!string.IsNullOrEmpty(submodule.Path) && !string.IsNullOrEmpty(submodule.Url))
            {
                result.Add(new SubmoduleEntry(submodule.Name, submodule.Path, submodule.Url, submodule.Branch));
            }
        }

        return result;
    }

    private static async ValueTask<(string GitDirectory, Dictionary<string, string>? GitLinks)> ReadRepositoryGitLinksAsync(IFileSystem fileSystem, string repositoryDirectory, CancellationToken cancellationToken)
    {
        // IFileSystem cannot tell whether .git is a directory, so try it as a directory first (the index is inside),
        // then as a file containing "gitdir: <path>" (worktrees and submodules)
        var dotGitPath = Path.Combine(repositoryDirectory, ".git");
        var gitLinks = await GitIndexReader.ReadGitLinksAsync(fileSystem, dotGitPath, cancellationToken).ConfigureAwait(false);
        if (gitLinks is not null)
            return (dotGitPath, gitLinks);

        var gitDirectory = await GetGitDirectoryFromFileAsync(fileSystem, dotGitPath, cancellationToken).ConfigureAwait(false);
        if (gitDirectory is null)
            return (dotGitPath, null);

        gitLinks = await GitIndexReader.ReadGitLinksAsync(fileSystem, gitDirectory, cancellationToken).ConfigureAwait(false);
        return (gitDirectory, gitLinks);
    }

    private static async ValueTask<string?> GetOriginUrlAsync(IFileSystem fileSystem, string gitDirectory, CancellationToken cancellationToken)
    {
        var commonDirectory = await GitFileSystemUtilities.GetCommonDirectoryAsync(fileSystem, gitDirectory, cancellationToken).ConfigureAwait(false);
        if (commonDirectory is null)
            return null;

        var config = await GitFileSystemUtilities.TryReadAllTextAsync(fileSystem, Path.Combine(commonDirectory, "config"), cancellationToken).ConfigureAwait(false);
        if (config is null)
            return null;

        string? result = null;
        foreach (var entry in GitConfigParser.Parse(config))
        {
            if (entry is { Section: "remote", Subsection: "origin", Key: "url", Value: { Length: > 0 } url })
            {
                // git uses the first url of a remote to fetch
                result ??= url;
            }
        }

        return result;
    }

    private static bool IsRelativeUrl(string url) => url.StartsWith("./", StringComparison.Ordinal) || url.StartsWith("../", StringComparison.Ordinal);

    /// <summary>
    /// Resolves a url relative to the url of the superproject, following git's <c>relative_url</c>: each <c>../</c> removes
    /// the last path component of the remote url, or its <c>host:</c> path for a scp-like url.
    /// Returns <see langword="null"/> when there is no remote url, or when it is itself a relative path.
    /// </summary>
    private static string? ResolveRelativeUrl(string? remoteUrl, string url)
    {
        if (string.IsNullOrEmpty(remoteUrl))
            return null;

        remoteUrl = remoteUrl.TrimEnd('/');
        if (!remoteUrl.Contains("://", StringComparison.Ordinal) && !Path.IsPathRooted(remoteUrl) && !IsScpLikeUrl(remoteUrl))
            return null;

        var useColonSeparator = false;
        while (true)
        {
            if (url.StartsWith("../", StringComparison.Ordinal))
            {
                url = url[3..];
                var slashIndex = remoteUrl.LastIndexOfAny(DirectorySeparators);
                var schemeIndex = remoteUrl.IndexOf("://", StringComparison.Ordinal);
                if (slashIndex >= 0 && (schemeIndex < 0 || slashIndex > schemeIndex + 2))
                {
                    remoteUrl = remoteUrl[..slashIndex];
                }
                else if (!useColonSeparator && remoteUrl.LastIndexOf(':', StringComparison.Ordinal) is var colonIndex and >= 0 && schemeIndex < 0)
                {
                    remoteUrl = remoteUrl[..colonIndex];
                    useColonSeparator = true;
                }
                else
                {
                    return null;
                }
            }
            else if (url.StartsWith("./", StringComparison.Ordinal))
            {
                url = url[2..];
            }
            else
            {
                break;
            }
        }

        return remoteUrl + (useColonSeparator ? ":" : "/") + url.TrimEnd('/');

        // [user@]host:path
        static bool IsScpLikeUrl(string url)
        {
            var colonIndex = url.IndexOf(':', StringComparison.Ordinal);
            return colonIndex > 1 && url.IndexOf('/', StringComparison.Ordinal) is var slashIndex && (slashIndex < 0 || slashIndex > colonIndex);
        }
    }

    private static async ValueTask<string?> GetGitDirectoryFromFileAsync(IFileSystem fileSystem, string dotGitPath, CancellationToken cancellationToken)
    {
        var content = await GitFileSystemUtilities.TryReadAllTextAsync(fileSystem, dotGitPath, cancellationToken).ConfigureAwait(false);
        if (content is null)
            return null;

        var newLineIndex = content.AsSpan().IndexOfAny('\r', '\n');
        var dotGitContent = (newLineIndex < 0 ? content : content[..newLineIndex]).Trim();
        if (!dotGitContent.StartsWith(GitDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var relativeGitDirectory = dotGitContent[GitDirectoryPrefix.Length..].Trim();
        var dotGitDirectory = Path.GetDirectoryName(dotGitPath);
        if (string.IsNullOrEmpty(relativeGitDirectory) || string.IsNullOrEmpty(dotGitDirectory))
            return null;

        return GitFileSystemUtilities.TryResolvePath(dotGitDirectory, relativeGitDirectory, out var gitDirectory) ? gitDirectory : null;
    }

    private sealed class GitSubmoduleVersionLocation : Location
    {
        private readonly string _repositoryDirectory;
        private readonly string _gitDirectory;
        private readonly string _submodulePath;

        public GitSubmoduleVersionLocation(IFileSystem fileSystem, string filePath, string repositoryDirectory, string gitDirectory, string submodulePath)
            : base(fileSystem, filePath)
        {
            _repositoryDirectory = repositoryDirectory;
            _gitDirectory = gitDirectory;
            _submodulePath = submodulePath;
        }

        public override bool IsUpdatable => true;

        protected internal override async Task UpdateCoreAsync(string? oldValue, string newValue, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(newValue))
                throw new ArgumentException("The new submodule commit SHA cannot be null or empty.", nameof(newValue));

            if (oldValue is not null)
            {
                var gitLinks = await GitIndexReader.ReadGitLinksAsync(FileSystem, _gitDirectory, cancellationToken).ConfigureAwait(false);
                if (gitLinks is null || !gitLinks.TryGetValue(_submodulePath, out var currentValue))
                    throw new DependencyScannerException($"Submodule '{_submodulePath}' was not found in the git index.");

                if (!string.Equals(currentValue, oldValue, StringComparison.OrdinalIgnoreCase))
                    throw new DependencyScannerException($"Expected value not found for submodule '{_submodulePath}'. Current value: {currentValue}; expected value: {oldValue}");
            }

            var gitProcessName = await GetAvailableGitProcessNameAsync(cancellationToken).ConfigureAwait(false);
            await RunGitCommandAsync(gitProcessName, _repositoryDirectory, ["submodule", "update", "--init", "--", _submodulePath], cancellationToken).ConfigureAwait(false);

            var submoduleDirectory = Path.Combine(_repositoryDirectory, _submodulePath);
            await RunGitCommandAsync(gitProcessName, submoduleDirectory, ["fetch", "--all", "--tags", "--prune"], cancellationToken).ConfigureAwait(false);
            await RunGitCommandAsync(gitProcessName, submoduleDirectory, ["checkout", "--detach", newValue], cancellationToken).ConfigureAwait(false);
            await RunGitCommandAsync(gitProcessName, _repositoryDirectory, ["add", "--", _submodulePath], cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string> GetAvailableGitProcessNameAsync(CancellationToken cancellationToken)
    {
        if (await IsProcessAvailableAsync("git", cancellationToken).ConfigureAwait(false))
            return "git";

        if (await IsProcessAvailableAsync("mingit", cancellationToken).ConfigureAwait(false))
            return "mingit";

        throw new InvalidOperationException("Cannot update Git submodule because neither 'git' nor 'mingit' is available.");
    }

    private static async Task<bool> IsProcessAvailableAsync(string processName, CancellationToken cancellationToken)
    {
        try
        {
            using var process = StartGitProcess(processName, workingDirectory: null, ["--version"]);
            if (process is null)
                return false;

            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorOutputTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await WaitForExitOrKillAsync(process, cancellationToken).ConfigureAwait(false);
            await standardOutputTask.ConfigureAwait(false);
            await errorOutputTask.ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static async Task RunGitCommandAsync(string processName, string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var process = StartGitProcess(processName, workingDirectory, arguments) ?? throw new DependencyScannerException($"Cannot start process '{processName}'.");

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorOutputTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await WaitForExitOrKillAsync(process, cancellationToken).ConfigureAwait(false);
        await standardOutputTask.ConfigureAwait(false);
        var errorOutput = await errorOutputTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new DependencyScannerException($"Command '{processName} {string.Join(" ", arguments)}' failed with exit code {process.ExitCode}: {errorOutput}");
        }
    }

    private static Process? StartGitProcess(string processName, string? workingDirectory, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(processName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        if (workingDirectory is not null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        // Never wait for credentials: there is nobody to answer the prompt
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo);
        process?.StandardInput.Close();
        return process;
    }

    private static async Task WaitForExitOrKillAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Otherwise the orphaned git process keeps running and may hold index.lock
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process has already exited
            }
            catch (Win32Exception)
            {
                // The process is exiting or cannot be terminated
            }

            throw;
        }
    }

    private readonly record struct SubmoduleEntry(string Name, string Path, string Url, string? Branch);
}
