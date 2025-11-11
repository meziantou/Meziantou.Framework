using Meziantou.Framework.DependencyScanning.Internals;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

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
            using var sr = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            using var jsonReader = new JsonTextReader(sr);
            var doc = await JToken.ReadFromAsync(jsonReader, context.CancellationToken).ConfigureAwait(false);

            HandleExtendableElement(context, doc);
            if (doc is JObject root)
            {
                var packageRules = root.Property("packageRules", StringComparison.Ordinal)?.Value as JArray;
                if (packageRules is not null)
                {
                    foreach (var rule in packageRules)
                    {
                        HandleExtendableElement(context, rule);
                    }
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    private void HandleExtendableElement(ScanFileContext context, JToken token)
    {
        if (token is JObject obj && obj.TryGetValue("extends", StringComparison.Ordinal, out var extends))
        {
            if (extends is JArray array)
            {
                foreach (var item in array)
                {
                    var value = item.Value<string>();
                    if (!string.IsNullOrEmpty(value))
                    {
                        // parse name#ref
                        var index = value.IndexOf('#', StringComparison.Ordinal);
                        if (index >= 0)
                        {
                            var name = value[..index];
                            var version = value[(index + 1)..];
                            context.ReportDependency(
                                this,
                                name: name,
                                version: version,
                                type: DependencyType.RenovateConfiguration,
                                nameLocation: new JsonLocation(context, item, 0, name.Length),
                                versionLocation: new JsonLocation(context, item, index + 1, version.Length));
                        }
                        else
                        {
                            context.ReportDependency(
                                this,
                                name: value,
                                version: null,
                                type: DependencyType.RenovateConfiguration,
                                nameLocation: new JsonLocation(context, item),
                                versionLocation: null);
                        }
                    }
                }
            }
        }
    }
}
