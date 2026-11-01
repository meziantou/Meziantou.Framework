using System.ComponentModel;
using System.Diagnostics;

namespace Meziantou.Framework.Globbing.Tests;

public sealed class GitIgnoreMatcherTests
{
    // The expected results were checked with git 2.56 (git ls-files --others --exclude-standard), and
    // MatchesGitLsFilesAndCheckIgnore compares them with the installed git
    private static readonly string[] TreeFiles =
    [
        ".gitignore",
        "a.txt",
        "a.log",
        "keep.log",
        "notes.bak",
        "secret.txt",
        ".DS_Store",
        ".hidden/file.txt",
        "build/out.dll",
        "build/.gitignore",
        "docs/tmpfile.txt",
        "docs/sub/c.log",
        "tmpdir/x.txt",
        "logs/a.txt",
        "src/.gitignore",
        "src/logs",
        "src/local.txt",
        "src/important.log",
        "src/x.tmp",
        "src/secret.txt",
        "src/generated/g.cs",
        "src/App/.gitignore",
        "src/App/local.txt",
        "src/App/y.tmp",
        "src/App/bin/a.dll",
        "src/App/generated/g.cs",
        "src/App/Program.cs",
    ];

    private static readonly string[] ExpectedFiles =
    [
        ".gitignore",
        ".hidden/file.txt",
        "a.txt",
        "docs/sub/c.log",
        "docs/tmpfile.txt",
        "keep.log",
        "src/.gitignore",
        "src/App/.gitignore",
        "src/App/Program.cs",
        "src/App/local.txt",
        "src/App/y.tmp",
        "src/important.log",
        "src/logs",
    ];

