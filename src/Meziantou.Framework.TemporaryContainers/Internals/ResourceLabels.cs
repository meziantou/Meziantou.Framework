namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>The labels the library stamps on every container and volume it creates, so a later run can tell its own leftovers from the resources it must not touch.</summary>
internal static class ResourceLabels
{
    public const string Prefix = "meziantou.tc.";

    /// <summary>Marks a resource as created by this library. Every cleanup starts from this label.</summary>
    public const string Managed = Prefix + "managed";

    /// <summary>The session that created the resource. A session is one process run.</summary>
    public const string SessionId = Prefix + "session";

    /// <summary>The machine that created the resource. A daemon can be shared, so the owner process only means something on the machine that recorded it.</summary>
    public const string Host = Prefix + "host";

    /// <summary>The id of the process that created the resource.</summary>
    public const string ProcessId = Prefix + "pid";

    /// <summary>The start time of the process that created the resource, in milliseconds since the Unix epoch.</summary>
    public const string ProcessStartTime = Prefix + "pid-start";

    /// <summary>The creation time of the resource, in milliseconds since the Unix epoch.</summary>
    public const string CreatedAt = Prefix + "created";

    /// <summary>The <see cref="ContainerDefinition.ReuseId"/> the resource was created with. Such a resource outlives the process that created it on purpose.</summary>
    public const string ReuseId = Prefix + "reuse";

    /// <summary>Builds the labels a new resource is created with: the ones the caller asked for, plus the identity of the run that creates it.</summary>
    /// <param name="userLabels">The labels of the definition.</param>
    /// <param name="reuseId">The reuse identifier, when the resource is meant to be reused across runs.</param>
    /// <param name="sessionOwned">Whether the resource belongs to the run that creates it. A resource that does not is never removed by the reaper of that session.</param>
    /// <param name="identity">The run to record. Defaults to the current one.</param>
    /// <returns>The labels to apply.</returns>
    public static Dictionary<string, string> Build(ContainerLabelCollection userLabels, string? reuseId, bool sessionOwned = true, SessionIdentity? identity = null)
    {
        identity ??= SessionIdentity.Current;

        // The library labels are added last: a definition cannot overwrite them, which would make its resources
        // invisible to the cleanup or, worse, make somebody else's resources look like ours.
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in userLabels)
            labels[name] = value;

        labels[Managed] = "1";
        labels[Host] = identity.Host;
        labels[ProcessId] = identity.ProcessId;
        labels[ProcessStartTime] = identity.ProcessStartTime;
        labels[CreatedAt] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

        if (reuseId is not null)
        {
            // A reused resource outlives the run that created it on purpose, so it is not tied to that run's session.
            labels[ReuseId] = reuseId;
        }
        else if (sessionOwned)
        {
            labels[SessionId] = identity.SessionId;
        }

        return labels;
    }
}
