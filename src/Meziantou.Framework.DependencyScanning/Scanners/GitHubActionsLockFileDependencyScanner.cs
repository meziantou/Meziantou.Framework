using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.DependencyScanning.Locations;
using Meziantou.Framework.Yaml.Model;
using static Meziantou.Framework.DependencyScanning.Internals.YamlParserUtilities;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans the GitHub Actions lock file (<c>.github/workflows/actions.lock</c>) for the resolved actions used by the workflows, and the actions these actions use.</summary>
/// <remarks>
/// The entries of the lock file are not updatable, as they record the commit and the identity of the repository.
/// The <c>commit</c>, <c>ref</c>, <c>hostname</c>, <c>owner_id</c> and <c>repo_id</c> values of an entry are reported in
/// <see cref="Dependency.Metadata"/> under the <c>commit</c>, <c>ref</c>, <c>hostname</c>, <c>ownerId</c> and <c>repoId</c> keys.
/// </remarks>
public sealed class GitHubActionsLockFileDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.GitHubActions];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        // https://github.com/github/actions-lockfile
        return context.HasFileName("actions.lock", ignoreCase: false)
            && context.RelativeDirectory is ".github/workflows" or ".github\\workflows";
    }

    public override ValueTask ScanAsync(ScanFileContext context)
    {
        var yaml = LoadYamlFile(context);
        if (yaml is null)
            return ValueTask.CompletedTask;

        foreach (var document in yaml.Stream)
        {
            // The "workflows" section only lists the keys of the "dependencies" section each workflow depends on
            if (GetProperty(document.Contents, "dependencies", StringComparison.Ordinal) is not YamlMapping dependencies)
                continue;

            foreach (var dependency in dependencies)
            {
                if (GetScalarValue(dependency.Key) is not { } pin || !TryParsePin(pin, out var name, out var version))
                    continue;

                // Version 0.0.1 of the lock file has a "tag" or a "branch" instead of a "ref"
                var entry = dependency.Value;
                var gitRef = GetValue(entry, "ref") ?? GetValue(entry, "tag") ?? GetValue(entry, "branch");
                context.ReportDependency(
                    this,
                    name,
                    version,
                    DependencyType.GitHubActions,
                    nameLocation: new NonUpdatableLocation(context),
                    versionLocation: new NonUpdatableLocation(context),
                    tags: [],
                    metadata: [
                        KeyValuePair.Create<string, object?>("commit", GetValue(entry, "commit")),
                        KeyValuePair.Create<string, object?>("ref", gitRef),
                        KeyValuePair.Create<string, object?>("hostname", GetValue(entry, "hostname")),
                        KeyValuePair.Create<string, object?>("ownerId", GetId(entry, "owner_id")),
                        KeyValuePair.Create<string, object?>("repoId", GetId(entry, "repo_id")),
                    ]);
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Parses the key of an entry: <c>owner/repo@ref</c>, or <c>owner/repo@ref:sha1-digest</c> in version 0.0.1 of the lock file.</summary>
    private static bool TryParsePin(string pin, [NotNullWhen(true)] out string? name, [NotNullWhen(true)] out string? version)
    {
        name = null;
        version = null;

        var index = pin.IndexOf('@', StringComparison.Ordinal);
        if (index <= 0 || index == pin.Length - 1)
            return false;

        // A git ref cannot contain a colon
        var gitRef = pin.AsSpan(index + 1);
        var digestIndex = gitRef.IndexOf(':');
        if (digestIndex >= 0)
        {
            gitRef = gitRef[..digestIndex];
        }

        if (gitRef.IsEmpty)
            return false;

        name = pin[..index];
        version = gitRef.ToString();
        return true;
    }

    private static string? GetValue(YamlElement? entry, string propertyName)
    {
        var value = GetScalarValue(GetProperty(entry, propertyName, StringComparison.Ordinal));
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static long? GetId(YamlElement? entry, string propertyName)
    {
        return long.TryParse(GetValue(entry, propertyName), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
    }
}
