using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.DependencyScanning.Locations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Meziantou.Framework.DependencyScanning.Scanners;

public sealed class NpmPackageJsonDependencyScanner : DependencyScanner
{
    protected override bool ShouldScanFileCore(CandidateFileContext context)
    {
        return context.HasFileName("package.json", ignoreCase: false);
    }

    public override async ValueTask ScanAsync(ScanFileContext context)
    {
        try
        {
            using var sr = await StreamUtilities.CreateReaderAsync(context.Content, context.CancellationToken).ConfigureAwait(false);
            using var jsonReader = new JsonTextReader(sr);
            var doc = await JToken.ReadFromAsync(jsonReader, context.CancellationToken).ConfigureAwait(false);

            // https://docs.npmjs.com/files/package.json#dependencies
            foreach (var deps in doc.SelectTokens("$.dependencies").OfType<JObject>())
            {
                await ScanDependenciesAsync(context, deps).ConfigureAwait(false);
            }

            // https://docs.npmjs.com/files/package.json#devdependencies
            foreach (var deps in doc.SelectTokens("$.devDependencies").OfType<JObject>())
            {
                await ScanDependenciesAsync(context, deps).ConfigureAwait(false);
            }

            // https://docs.npmjs.com/files/package.json#peerdependencies
            foreach (var deps in doc.SelectTokens("$.peerDependencies").OfType<JObject>())
            {
                await ScanDependenciesAsync(context, deps).ConfigureAwait(false);
            }

            // https://docs.npmjs.com/files/package.json#optionaldependencies
            foreach (var deps in doc.SelectTokens("$.optionaldependencies").OfType<JObject>())
            {
                await ScanDependenciesAsync(context, deps).ConfigureAwait(false);
            }
        }
        catch (JsonException)
        {
        }
    }

    private static ValueTask ScanDependenciesAsync(ScanFileContext context, JObject deps)
    {
        foreach (var dep in deps.Properties())
        {
            if (dep.Value is null)
                continue;

            var packageName = dep.Name;
            string? version = null;
            if (dep.Value.Type == JTokenType.String)
            {
                if (dep.Value != null)
                {
                    version = dep.Value.Value<string>();
                }
            }
            else if (dep.Value.Type == JTokenType.Object)
            {
                if (dep.Value != null)
                {
                    var token = dep.Value.SelectToken("$.version");
                    if (token != null)
                    {
                        version = token.Value<string>();
                    }
                }
            }
            else
            {
                continue;
            }

            if (version is null)
                continue;

            if (dep.Value != null)
            {
                var dependency = new Dependency(packageName, version, DependencyType.Npm,
                    nameLocation: new NonUpdatableLocation(context),
                    versionLocation: new JsonLocation(context, dep.Value));
                context.ReportDependency(dependency);
            }
        }

        return ValueTask.CompletedTask;
    }
}
