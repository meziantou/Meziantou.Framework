using System.Runtime.CompilerServices;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.DependencyScanning.Tool.Tests;

public sealed class MinimumAgeTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 3, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecentVersionsAreSkipped_AndAnOlderCompatibleVersionIsSelected()
    {
        var versions = new[]
        {
            new PackageVersion("2.0.0", Now.UtcDateTime.AddDays(-2)),  // too recent
            new PackageVersion("1.5.0", Now.UtcDateTime.AddDays(-30)), // old enough
        };

        var (result, _, location) = await RunUpdateAsync(versions, minimumAge: 7);

        Assert.Equal("1.5.0", result);
        Assert.Equal("1.5.0", location.UpdatedValue);
    }

    [Fact]
    public async Task FilteringDisabled_SelectsLatestVersion()
    {
        var versions = new[]
        {
            new PackageVersion("2.0.0", Now.UtcDateTime.AddDays(-2)),
            new PackageVersion("1.5.0", Now.UtcDateTime.AddDays(-30)),
        };

        var (result, _, location) = await RunUpdateAsync(versions, minimumAge: 0);

        Assert.Equal("2.0.0", result);
        Assert.Equal("2.0.0", location.UpdatedValue);
    }

    [Fact]
    public async Task AllVersionsTooRecent_NoUpdate()
    {
        var versions = new[]
        {
            new PackageVersion("2.0.0", Now.UtcDateTime.AddDays(-1)),
            new PackageVersion("1.5.0", Now.UtcDateTime.AddDays(-3)),
        };

        var (result, _, location) = await RunUpdateAsync(versions, minimumAge: 7);

        Assert.Null(result);
        Assert.Null(location.UpdatedValue);
    }

    [Fact]
    public async Task VersionsWithoutPublishDate_AreNotFiltered()
    {
        // Some sources (e.g. Docker) don't expose a publication date; those versions must remain eligible.
        var versions = new[]
        {
            new PackageVersion("2.0.0", PublishedDate: null),
        };

        var (result, _, location) = await RunUpdateAsync(versions, minimumAge: 7);

        Assert.Equal("2.0.0", result);
        Assert.Equal("2.0.0", location.UpdatedValue);
    }

    [Fact]
    public async Task GetUpdatedVersionDoesNotUpdateDependency()
    {
        var location = new RecordingLocation();
        var dependency = new Dependency("Sample.Package", "1.0.0", DependencyType.NuGet, nameLocation: null, versionLocation: location);
        var updater = new FakePackageUpdater([new PackageVersion("2.0.0", PublishedDate: null)]);

        var result = await updater.GetUpdatedVersionAsync(dependency, XunitCancellationToken);

        Assert.Equal("2.0.0", result);
        Assert.Null(location.UpdatedValue);
    }

    private static async Task<(string? Result, Dependency Dependency, RecordingLocation Location)> RunUpdateAsync(PackageVersion[] versions, int minimumAge)
    {
        var location = new RecordingLocation();
        var dependency = new Dependency("Sample.Package", "1.0.0", DependencyType.NuGet, nameLocation: null, versionLocation: location);

        var updater = new FakePackageUpdater(versions)
        {
            MinimumAge = minimumAge,
            TimeProvider = new FakeTimeProvider(Now),
        };

        var result = await updater.UpdateAsync(dependency, XunitCancellationToken);
        return (result, dependency, location);
    }

    private sealed class FakePackageUpdater(PackageVersion[] versions) : PackageUpdater
    {
        public override VersioningStrategy VersioningStrategy { get; set; } = NuGetVersioningStrategy.Instance;

        protected override bool IsSupported(Dependency dependency) => true;

        protected override async IAsyncEnumerable<PackageVersion> GetVersionsAsync(Dependency dependency, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var version in versions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return version;
            }

            await Task.CompletedTask;
        }

        public override Task UpdateLockFileAsync(FullPath rootDirectory, IEnumerable<Dependency> updatedDependencies, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingLocation : Location
    {
        public RecordingLocation()
            : base(fileSystem: null!, filePath: "memory")
        {
        }

        public string? UpdatedValue { get; private set; }

        public override bool IsUpdatable => true;

        protected override Task UpdateCoreAsync(string? oldValue, string newValue, CancellationToken cancellationToken)
        {
            UpdatedValue = newValue;
            return Task.CompletedTask;
        }
    }
}
