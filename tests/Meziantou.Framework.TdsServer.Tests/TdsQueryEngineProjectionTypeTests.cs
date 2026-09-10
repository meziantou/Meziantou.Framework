using System.Net;
using Meziantou.Framework.Tds.Handler;
using Meziantou.Framework.Tds.QueryEngine;

namespace Meziantou.Framework.Tds.Tests;

// The projection type cache lives in the process, not in a server, so these tests call the query delegate
// directly: they need thousands of distinct shapes, and a TDS round trip per shape would need both a real
// globalization mode -- SqlClient refuses to run without one -- and far more time than the emit path it covers.
public sealed class TdsQueryEngineProjectionTypeTests
{
    // Aliases come from client SQL and each distinct shape emits a type, so the factory retains only a bounded
    // number of them. Push well past that bound: emitted types used to be capped and never reclaimed, which let
    // a client shut every unseen shape out of the process for the rest of its life.
    //
    // "Well past" is what the reclamation test needs. Types are packed into collectible assemblies, and an
    // assembly is reclaimed only once every type in it is unreachable, so the tracked type's assembly also holds
    // the shapes emitted right after it. Exceeding the retention window by a single assembly's worth would leave
    // a handful of queries of slack; this leaves hundreds.
    private const int ProjectionShapeChurnCount = 1_600;

    [Fact]
    public async Task MoreProjectionShapesThanTheCacheRetains_KeepBeingServed()
    {
        var aliasPrefix = "Churn" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var handler = TdsQueryEngine.CreateQueryHandler(CreateQueryEngineOptions());

        for (var index = 0; index < ProjectionShapeChurnCount; index++)
        {
            var alias = $"{aliasPrefix}_{index}";
            Assert.Equal(alias, await GetColumnNameAsync(handler, $"SELECT Id AS {alias} FROM customers"));
        }

        // The first shapes left the cache long ago, so they have to be emitted again rather than rejected.
        var evictedAlias = $"{aliasPrefix}_0";
        Assert.Equal(evictedAlias, await GetColumnNameAsync(handler, $"SELECT Id AS {evictedAlias} FROM customers"));

        // The cache is process-wide: a handler created after the churn used to inherit an exhausted one.
        var laterHandler = TdsQueryEngine.CreateQueryHandler(CreateQueryEngineOptions());
        var freshAlias = $"{aliasPrefix}_fresh";
        Assert.Equal(freshAlias, await GetColumnNameAsync(laterHandler, $"SELECT Id AS {freshAlias} FROM customers"));
    }

    [Fact]
    public async Task ProjectionTypesLeavingTheCache_AreReclaimed()
    {
        var aliasPrefix = "Reclaim" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var queryEngineOptions = CreateQueryEngineOptions();
        var materializeAsync = queryEngineOptions.MaterializeAsync;
        WeakReference? projectionAssembly = null;
        queryEngineOptions.MaterializeAsync = (query, cancellationToken) =>
        {
            // Track the assembly and never the type itself: a live type keeps its assembly alive on its own.
            projectionAssembly ??= new WeakReference(query.ElementType.Assembly);
            return materializeAsync(query, cancellationToken);
        };

        var handler = TdsQueryEngine.CreateQueryHandler(queryEngineOptions);
        var trackedAlias = $"{aliasPrefix}_tracked";
        Assert.Equal(trackedAlias, await GetColumnNameAsync(handler, $"SELECT Id AS {trackedAlias} FROM customers"));
        Assert.NotNull(projectionAssembly);
        Assert.True(projectionAssembly.IsAlive, "The projection type was not emitted into a dynamic assembly");

        for (var index = 0; index < ProjectionShapeChurnCount; index++)
        {
            var alias = $"{aliasPrefix}_{index}";
            Assert.Equal(alias, await GetColumnNameAsync(handler, $"SELECT Id AS {alias} FROM customers"));
        }

        // Unloading is not synchronous: it completes on a later collection, once the runtime has finished
        // walking everything that referenced the assembly. Tests also run in parallel, and a query running in
        // another one can still hold a type emitted into the same assembly, so wait between the sweeps instead
        // of failing on the first one.
        for (var index = 0; index < 60 && projectionAssembly.IsAlive; index++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (projectionAssembly.IsAlive)
            {
                await Task.Delay(100);
            }
        }

        Assert.False(projectionAssembly.IsAlive, "The projection types dropped from the cache were not reclaimed");
    }

    private static async Task<string> GetColumnNameAsync(TdsQueryDelegate handler, string commandText)
    {
        var result = await handler(
            new TdsQueryContext
            {
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 1433),
                RequestType = TdsQueryRequestType.SqlBatch,
                CommandText = commandText,
                HasCompleteParameters = true,
            },
            CancellationToken.None);

        Assert.Null(result.Error);
        var resultSet = Assert.Single(result.ResultSets);
        return Assert.Single(resultSet.Columns).Name;
    }

    private static TdsQueryEngineOptions CreateQueryEngineOptions()
    {
        var options = new TdsQueryEngineOptions();
        options.AddQueryRoot("customers", context => new[] { new Customer(1, "Alice") }.AsQueryable());
        return options;
    }

    private sealed record Customer(int Id, string Name);
}