    [Fact]
    public void DocumentationExample_EnumerateFiles()
    {
        #region GitIgnoreMatcher_EnumerateFiles
        using var rootDirectory = TemporaryDirectory.Create();
        rootDirectory.CreateTextFile(".gitignore", "*.log\n");
        rootDirectory.CreateTextFile("src/.gitignore", "bin/\n!important.log\n");
        rootDirectory.CreateEmptyFile("src/Program.cs");
        rootDirectory.CreateEmptyFile("src/bin/App.dll");
        rootDirectory.CreateEmptyFile("src/trace.log");
        rootDirectory.CreateEmptyFile("src/important.log");

        var matcher = new GitIgnoreMatcher(rootDirectory.FullPath);

        // The .gitignore file of a directory applies to its content, and takes precedence over the parent ones
        Assert.True(matcher.IsIgnored("src/trace.log"));
        Assert.False(matcher.IsIgnored("src/important.log"));

        // Enumerate the files that are not ignored. The enumeration does not go into src/bin.
        var files = matcher.EnumerateFiles()
            .Select(path => Path.GetRelativePath(rootDirectory.FullPath, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal([".gitignore", "src/.gitignore", "src/Program.cs", "src/important.log"], files);
        #endregion
    }

    [Fact]
    public void EnumerateFiles_FollowsGitPrecedence()
    {
        using var rootDirectory = TemporaryDirectory.Create();
        var globalExcludesFile = CreateTree(rootDirectory);
        var matcher = new GitIgnoreMatcher(rootDirectory.FullPath, new GitIgnoreMatcherOptions { GlobalExcludesFile = globalExcludesFile });

        Assert.Equal(ExpectedFiles, GetRelativeFiles(matcher, rootDirectory.FullPath));
    }

    [Fact]
    public void IsIgnored_FollowsGitPrecedence()
    {
        using var rootDirectory = TemporaryDirectory.Create();
        var globalExcludesFile = CreateTree(rootDirectory);
        var matcher = new GitIgnoreMatcher(rootDirectory.FullPath, new GitIgnoreMatcherOptions { GlobalExcludesFile = globalExcludesFile });

        foreach (var file in TreeFiles)
        {
            Assert.Equal(!ExpectedFiles.Contains(file), matcher.IsIgnored(file), file);
        }

        Assert.True(matcher.IsIgnored("build"));
        Assert.True(matcher.IsIgnored("build/"));
        Assert.True(matcher.IsIgnored("src/App/bin", PathItemType.Directory));
        Assert.False(matcher.IsIgnored("src/App", PathItemType.Directory));
        Assert.False(matcher.IsIgnored("src/logs", PathItemType.File));
        Assert.True(matcher.IsIgnored("src/logs", PathItemType.Directory));
        Assert.False(matcher.IsIgnored("", PathItemType.Directory));
    }

    [Fact]
    public void Options_DisableTheRepositoryExcludeFile()
    {
        using var rootDirectory = TemporaryDirectory.Create();
        CreateTree(rootDirectory);
        var matcher = new GitIgnoreMatcher(rootDirectory.FullPath, new GitIgnoreMatcherOptions { UseRepositoryExcludeFile = false });

        Assert.False(matcher.IsIgnored("secret.txt", PathItemType.File));
        Assert.False(matcher.IsIgnored("notes.bak", PathItemType.File)); // no global excludes file
        Assert.True(matcher.IsIgnored("a.log", PathItemType.File));
    }

    [Fact]
    public void RepositoryExcludeFileOfAWorktree()
    {
        using var rootDirectory = TemporaryDirectory.Create();
        rootDirectory.CreateTextFile("repo/.git/info/exclude", "excluded.txt\n");
        rootDirectory.CreateTextFile("repo/.git/worktrees/wt/commondir", "../..\n");
        rootDirectory.CreateTextFile("wt/.git", "gitdir: ../repo/.git/worktrees/wt\n");

        var matcher = new GitIgnoreMatcher(rootDirectory.GetFullPath("wt"));

        Assert.True(matcher.IsIgnored("excluded.txt", PathItemType.File));
        Assert.False(matcher.IsIgnored("other.txt", PathItemType.File));
    }

    [Fact]
    public void EnumerateFiles_DoesNotReadTheGitIgnoreFilesOfIgnoredDirectories()
    {
        using var rootDirectory = TemporaryDirectory.Create();
        rootDirectory.CreateTextFile(".gitignore", "build/\n");
        rootDirectory.CreateTextFile("build/.gitignore", "!*\n");
        rootDirectory.CreateEmptyFile("build/out.dll");
        rootDirectory.CreateEmptyFile(".git/HEAD");

        var matcher = new GitIgnoreMatcher(rootDirectory.FullPath);

        Assert.Equal([".gitignore"], GetRelativeFiles(matcher, rootDirectory.FullPath));
        Assert.True(matcher.IsIgnored("build/out.dll", PathItemType.File));
    }

    [Fact]
    public void MatchesGitLsFilesAndCheckIgnore()
    {
        using var rootDirectory = TemporaryDirectory.Create();
        var configFile = rootDirectory.CreateEmptyFile("gitconfig");
        try
        {
            RunGit(rootDirectory.FullPath, configFile, excludesFile: "", "init", "--quiet");
        }
        catch (Win32Exception)
        {
            global::Xunit.Assert.Skip("git is not installed");
        }

        // The helper files are moved to the .git directory, which neither git nor the matcher enumerates
        var gitConfigFile = Path.Combine(rootDirectory.FullPath, ".git", "test-gitconfig");
        File.Move(configFile, gitConfigFile);
        var globalExcludesFile = CreateTree(rootDirectory);

        var gitFiles = RunGit(rootDirectory.FullPath, gitConfigFile, globalExcludesFile, "ls-files", "--others", "--exclude-standard", "-z")
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var matcher = new GitIgnoreMatcher(rootDirectory.FullPath, new GitIgnoreMatcherOptions { GlobalExcludesFile = globalExcludesFile });
        Assert.Equal(gitFiles, GetRelativeFiles(matcher, rootDirectory.FullPath));
        Assert.Equal(ExpectedFiles, gitFiles);

        // check-ignore also reports the ignored directories, and the paths below them
        var paths = TreeFiles
            .SelectMany(file => Enumerable.Range(1, file.Count(c => c == '/') + 1).Select(count => string.Join('/', file.Split('/').Take(count))))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var gitIgnored = RunGit(rootDirectory.FullPath, gitConfigFile, globalExcludesFile, ["check-ignore", "--stdin", "-z"], string.Join('\0', paths) + '\0')
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var path in paths)
        {
            Assert.Equal(gitIgnored.Contains(path), matcher.IsIgnored(path), path);
        }
    }

    // Returns the path of the global excludes file, which is in the .git directory so that it is not enumerated
    private static string CreateTree(TemporaryDirectory rootDirectory)
    {
        foreach (var file in TreeFiles)
        {
            rootDirectory.CreateEmptyFile(file);
        }

        rootDirectory.CreateTextFile(".gitignore", "*.log\n/build/\n!keep.log\ntmp*\n!/docs/**\nlogs/\n");
        rootDirectory.CreateTextFile("build/.gitignore", "!out.dll\n");
        rootDirectory.CreateTextFile("src/.gitignore", "generated/\n!important.log\n*.tmp\n/local.txt\n");
        rootDirectory.CreateTextFile("src/App/.gitignore", "!*.tmp\nbin/\n");
        rootDirectory.CreateTextFile(".git/info/exclude", "secret.txt\n");
        return rootDirectory.CreateTextFile(".git/test-global-ignore", ".DS_Store\n*.bak\n");
    }

    private static string[] GetRelativeFiles(GitIgnoreMatcher matcher, string rootDirectory)
    {
        return matcher.EnumerateFiles()
            .Select(path => Path.GetRelativePath(rootDirectory, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string RunGit(string workingDirectory, string configFile, string excludesFile, params string[] arguments)
    {
        return RunGit(workingDirectory, configFile, excludesFile, arguments, standardInput: null);
    }

    private static string RunGit(string workingDirectory, string configFile, string excludesFile, string[] arguments, string? standardInput)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // Ignore the configuration of the machine, which may set its own excludes file
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = configFile;
        if (excludesFile.Length > 0)
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("core.excludesFile=" + excludesFile);
        }

        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("core.ignoreCase=false");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
        }

        process.StandardInput.Close();
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        // check-ignore exits with 1 when no path is ignored
        Assert.True(process.ExitCode is 0 or 1, error.Result);
        return output;
    }
}
