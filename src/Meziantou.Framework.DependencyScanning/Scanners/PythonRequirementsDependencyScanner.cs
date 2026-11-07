using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>
/// Scans Python requirements and constraints files for PyPI packages pinned with <c>==</c>.
/// Files named <c>*requirements*.txt</c> or <c>*constraints*.txt</c> (for instance <c>requirements.txt</c>, <c>requirements-dev.txt</c> or <c>constraints.txt</c>),
/// their pip-tools <c>.in</c> counterparts (for instance <c>requirements.in</c>), and <c>*.txt</c> and <c>*.in</c> files in a <c>requirements</c> directory are scanned.
/// </summary>
public sealed class PythonRequirementsDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.PyPi];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        if (!context.HasExtension([".txt", ".in"], ignoreCase: true))
            return false;

        if (context.FileName.Contains("requirements", StringComparison.OrdinalIgnoreCase) || context.FileName.Contains("constraints", StringComparison.OrdinalIgnoreCase))
            return true;

        return Path.GetFileName(context.Directory.TrimEnd(['/', '\\'])).Equals("requirements", StringComparison.OrdinalIgnoreCase);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        using var sr = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var lineNo = 0;
        var isContinuation = false;
        string? line;
        while ((line = await sr.ReadLineAsync(context.CancellationToken).ConfigureAwait(false)) is not null)
        {
            lineNo++;

            // A line ending with a backslash, and without a comment, is joined with the next one. The requirement is on
            // the first line, the next ones hold options such as --hash=sha256:...
            var wasContinuation = isContinuation;
            isContinuation = line.AsSpan().TrimEnd().EndsWith('\\') && !HasComment(line);
            if (wasContinuation)
                continue;

            if (!TryParseLine(line, out var nameIndex, out var nameLength, out var versionIndex, out var versionLength))
                continue;

            context.ReportDependency(this, line.Substring(nameIndex, nameLength), line.Substring(versionIndex, versionLength), DependencyType.PyPi,
                nameLocation: new TextLocation(context.FileSystem, context.FullPath, lineNo, nameIndex + 1, nameLength),
                versionLocation: new TextLocation(context.FileSystem, context.FullPath, lineNo, versionIndex + 1, versionLength));
        }
    }

    private static bool HasComment(string line)
    {
        var index = -1;
        while ((index = line.IndexOf('#', index + 1, StringComparison.Ordinal)) >= 0)
        {
            if (index is 0 || line[index - 1] is ' ' or '\t')
                return true;
        }

        return false;
    }

    // name[extras] == version [; marker] [--option ...] [# comment] [\]
    // https://pip.pypa.io/en/stable/reference/requirements-file-format/
    private static bool TryParseLine(string line, out int nameIndex, out int nameLength, out int versionIndex, out int versionLength)
    {
        nameIndex = nameLength = versionIndex = versionLength = 0;

        var index = SkipWhitespaces(line, 0);

        // https://packaging.python.org/en/latest/specifications/name-normalization/
        nameIndex = index;
        while (index < line.Length && (char.IsAsciiLetterOrDigit(line[index]) || line[index] is '.' or '_' or '-'))
        {
            index++;
        }

        nameLength = index - nameIndex;
        if (nameLength is 0 || !char.IsAsciiLetterOrDigit(line[nameIndex]) || !char.IsAsciiLetterOrDigit(line[index - 1]))
            return false;

        index = SkipWhitespaces(line, index);
        if (index < line.Length && line[index] is '[')
        {
            var endOfExtras = line.IndexOf(']', index, StringComparison.Ordinal);
            if (endOfExtras < 0)
                return false;

            index = SkipWhitespaces(line, endOfExtras + 1);
        }

        // Only exact pins (==) are reported. === (arbitrary equality) and other operators are not.
        if (index + 2 > line.Length || line[index] is not '=' || line[index + 1] is not '=')
            return false;

        index += 2;
        if (index < line.Length && line[index] is '=')
            return false;

        index = SkipWhitespaces(line, index);

        // https://packaging.python.org/en/latest/specifications/version-specifiers/
        versionIndex = index;
        while (index < line.Length && (char.IsAsciiLetterOrDigit(line[index]) || line[index] is '.' or '_' or '-' or '+' or '!' or '*'))
        {
            index++;
        }

        versionLength = index - versionIndex;
        if (versionLength is 0)
            return false;

        var afterVersion = SkipWhitespaces(line, index);
        if (afterVersion >= line.Length)
            return true;

        var next = line[afterVersion];
        if (next is ';') // environment marker
            return true;

        if (next is '\\') // line continuation
            return line.AsSpan(afterVersion + 1).IsWhiteSpace();

        // A comment or a per-requirement option (--hash=...) must be separated from the version by a whitespace
        if (afterVersion == index)
            return false;

        return next is '#' || line.AsSpan(afterVersion).StartsWith("--", StringComparison.Ordinal);

        static int SkipWhitespaces(string line, int index)
        {
            while (index < line.Length && line[index] is ' ' or '\t')
            {
                index++;
            }

            return index;
        }
    }
}
