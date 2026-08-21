using BenchmarkDotNet.Attributes;
using Meziantou.Framework;
using Meziantou.Framework.SnapshotTesting;

namespace SnapshotTestingBenchmarks;

/// <summary>
/// Measures an end-to-end <see cref="Snapshot.Validate(object?, SnapshotType?, SnapshotSettings?, string?, int, string?)"/>
/// call whose snapshot already matches. This is the path every passing snapshot test takes.
/// </summary>
[MemoryDiagnoser]
public class SnapshotValidateBenchmark
{
    private readonly object _value = new { FirstName = "John", LastName = "Doe", Age = 42 };
    private SnapshotSettings _settings = null!;
    private string _sourceFilePath = null!;

    /// <summary>Number of unrelated snapshots sitting in the same <c>__snapshots__</c> folder.</summary>
    [Params(0, 100)]
    public int SiblingSnapshotCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SnapshotTestingBenchmarks", $"Validate{SiblingSnapshotCount}");
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        Directory.CreateDirectory(directory);
        _sourceFilePath = Path.Combine(directory, "Source.cs");
        File.WriteAllText(_sourceFilePath, "");

        var snapshotDirectory = Path.Combine(directory, "__snapshots__");
        Directory.CreateDirectory(snapshotDirectory);
        for (var i = 0; i < SiblingSnapshotCount; i++)
        {
            File.WriteAllText(Path.Combine(snapshotDirectory, $"Sibling{i}.verified.txt"), "sibling");
        }

        var snapshotPath = FullPath.FromPath(Path.Combine(snapshotDirectory, "Benchmark.verified.txt"));
        _settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotPathStrategy = _ => snapshotPath,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
        };

        // Create the verified snapshot so the benchmark measures the matching path.
        Validate();
        _settings.SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow;
    }

    [Benchmark]
    public void Validate() => Snapshot.Validate(_value, type: null, _settings, _sourceFilePath, callerLineNumber: 1, callerMemberName: nameof(Validate));
}
