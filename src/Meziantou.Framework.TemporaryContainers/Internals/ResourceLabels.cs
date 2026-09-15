namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>The labels the library stamps on every container, volume and image it creates, so a later run can tell its own leftovers from the resources it must not touch.</summary>
internal static class ResourceLabels
{
    public const string Prefix = "meziantou.tc.";

    /// <summary>Marks a resource as created by this library. Every cleanup starts from this label.</summary>
    public const string Managed = Prefix + "managed";

    /// <summary>The session that created the resource. A session is one process run.</summary>
    public const string SessionId = Prefix + "session";

    /// <summary>The machine that created the resource. A daemon can be shared, so the owner process only means something on the machine that recorded it.</summary>
    public const string Host = Prefix + "host";

    /// <summary>What tells apart the machines, or the process id spaces, that share a host name. See <see cref="SessionIdentity.MachineId"/>.</summary>
    public const string Machine = Prefix + "machine";

    /// <summary>The id of the process that created the resource.</summary>
    public const string ProcessId = Prefix + "pid";

    /// <summary>The start time of the process that created the resource, in milliseconds since the Unix epoch.</summary>
    public const string ProcessStartTime = Prefix + "pid-start";

    /// <summary>The start time of the process that created the resource, in clock ticks since boot, on Linux.</summary>
    public const string ProcessStartTicks = Prefix + "pid-ticks";

    /// <summary>The creation time of the resource, in milliseconds since the Unix epoch.</summary>
    public const string CreatedAt = Prefix + "created";

    /// <summary>The <see cref="ContainerDefinition.ReuseId"/> the resource was created with. Such a resource outlives the process that created it on purpose.</summary>
    public const string ReuseId = Prefix + "reuse";

    /// <summary>A hash of the configuration a reused container was created with, so a definition that changed is not silently served the container of the old one.</summary>
    public const string ConfigurationHash = Prefix + "config";

    /// <summary>The <see cref="TemporaryVolume"/> instance that created a volume. A runtime that answers the creation of an existing volume with a success cannot say who created it otherwise.</summary>
    public const string Instance = Prefix + "instance";

    /// <summary>Builds the labels a new resource is created with: the ones the caller asked for, plus the identity of the run that creates it.</summary>
    /// <param name="userLabels">The labels of the definition.</param>
    /// <param name="reuseId">The reuse identifier, when the resource is meant to be reused across runs.</param>
    /// <param name="sessionOwned">Whether the resource belongs to the run that creates it. A resource that does not is never removed by the reaper of that session.</param>
    /// <param name="identity">The run to record. Defaults to the current one.</param>
    /// <returns>The labels to apply.</returns>
    public static Dictionary<string, string> Build(IEnumerable<KeyValuePair<string, string>> userLabels, string? reuseId, bool sessionOwned = true, SessionIdentity? identity = null)
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
        SetOrRemove(labels, Machine, identity.MachineId);
        SetOrRemove(labels, ProcessStartTicks, identity.ProcessStartTicks);

        if (reuseId is not null)
        {
            // A reused resource outlives the run that created it on purpose, so it is not tied to that run's session.
            labels[ReuseId] = reuseId;
        }
        else if (sessionOwned)
        {
            labels[SessionId] = identity.SessionId;
        }
        else
        {
            labels.Remove(SessionId);
        }

        return labels;
    }

    /// <summary>Builds the labels of a new container.</summary>
    public static Dictionary<string, string> Build(ContainerDefinition definition)
    {
        var labels = Build(definition.Labels, definition.ReuseId, definition.SessionOwned, definition.Identity);
        if (definition.ReuseId is not null)
            labels[ConfigurationHash] = definition.ConfigurationHash ?? ContainerConfigurationHash.Compute(definition);

        return labels;
    }

    /// <summary>Builds the labels of an image built for a container. It belongs to whatever the container belongs to: the session, or nobody when the container is reused.</summary>
    public static Dictionary<string, string> BuildForImage(ContainerDefinition definition)
        => Build([], definition.ReuseId, definition.SessionOwned, definition.Identity);

    private static void SetOrRemove(Dictionary<string, string> labels, string name, string value)
    {
        if (value.Length > 0)
            labels[name] = value;
        else
            labels.Remove(name);
    }
}
