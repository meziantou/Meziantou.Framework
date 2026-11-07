namespace Meziantou.Framework.Imaging.Benchmarks.Infrastructure;

/// <summary>Describes a benchmark workload for the report columns (<see cref="WorkloadColumns"/>).</summary>
/// <param name="description">The input and settings: dimensions, frame count, pixel type, codec settings.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
internal sealed class WorkloadAttribute(string description) : Attribute
{
    public string Description { get; } = description;

    /// <summary>Gets the number of frames processed per operation (allocations per frame divide by it).</summary>
    public int Frames { get; init; } = 1;

    /// <summary>
    /// Gets a value indicating whether the peak live bytes are meaningful for one operation (false for operations made of many
    /// concurrent requests, whose memory is reported by the <c>service</c> command instead).
    /// </summary>
    public bool MeasurePeakLive { get; init; } = true;

    /// <summary>Gets a value indicating whether the benchmark method returns the encoded output size in bytes.</summary>
    public bool ReturnsOutputBytes { get; init; }
}
