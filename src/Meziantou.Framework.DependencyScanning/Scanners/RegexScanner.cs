using System.Diagnostics;
using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.Globbing;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>
/// A configurable scanner that uses regular expressions to extract dependencies from files.
/// <example>
/// <code><![CDATA[
/// var scanner = new RegexScanner
/// {
///     FilePatterns = [Glob.Parse("**/*.custom", GlobDialect.Standard, GlobOptions.IgnoreCase)],
///     DependencyType = DependencyType.DockerImage,
///     Regex = new Regex(@"image:\s*(?<name>[a-z/]+)(:(?<version>[0-9.]+))?", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10))
/// };
/// var options = new ScannerOptions { Scanners = [scanner] };
/// var dependencies = await DependencyScanner.ScanDirectoryAsync("C:\\MyProject", options, cancellationToken);
/// ]]></code>
/// </example>
/// </summary>
public sealed class RegexScanner : DependencyScanner
{
    private const string NameGroupName = "name";
    private const string VersionGroupName = "version";

    private bool _frozen;

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes
    {
        get
        {
            _frozen = true;
            field ??= [DependencyType];
            return field;
        }
    }

    /// <summary>Gets or sets the regular expression used to match dependencies. The expression must include named groups 'name' and optionally 'version'.</summary>
    public Regex? Regex { get; set; }

    /// <summary>Gets or sets the type of dependency to report when a match is found.</summary>
    public DependencyType DependencyType
    {
        get => field;
        set
        {
            if (_frozen)
                throw new InvalidOperationException("The scanner is already used and cannot be modified.");

            field = value;
        }
    }

    /// <summary>Gets or sets the glob patterns specifying which files to scan. Patterns are matched against the path relative to the root directory of the scan (for instance <c>build/*.yml</c>). If <see langword="null"/>, all files are scanned.</summary>
    public GlobCollection? FilePatterns { get; set; }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        var regex = Regex;
        if (regex is null)
            return;

        _frozen = true;

        using var sr = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
        var text = await sr.ReadToEndAsync(context.CancellationToken).ConfigureAwait(false);

        var lineTracker = new LineTracker(text);
        foreach (Match match in regex.Matches(text))
        {
            Debug.Assert(match.Success);

            var nameGroup = match.Groups[NameGroupName];
            var name = nameGroup.Value;
            if (!string.IsNullOrEmpty(name))
            {
                var versionGroup = match.Groups[VersionGroupName];
                if (versionGroup.Success)
                {
                    var version = versionGroup.Value;

                    // Groups can appear in any order in the pattern, compute the first one first so the tracker only moves forward
                    TextLocation nameLocation;
                    TextLocation versionLocation;
                    if (nameGroup.Index <= versionGroup.Index)
                    {
                        nameLocation = lineTracker.CreateLocation(context, nameGroup.Index, nameGroup.Length);
                        versionLocation = lineTracker.CreateLocation(context, versionGroup.Index, versionGroup.Length);
                    }
                    else
                    {
                        versionLocation = lineTracker.CreateLocation(context, versionGroup.Index, versionGroup.Length);
                        nameLocation = lineTracker.CreateLocation(context, nameGroup.Index, nameGroup.Length);
                    }

                    context.ReportDependency(this, name, version, DependencyType, nameLocation, versionLocation);
                }
                else
                {
                    var nameLocation = lineTracker.CreateLocation(context, nameGroup.Index, nameGroup.Length);
                    context.ReportDependency(this, name, version: null, DependencyType, nameLocation, versionLocation: null);
                }
            }
        }
    }

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        if (FilePatterns is not null)
            return FilePatterns.IsMatch(context.RelativeDirectory, context.FileName);

        return true;
    }

    /// <summary>Computes line and column numbers of increasing offsets without rescanning the text from the start. <c>\r\n</c>, <c>\r</c> and <c>\n</c> are line breaks.</summary>
    private sealed class LineTracker(string text)
    {
        private int _index;
        private int _line = 1;
        private int _lineStart;

        public TextLocation CreateLocation(ScanFileContext context, int index, int length)
        {
            // Matches are returned in order, except with RegexOptions.RightToLeft
            if (index < _index)
            {
                _index = 0;
                _line = 1;
                _lineStart = 0;
            }

            while (_index < index)
            {
                var c = text[_index];
                if (c is '\n' || (c is '\r' && (_index + 1 >= text.Length || text[_index + 1] is not '\n')))
                {
                    _line++;
                    _lineStart = _index + 1;
                }

                _index++;
            }

            // LineNumber and LinePosition are 1-based
            return new TextLocation(context.FileSystem, context.FullPath, _line, index - _lineStart + 1, length);
        }
    }
}
