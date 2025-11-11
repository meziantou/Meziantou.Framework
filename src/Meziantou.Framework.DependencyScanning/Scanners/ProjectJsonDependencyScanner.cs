using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Meziantou.Framework.DependencyScanning.Scanners;

/// <summary>Scans legacy project.json files for NuGet package dependencies.</summary>
public sealed class ProjectJsonDependencyScanner : DependencyScanner
{
    protected internal override IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; } = [DependencyType.NuGet];

    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("project.json", ignoreCase: true);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        try
        {
            using var sr = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            using var jsonReader = new JsonTextReader(sr);
            var doc = await JToken.ReadFromAsync(jsonReader, context.CancellationToken).ConfigureAwait(false);
            foreach (var deps in doc.SelectTokens("$..dependencies").Concat(doc.SelectTokens("$.tools")).OfType<JObject>())
            {
                foreach (var dep in deps.Properties())
                {
                    JToken valueElement = dep;
                    var packageName = dep.Name;
                    string? version;
                    if (dep.Value.Type == JTokenType.String)
                    {
                        version = dep.Value.Value<string>();
                        valueElement = dep.Value;
                    }
                    else if (dep.Value.Type == JTokenType.Object)
                    {
                        var token = dep.Value.SelectToken("$.version");
                        if (token is null)
                            continue;

                        version = token.Value<string>();
                        valueElement = token;
                    }
                    else
                    {
                        continue;
                    }

                    if (version is not null)
                    {
                        context.ReportDependency(this, packageName, version, DependencyType.NuGet, nameLocation: new NonUpdatableLocation(context), versionLocation: new JsonLocation(context, valueElement));
                    }
                }
            }
        }
        catch (JsonException)
        {
        }
    }
}
