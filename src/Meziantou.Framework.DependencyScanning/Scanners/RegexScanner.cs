using System.Diagnostics;
using System.Text.RegularExpressions;
using Meziantou.Framework.Globbing;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>
/// A configurable scanner that uses regular expressions to extract dependencies from files.
/// <example>
/// <code><![CDATA[
/// var scanner = new RegexScanner
/// {
///     FilePatterns = [Glob.Parse("**/*.custom", GlobOptions.IgnoreCase)],
///     DependencyType = DependencyType.DockerImage,
///     RegexPattern = @"image:\s*(?<name>[a-z/]+)(:(?<version>[0-9.]+))?"
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
    private string? _regexPattern;
    private Regex? _regex;
    private int _nameGroupNumber = -1;
    private int _versionGroupNumber = -1;

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
    public string? RegexPattern
    {
        get => _regexPattern;
        set
        {
            if (_frozen)
                throw new InvalidOperationException("The scanner is already used and cannot be modified.");

            _regexPattern = value;
            _regex = null;
            _nameGroupNumber = -1;
            _versionGroupNumber = -1;
        }
    }

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
        var regexPattern = RegexPattern;
        if (regexPattern is null)
            return;

        _frozen = true;
        var regex = _regex;
        if (regex is null)
        {
            regex = new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10));
            _regex = regex;
            _nameGroupNumber = regex.GroupNumberFromName(NameGroupName);
            if (_nameGroupNumber <= 0)
                throw new InvalidOperationException($"The regular expression must define the '{NameGroupName}' named group.");

            _versionGroupNumber = regex.GroupNumberFromName(VersionGroupName);
        }

        using var sr = new StreamReader(context.Content);
        var text = await sr.ReadToEndAsync(context.CancellationToken).ConfigureAwait(false);

        foreach (Match match in regex.Matches(text))
        {
            Debug.Assert(match.Success);

            var nameGroup = match.Groups[_nameGroupNumber];
            var name = nameGroup.Value;
            if (!string.IsNullOrEmpty(name))
            {
                if (_versionGroupNumber >= 0)
                {
                    var versionGroup = match.Groups[_versionGroupNumber];
                    if (versionGroup.Success)
                    {
                        var version = versionGroup.Value;
                        var nameLocation = TextLocation.FromIndex(context.FileSystem, context.FullPath, text, nameGroup.Index, nameGroup.Length);
                        var versionLocation = TextLocation.FromIndex(context.FileSystem, context.FullPath, text, versionGroup.Index, versionGroup.Length);
                        context.ReportDependency(this, name, version, DependencyType, nameLocation, versionLocation);
                        continue;
                    }
                }

                var location = TextLocation.FromIndex(context.FileSystem, context.FullPath, text, nameGroup.Index, nameGroup.Length);
                context.ReportDependency(this, name, version: null, DependencyType, location, versionLocation: null);
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
