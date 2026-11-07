using System.Diagnostics;
using Meziantou.Framework.Geo.Maps.Tool.Sources;

namespace Meziantou.Framework.Geo.Maps.Tool;

/// <summary>The sources, loaded once, and the regions they are cut to.</summary>
internal sealed record SourceSet(IReadOnlyList<Region> Regions, CountryLevelsSource CountryLevels, NaturalEarthSource NaturalEarth, BaseLayersSource BaseLayers, OsmBoundariesBackfill Backfill)
{
    public static async Task<SourceSet> LoadAsync(Downloader downloader, TextWriter output, CancellationToken cancellationToken)
    {
        if (downloader.NaturalEarthCommit is null || downloader.CldrVersion is null)
            throw new InvalidOperationException("The pins have no commit of Natural Earth or no release of CLDR.");

        var watch = Stopwatch.StartNew();
        await output.WriteLineAsync("Loading the sources".AsMemory(), cancellationToken).ConfigureAwait(false);
        var countryLevels = await CountryLevelsSource.LoadAsync(downloader).ConfigureAwait(false);
        var regions = await Tool.Regions.LoadAsync(downloader, downloader.CldrVersion, countryLevels).ConfigureAwait(false);
        var naturalEarth = await NaturalEarthSource.LoadAsync(downloader, downloader.NaturalEarthCommit).ConfigureAwait(false);
        var baseLayers = await BaseLayersSource.LoadAsync(downloader, downloader.NaturalEarthCommit).ConfigureAwait(false);
        var backfill = OsmBoundariesBackfill.Load();
        await output.WriteLineAsync($"Loaded the sources in {watch.Elapsed.TotalSeconds:F1}s".AsMemory(), cancellationToken).ConfigureAwait(false);
        return new SourceSet(regions, countryLevels, naturalEarth, baseLayers, backfill);
    }
}
