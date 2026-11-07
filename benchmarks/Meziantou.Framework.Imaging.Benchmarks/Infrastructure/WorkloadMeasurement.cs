using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Running;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Benchmarks.Infrastructure;

/// <summary>
/// Untimed measurements of a benchmark case, run once in the host process when the report is built: the encoded output
/// size and the peak of the library-accounted live bytes (pixel storage, codec and resampling working buffers rented by
/// the allocation scopes). They are deterministic, so one run is enough; they never run inside a
/// timed region.
/// </summary>
/// <remarks>
/// The peak is the sum of the peaks of every allocation scope the operation created (an image, a reader and the images it
/// returns, a writer...), recorded with the internal <see cref="AllocationTracking"/> hook: an upper bound of the true
/// simultaneous peak. Managed allocations outside the scopes (small objects, metadata) are reported by the
/// <c>Allocated</c> column of the memory diagnoser instead.
/// </remarks>
internal static class WorkloadMeasurement
{
    private static readonly ConcurrentDictionary<BenchmarkCase, Result> Cache = new();

    public static Result Get(BenchmarkCase benchmarkCase) => Cache.GetOrAdd(benchmarkCase, Measure);

    private static Result Measure(BenchmarkCase benchmarkCase)
    {
        var descriptor = benchmarkCase.Descriptor;
        var instance = Activator.CreateInstance(descriptor.Type);
        foreach (var parameter in benchmarkCase.Parameters.Items)
        {
            if (parameter.IsArgument)
                return default;

            var member = descriptor.Type.GetMember(parameter.Name, BindingFlags.Public | BindingFlags.Instance).Single();
            if (member is PropertyInfo property)
            {
                property.SetValue(instance, parameter.Value);
            }
            else
            {
                ((FieldInfo)member).SetValue(instance, parameter.Value);
            }
        }

        descriptor.GlobalSetupMethod?.Invoke(instance, null);
        try
        {
            // Warm-up run: lazy inputs and static tables are created outside the measurement
            descriptor.WorkloadMethod.Invoke(instance, null);
            using var tracking = new AllocationTracking();
            var value = descriptor.WorkloadMethod.Invoke(instance, null);
            var peak = tracking.Scopes.Sum(scope => scope.GetDiagnostics().PeakLiveBytes);
            var attribute = descriptor.WorkloadMethod.GetCustomAttribute<WorkloadAttribute>();
            long? output = attribute?.ReturnsOutputBytes == true ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : null;
            return new Result(output, peak);
        }
        finally
        {
            descriptor.GlobalCleanupMethod?.Invoke(instance, null);
            (instance as IDisposable)?.Dispose();
        }
    }

    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct Result(long? OutputBytes, long PeakLiveBytes);
}
