using System.ComponentModel;
using System.Diagnostics;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Git .gitmodules files for submodule references.</summary>
public sealed class GitSubmoduleDependencyScanner : DependencyScanner
{
    private const string GitDirectoryPrefix = "gitdir:";

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
        if (gitDirectory is null || gitLinks is null)
            return;

        foreach (var submodule in submodules)
        {
            if (!gitLinks.TryGetValue(submodule.Path, out var sha))
                continue;

            context.ReportDependency(this, submodule.Url, sha, DependencyType.GitReference,
                nameLocation: new NonUpdatableLocation(context),
                versionLocation: new GitSubmoduleVersionLocation(context.FileSystem, context.FullPath, repositoryDirectory, gitDirectory, submodule.Path));
        }
    }

    private static async ValueTask<List<SubmoduleEntry>> ParseGitModulesAsync(ScanFileContext context)
    {
        using var reader = new StreamReader(context.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        var text = await reader.ReadToEndAsync(context.CancellationToken).ConfigureAwait(false);

        // Entries are grouped by submodule name, as git does, so a submodule can be split across several sections
        var submodules = new List<(string Name, string? Path, string? Url)>();
        foreach (var entry in GitConfigParser.Parse(text))
        {
            if (entry is not { Section: "submodule", Subsection: { } name, Value: { } value })
                continue;

            var index = submodules.FindIndex(item => item.Name == name);
            if (index < 0)
            {
                index = submodules.Count;
                submodules.Add((name, null, null));
            }

            if (entry.Key is "path")
            {
                submodules[index] = submodules[index] with { Path = GitIndexReader.NormalizeGitPath(value) };
            }
            else if (entry.Key is "url")
            {
                submodules[index] = submodules[index] with { Url = value };
            }
        }

        var result = new List<SubmoduleEntry>(submodules.Count);
        foreach (var submodule in submodules)
        {
            if (!string.IsNullOrEmpty(submodule.Path) && !string.IsNullOrEmpty(submodule.Url))
            {
                result.Add(new SubmoduleEntry(submodule.Path, submodule.Url));
            }
        }

        return result;
    }

    private static async ValueTask<(string? GitDirectory, Dictionary<string, string>? GitLinks)> ReadRepositoryGitLinksAsync(IFileSystem fileSystem, string repositoryDirectory, CancellationToken cancellationToken)
    {
        // IFileSystem cannot tell whether .git is a directory, so try it as a directory first (the index is inside),
        // then as a file containing "gitdir: <path>" (worktrees and submodules)
        var dotGitPath = Path.Combine(repositoryDirectory, ".git");
        var gitLinks = await GitIndexReader.ReadGitLinksAsync(fileSystem, dotGitPath, cancellationToken).ConfigureAwait(false);
        if (gitLinks is not null)
            return (dotGitPath, gitLinks);

        var gitDirectory = await GetGitDirectoryFromFileAsync(fileSystem, dotGitPath, cancellationToken).ConfigureAwait(false);
        if (gitDirectory is null)
            return default;

        gitLinks = await GitIndexReader.ReadGitLinksAsync(fileSystem, gitDirectory, cancellationToken).ConfigureAwait(false);
        return gitLinks is null ? default : (gitDirectory, gitLinks);
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

    private readonly record struct SubmoduleEntry(string Path, string Url);
}
