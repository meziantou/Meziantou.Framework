using Meziantou.Framework.Tds.QueryEngine;

namespace Meziantou.Framework.Tds.Tests;

/// <summary>
/// Unlike <see cref="TdsQueryEngineTests"/>, these tests drive the query engine directly instead of going
/// through SqlClient, so they do not need a real culture and run in every globalization mode.
/// </summary>
public sealed class TdsProjectionTypeCacheTests
{
    [Fact]
    public void ProjectionTypeCache_WhenTheShapeLimitIsReached_KeepsServingKnownShapesAndRejectsNewOnes()
    {
        // The shared cache is process-wide and its emitted types can never be reclaimed, so the limit is
        // exercised on a private cache instead of by sending 1024 distinct aliases through a server.
        var cache = new TdsProjectionTypeCache(maxCachedTypes: 2);
        var first = cache.GetProjectionType([new TdsProjectionMember("First", typeof(int))]);
        var second = cache.GetProjectionType([new TdsProjectionMember("Second", typeof(int))]);

        Assert.NotSame(first, second);

        var exception = Assert.Throws<TdsQueryEngineException>(() => cache.GetProjectionType([new TdsProjectionMember("Third", typeof(int))]));
        Assert.Contains("2 distinct query projection shapes", exception.Message);

        // Carrier types draw from the same budget as projection types.
        _ = Assert.Throws<TdsQueryEngineException>(() => cache.GetCarrierType([new TdsProjectionMember("First", typeof(int))]));

        // A shape that was cached before the limit was reached keeps being served.
        Assert.Same(first, cache.GetProjectionType([new TdsProjectionMember("First", typeof(int))]));
        Assert.Same(second, cache.GetProjectionType([new TdsProjectionMember("Second", typeof(int))]));
    }
}
