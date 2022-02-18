using Meziantou.Framework.DependencyScanning.Internals;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Meziantou.Framework.DependencyScanning.Scanners;

public sealed class ProjectJsonDependencyScanner : DependencyScanner
{
    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.FileName.Equals("project.json", StringComparison.OrdinalIgnoreCase);
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
                    }
                    else if (dep.Value.Type == JTokenType.Object)
                    {
                        var token = dep.Value.SelectToken("$.version");
                        if (token == null)
                            continue;

                        version = token.Value<string>();
                        valueElement = token;
                    }
                    else
                    {
                        continue;
                    }

                    if (version != null)
                    {
                        await context.ReportDependency(new Dependency(packageName, version, DependencyType.NuGet, new JsonLocation(context.FullPath, LineInfo.FromJToken(dep), valueElement.Path))).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (JsonException)
        {
        }
    }
}
