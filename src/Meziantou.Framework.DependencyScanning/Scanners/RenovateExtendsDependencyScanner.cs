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

            HandleExtendableElement(context, doc, "$", doc.Root);
            if (doc.Root is JsonObject root)
            {
                var packageRulesPath = JsonNodeDocument.AppendPropertyPath("$", "packageRules");
                var packageRules = root["packageRules"] as JsonArray;
                if (packageRules is not null)
                {
                    for (var index = 0; index < packageRules.Count; index++)
                    {
                        HandleExtendableElement(context, doc, JsonNodeDocument.AppendArrayIndexPath(packageRulesPath, index), packageRules[index]);
                    }
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    private void HandleExtendableElement(ScanFileContext context, JsonNodeDocument doc, string path, JsonNode? token)
    {
        if (token is JsonObject obj && obj.TryGetPropertyValue("extends", out var extends))
        {
            if (extends is JsonArray array)
            {
                var extendsPath = JsonNodeDocument.AppendPropertyPath(path, "extends");
                for (var index = 0; index < array.Count; index++)
                {
                    var item = array[index];
                    var itemPath = JsonNodeDocument.AppendArrayIndexPath(extendsPath, index);
                    if (item is not JsonValue itemValue || !itemValue.TryGetValue<string>(out var value))
                        continue;

                    if (!string.IsNullOrEmpty(value))
                    {
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
                                nameLocation: new JsonLocation(context, itemPath, doc.GetLineInfo(itemPath), 0, name.Length),
                                versionLocation: new JsonLocation(context, itemPath, doc.GetLineInfo(itemPath), hashIndex + 1, version.Length));
                        }
                        else
                        {
                            context.ReportDependency(
                                this,
                                name: value,
                                version: null,
                                type: DependencyType.RenovateConfiguration,
                                nameLocation: new JsonLocation(context, itemPath, doc.GetLineInfo(itemPath)),
                                versionLocation: null);
                        }
                    }
                }
            }
        }
    }
}
