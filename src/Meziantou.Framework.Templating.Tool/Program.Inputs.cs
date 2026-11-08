using Meziantou.Framework.Globbing;

namespace Meziantou.Framework.Templating.Tool;

internal static partial class Program
{
    /// <summary>
    /// Resolves the template files of the <c>--input</c> values. A value that is not an existing file and holds a
    /// wildcard is a glob pattern (MSBuild dialect). Several values, or a glob pattern, make the run a multi-template run,
    /// even when they match a single file, so that the location of the outputs does not depend on the number of matches.
    /// </summary>
    private static ResolvedInputs? ResolveInputFiles(RenderOptions options, List<string> errors)
    {
        var rootDirectory = string.IsNullOrWhiteSpace(options.RootDirectory) ? FullPath.CurrentDirectory() : FullPath.FromPath(options.RootDirectory);
        var files = new List<InputFile>();
        var seenFiles = new HashSet<FullPath>();
        var isMultiple = options.Inputs.Length > 1;
        foreach (var input in options.Inputs)
        {
            var path = FullPath.Combine(rootDirectory, input);
            if (File.Exists(path))
            {
                if (seenFiles.Add(path))
                {
                    files.Add(new InputFile(path, path.Name));
                }

                continue;
            }

            if (!input.AsSpan().ContainsAny('*', '?'))
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"The input template file '{path}' does not exist"));
                continue;
            }

            isMultiple = true;
            if (!Glob.TryParse(input, GlobDialect.MSBuild, GlobOptions.None, out var glob))
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"The glob pattern '{input}' is invalid"));
                continue;
            }

            // A file keeps its path relative to the part of the pattern that has no wildcard, like
            // %(RecursiveDir) in MSBuild: "templates/**/*.tt" generates "<output-directory>/sub/a.cs" for "templates/sub/a.tt"
            var baseDirectory = glob.TrySplitBasePath(out var basePath, out _) ? FullPath.Combine(rootDirectory, basePath) : rootDirectory;
            var matches = glob.EnumerateFiles(rootDirectory)
                .Select(FullPath.FromPath)
                .Order()
                .ToList();
            if (matches.Count is 0)
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"The glob pattern '{input}' does not match any file"));
                continue;
            }

            foreach (var match in matches)
            {
                if (seenFiles.Add(match))
                {
                    var relativePath = match.IsChildOf(baseDirectory) ? match.MakePathRelativeTo(baseDirectory) : match.Name;
                    files.Add(new InputFile(match, relativePath));
                }
            }
        }

        if (errors.Count > 0)
            return null;

        return new ResolvedInputs(files, isMultiple);
    }

    private sealed record InputFile(FullPath Path, string RelativePath);

    private sealed record ResolvedInputs(List<InputFile> Files, bool IsMultiple);

    private sealed record LoadedTemplate(FullPath InputPath, Template Template, FullPath? OutputPath);

    private sealed class RenderOptions
    {
        public required string[] Inputs { get; init; }
        public required string? OutputFile { get; init; }
        public required string? OutputDirectory { get; init; }
        public required string? OutputExtension { get; init; }
        public required string? RootDirectory { get; init; }
        public required string[] Arguments { get; init; }
        public required string? ArgumentsFile { get; init; }
        public required string? StartCodeBlockDelimiter { get; init; }
        public required string? EndCodeBlockDelimiter { get; init; }
        public required string? OutputEncoding { get; init; }
        public required string? LineEnding { get; init; }
    }

    private sealed class RenderSettings
    {
        public required string? StartCodeBlockDelimiter { get; init; }
        public required string? EndCodeBlockDelimiter { get; init; }
        public required string? LineEnding { get; init; }
        public required Encoding OutputEncoding { get; init; }
        public required string? OutputExtension { get; init; }
        public required FullPath? OutputFile { get; init; }
        public required FullPath? OutputDirectory { get; init; }
    }
}
