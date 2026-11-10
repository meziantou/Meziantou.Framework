namespace Meziantou.Framework.Ntp;

/// <summary>
/// The exception thrown when the servers queried by
/// <see cref="NtpClient.MeasureAsync(IEnumerable{NtpClient}, NtpMeasurementOptions?, CancellationToken)"/>
/// answered, but no majority of them agrees on the time.
/// </summary>
public sealed class NtpClockSelectionException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="NtpClockSelectionException"/> class.</summary>
    public NtpClockSelectionException()
        : this("No majority of the NTP servers agrees on the time.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="NtpClockSelectionException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public NtpClockSelectionException(string message)
        : base(message)
    {
        Measurements = [];
        FailedServers = new Dictionary<NtpClient, Exception>();
    }

    /// <summary>Initializes a new instance of the <see cref="NtpClockSelectionException"/> class with a message and an inner exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public NtpClockSelectionException(string message, Exception innerException)
        : base(message, innerException)
    {
        Measurements = [];
        FailedServers = new Dictionary<NtpClient, Exception>();
    }

    internal NtpClockSelectionException(string message, IReadOnlyList<NtpServerMeasurement> measurements, IReadOnlyDictionary<NtpClient, Exception> failedServers)
        : base(message)
    {
        Measurements = measurements;
        FailedServers = failedServers;
    }

    /// <summary>Gets the measurements of the servers that answered.</summary>
    public IReadOnlyList<NtpServerMeasurement> Measurements { get; }

    /// <summary>Gets the clients whose server gave no valid reply, with the reason.</summary>
    public IReadOnlyDictionary<NtpClient, Exception> FailedServers { get; }
}
