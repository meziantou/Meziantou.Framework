using BenchmarkDotNet.Running;
using Meziantou.Framework.Imaging.Benchmarks;

// "service": closed-loop load test with latency percentiles; "profile": one workload in a loop for a profiler;
// "webp-quality", "avif-quality" and "heic-quality": size, quality and encoding time across settings. Anything else goes to BenchmarkDotNet.
switch (args.FirstOrDefault())
{
    case "service":
        ServiceLoad.Run(args[1..]);
        break;

    case "profile":
        ProfileLoop.Run(args[1..]);
        break;

    case "webp-quality":
        WebPQualityReport.Run(args[1..]);
        break;

    case "avif-quality":
        AvifQualityReport.Run(args[1..]);
        break;

    case "heic-quality":
        HeicQualityReport.Run(args[1..]);
        break;

    default:
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        break;
}
