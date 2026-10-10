using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// Service-style throughput: <see cref="Requests"/> independent requests (decode a 1024x768 JPEG, resize it to fit 320x240,
/// encode a JPEG) processed by <see cref="Workers"/> concurrent callers, each image using the default single-worker
/// configuration. The reported time is per request (wall clock divided by <see cref="Requests"/>). Tail latencies are
/// measured by the <c>service</c> command.
/// </summary>
[Config(typeof(ConcurrencyConfig))]
public class ConcurrencyBenchmarks : IDisposable
{
    private const int Requests = 32;
    private readonly ThreadLocal<MemoryStream> _outputs = new(() => new MemoryStream(), trackAllValues: false);

    [Params(1, 2, 4)]
    public int Workers { get; set; } = 1;

    [Benchmark(OperationsPerInvoke = Requests)]
    [Workload("32 requests: JPEG 1024x768 -> fit 320x240 -> JPEG q85", MeasurePeakLive = false)]
    public void Throughput()
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = Workers };
        Parallel.For(0, Requests, options, _ => ImageWorkloads.JpegDecodeResizeEncode(BenchmarkInputs.ServiceJpeg, new Size(320, 240), _outputs.Value!));
    }

    public void Dispose()
    {
        _outputs.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The workload configuration plus the threading diagnoser (completed work items, lock contentions). BenchmarkDotNet
    /// 0.15.8 rejects that diagnoser on runtimes it does not know (".NET Core 3.0+ only" on .NET 11), which would abort the
    /// whole run, so it is added on .NET 10 only.
    /// </summary>
    internal sealed class ConcurrencyConfig : ManualConfig
    {
        public ConcurrencyConfig()
        {
            Add(new WorkloadConfig());
            if (Environment.Version.Major <= 10)
            {
                AddDiagnoser(ThreadingDiagnoser.Default);
            }
        }
    }
}
