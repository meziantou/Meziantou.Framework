using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans Renovate configuration files for extends references to shared configuration presets.</summary>
public sealed class RenovateExtendsDependencyScanner : DependencyScanner
{
    private static readonly string[] PotentialRenovateFiles =
    [
        "renovate.json",
        "renovate.json5",
        "renovaterc",
        "renovaterc.json",
        "renovaterc.json5",
    ];

    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.RenovateConfiguration];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return IsInExpectedDirectory(context) && MatchFileName(context);

        static bool MatchFileName(CandidateFileContext context)
        {
            foreach (var file in PotentialRenovateFiles)
            {
                if (context.HasFileName(file, ignoreCase: false))
                    return true;
            }

            return false;
        }

        static bool IsInExpectedDirectory(CandidateFileContext context)
        {
            return context.RelativeDirectory is "" or ".github" or ".gitlab";
        }
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        try
        {
            var doc = await JsonNodeDocument.ParseAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            if (doc.GetRootObject() is not JsonObject root)
                return;

            HandleExtendableElement(context, doc, root);
            if (JsonNodeDocument.TryGetArray(root, "packageRules", out var packageRules))
            {
                foreach (var rule in doc.GetArray(packageRules))
                {
                    HandleExtendableElement(context, doc, rule);
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    private void HandleExtendableElement(ScanFileContext context, JsonNodeDocument doc, JsonNode? token)
    {
        if (token is JsonObject obj && JsonNodeDocument.TryGetArray(obj, "extends", out var extends))
        {
            foreach (var item in doc.GetArray(extends))
            {
                if (item is null || !JsonNodeDocument.TryGetString(item, out var value))
                    continue;

                if (!string.IsNullOrEmpty(value))
                {
                    var itemPath = item.GetPath();

                    // parse name#ref
                    var hashIndex = value.IndexOf('#', StringComparison.Ordinal);
                    if (hashIndex >= 0)
                    {
                        var name = value[..hashIndex];
                        var version = value[(hashIndex + 1)..];
                        context.ReportDependency(
                            this,
                            name: name,
                            version: version,
                            type: DependencyType.RenovateConfiguration,
                            nameLocation: new JsonLocation(context, itemPath, 0, name.Length),
                            versionLocation: new JsonLocation(context, itemPath, hashIndex + 1, version.Length));
                    }
                    else
                    {
                        context.ReportDependency(
                            this,
                            name: value,
                            version: null,
                            type: DependencyType.RenovateConfiguration,
                            nameLocation: new JsonLocation(context, itemPath),
                            versionLocation: null);
                    }
                }
            }
        }
    }
}
