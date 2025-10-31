using System.Diagnostics;
using System.Text.RegularExpressions;
using Meziantou.Framework.Globbing;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>
/// A configurable scanner that uses regular expressions to extract dependencies from files.
/// <example>
/// <code>
/// var scanner = new RegexScanner
/// {
///     FilePatterns = [Glob.Parse("**/*.custom", GlobOptions.IgnoreCase)],
///     DependencyType = DependencyType.DockerImage,
///     RegexPattern = @"image:\s*(?&lt;name&gt;[a-z/]+)(:(?&lt;version&gt;[0-9.]+))?"
/// };
/// var options = new ScannerOptions { Scanners = [scanner] };
/// var dependencies = await DependencyScanner.ScanDirectoryAsync("C:\\MyProject", options, cancellationToken);
/// </code>
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

    /// <summary>Gets or sets the regular expression pattern to match dependencies. The pattern must include named groups 'name' and optionally 'version'.</summary>
    public string? RegexPattern { get; set; }

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

    /// <summary>Gets or sets the glob patterns specifying which files to scan. If <see langword="null"/>, all files are scanned.</summary>
    public GlobCollection? FilePatterns { get; set; }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        if (RegexPattern is null)
            return;

        _frozen = true;

        using var sr = new StreamReader(context.Content);
        var text = await sr.ReadToEndAsync(context.CancellationToken).ConfigureAwait(false);

        foreach (Match match in Regex.Matches(text, RegexPattern, RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10)))
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
                    var nameLocation = TextLocation.FromIndex(context.FileSystem, context.FullPath, text, nameGroup.Index, nameGroup.Length);
                    var versionLocation = TextLocation.FromIndex(context.FileSystem, context.FullPath, text, versionGroup.Index, versionGroup.Length);
                    context.ReportDependency(this, name, version, DependencyType, nameLocation, versionLocation);
                }
                else
                {
                    var nameLocation = TextLocation.FromIndex(context.FileSystem, context.FullPath, text, nameGroup.Index, nameGroup.Length);
                    context.ReportDependency(this, name, version: null, DependencyType, nameLocation, versionLocation: null);
                }
            }
        }
    }

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        if (FilePatterns is not null)
            return FilePatterns.IsMatch(context.Directory, context.FileName);

        return true;
    }
}
