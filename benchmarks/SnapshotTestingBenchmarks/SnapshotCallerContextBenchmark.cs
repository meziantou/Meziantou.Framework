using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Meziantou.Framework.SnapshotTesting;

namespace SnapshotTestingBenchmarks;

/// <summary>
/// Measures the cost of resolving the caller context, which every snapshot assertion pays before
/// anything is serialized or compared.
/// </summary>
[MemoryDiagnoser]
public class SnapshotCallerContextBenchmark
{
    private string _sourceFilePath = null!;

    /// <summary>Number of frames between the assertion method and the benchmark entry point.</summary>
    [Params(1, 20)]
    public int StackDepth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SnapshotTestingBenchmarks", "CallerContext");
        Directory.CreateDirectory(directory);
        _sourceFilePath = Path.Combine(directory, "Source.cs");
        File.WriteAllText(_sourceFilePath, "");
    }

    [Benchmark]
    public string Create() => Recurse(StackDepth);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private string Recurse(int depth)
    {
        if (depth > 0)
            return Recurse(depth - 1);

        return CreateCallerContext();
    }

    [SnapshotAssertion]
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private string CreateCallerContext()
    {
        return SnapshotCallerContext.Create(_sourceFilePath, lineNumber: 1, memberName: nameof(CreateCallerContext)).MethodName;
    }
}
