namespace Meziantou.Framework.TemporaryContainers.Internals;

internal static class DockerCreateArgumentBuilder
{
    /// <param name="definition">The container definition.</param>
    /// <param name="imageRef">The image to create the container from.</param>
    /// <param name="pullPolicyValue">The value of <c>--pull</c>, or <see langword="null"/> to leave it out.</param>
    /// <param name="quotedMountFieldsSupported">Whether the runtime parses the <c>--mount</c> descriptor as a CSV record, which is the only way to express a value containing a comma. Docker does; podman and wslc split on the comma instead.</param>
    /// <param name="hostIpSupported">Whether a port can be published on a specific host address, which Windows containers and wslc cannot.</param>
    /// <param name="environmentFile">The file holding the environment variables, or <see langword="null"/> to pass them all on the command line.</param>
    public static List<string> Build(ContainerDefinition definition, string imageRef, string? pullPolicyValue, bool quotedMountFieldsSupported, bool hostIpSupported = true, EnvironmentFile? environmentFile = null)
    {
        var args = new List<string> { "create" };

        // A reused container gets a deterministic name so the daemon itself rejects a second creation: two processes
        // that start at the same time would otherwise both find nothing and both create one.
        AddOption(args, "--name", definition.Name ?? (definition.ReuseId is { } reuseId ? ResourceNaming.GetReuseName(reuseId) : null));
        AddOption(args, "--hostname", definition.Hostname);
        AddOption(args, "--user", definition.User);
        AddOption(args, "--workdir", definition.WorkingDirectory);
        AddOption(args, "--pull", pullPolicyValue);
        AddOption(args, "--platform", definition.Platform);

        if (definition.Resources.ReadOnlyRootFilesystem)
            args.Add("--read-only");

        if (definition.Resources.MemoryLimit is { } memory)
            AddOption(args, "--memory", memory.ToString(CultureInfo.InvariantCulture) + "b");

        if (definition.Resources.CpuLimit is { } cpu)
            AddOption(args, "--cpus", cpu.ToString(CultureInfo.InvariantCulture));

        if (definition.Resources.SharedMemorySize is { } sharedMemorySize)
            AddOption(args, "--shm-size", sharedMemorySize.ToString(CultureInfo.InvariantCulture) + "b");

        foreach (var ulimit in definition.Resources.Ulimits)
            AddOption(args, "--ulimit", ulimit.Format());

        if (definition.Privileged)
            args.Add("--privileged");

        foreach (var capability in definition.CapAdd)
            AddOption(args, "--cap-add", capability);

        foreach (var capability in definition.CapDrop)
            AddOption(args, "--cap-drop", capability);

        foreach (var host in definition.ExtraHosts)
            AddOption(args, "--add-host", host.Format());

        AddHealthcheck(args, definition.Healthcheck);

        if (definition.StopTimeout is { } stopTimeout)
            AddOption(args, "--stop-timeout", ((long)stopTimeout.TotalSeconds).ToString(CultureInfo.InvariantCulture));

        AddOption(args, "--stop-signal", definition.StopSignal);

        // The other networks are connected once the container exists: only recent versions of docker take several
        // networks at creation.
        if (definition.Network.GetNetworkPlan().Primary is { } network)
        {
            AddOption(args, "--network", network.Name);
            foreach (var alias in network.Aliases)
                AddOption(args, "--network-alias", alias);
        }

        foreach (var (name, value) in ResourceLabels.Build(definition))
        {
            args.Add("--label");
            args.Add($"{name}={value}");
        }

        EnvironmentFile.AddArguments(args, definition.Environment, environmentFile);

        foreach (var port in definition.Ports)
        {
            args.Add("-p");
            args.Add(FormatPort(port, hostIpSupported));
        }

        foreach (var port in definition.ExposedPorts)
            AddOption(args, "--expose", string.Create(CultureInfo.InvariantCulture, $"{port.Port}/{ContainerPort.GetProtocolName(port.Protocol)}"));

        foreach (var mount in definition.Mounts)
            AppendMount(args, mount, quotedMountFieldsSupported);

        var entrypoint = new List<string>(definition.Entrypoint);
        if (entrypoint.Count > 0)
            AddOption(args, "--entrypoint", entrypoint[0]);

        args.AddRange(definition.AdditionalCreateArguments);
        args.Add(imageRef);

        for (var i = 1; i < entrypoint.Count; i++)
            args.Add(entrypoint[i]);

        foreach (var token in definition.Command)
            args.Add(token);

        return args;
    }

    /// <summary>Formats a <c>-p</c> value: <c>ip:hostPort:containerPort</c>, where an empty host port asks for a random one and an IPv6 address is enclosed in brackets.</summary>
    internal static string FormatPort(ContainerPort port, bool hostIpSupported)
    {
        // TCP is the default of every runtime, and leaving it out keeps the arguments the runtimes always got.
        var protocol = port.Protocol is ContainerPortProtocol.Tcp ? "" : "/" + port.ProtocolName;
        var hostPort = port.HostPort?.ToString(CultureInfo.InvariantCulture);
        if (!hostIpSupported)
            return hostPort is null ? string.Create(CultureInfo.InvariantCulture, $"{port.Port}{protocol}") : string.Create(CultureInfo.InvariantCulture, $"{hostPort}:{port.Port}{protocol}");

        var hostIp = port.HostIp.Contains(':', StringComparison.Ordinal) ? "[" + port.HostIp + "]" : port.HostIp;
        return string.Create(CultureInfo.InvariantCulture, $"{hostIp}:{hostPort}:{port.Port}{protocol}");
    }

    private static void AddHealthcheck(List<string> args, ContainerHealthcheck? healthcheck)
    {
        if (healthcheck is null)
            return;

        if (healthcheck.GetShellCommand() is not { } command)
        {
            args.Add("--no-healthcheck");
            return;
        }

        AddOption(args, "--health-cmd", command);
        AddOption(args, "--health-interval", FormatDuration(healthcheck.Interval));
        AddOption(args, "--health-timeout", FormatDuration(healthcheck.Timeout));
        AddOption(args, "--health-start-period", FormatDuration(healthcheck.StartPeriod));
        AddOption(args, "--health-retries", healthcheck.Retries?.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Formats a duration the way Go parses it, in milliseconds so that no precision the API would keep is lost.</summary>
    private static string? FormatDuration(TimeSpan? value)
        => value is { } duration ? ((long)duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) + "ms" : null;

    private static void AddOption(List<string> args, string flag, string? value)
    {
        if (value is not null)
        {
            args.Add(flag);
            args.Add(value);
        }
    }

    private static void AppendMount(List<string> args, IMount mount, bool quotedMountFieldsSupported)
    {
        switch (mount)
        {
            case BindMount bind:
                AddMountDescriptor(args, "bind", bind.Source, bind.Target, bind.ReadOnly, quotedMountFieldsSupported);
                break;

            case VolumeMount volume:
                AddMountDescriptor(args, "volume", volume.Name, volume.Target, volume.ReadOnly, quotedMountFieldsSupported);
                break;

            case OwnedVolumeMount owned:
                AddMountDescriptor(args, "volume", owned.Volume.Name, owned.Target, owned.ReadOnly, quotedMountFieldsSupported);
                break;

            case TmpfsMount tmpfs:
                var options = tmpfs.FormatOptions();
                args.Add("--tmpfs");
                args.Add(options.Length > 0 ? tmpfs.Target + ":" + options : tmpfs.Target);
                break;

            default:
                throw new NotSupportedException($"Mount type '{mount.GetType()}' is not supported.");
        }
    }

    private static void AddMountDescriptor(List<string> args, string type, string source, string target, bool readOnly, bool quotedMountFieldsSupported)
    {
        var descriptor = new StringBuilder("type=").Append(type);
        AppendMountField(descriptor, "source", source, quotedMountFieldsSupported);
        AppendMountField(descriptor, "target", target, quotedMountFieldsSupported);
        if (readOnly)
            descriptor.Append(",readonly");

        args.Add("--mount");
        args.Add(descriptor.ToString());
    }

    /// <summary>Appends one <c>key=value</c> field to a <c>--mount</c> descriptor, quoting it when the value would otherwise be read as several fields.</summary>
    private static void AppendMountField(StringBuilder descriptor, string key, string value, bool quotedMountFieldsSupported)
    {
        descriptor.Append(',');
        if (!value.Contains(',', StringComparison.Ordinal) && !value.Contains('"', StringComparison.Ordinal))
        {
            descriptor.Append(key).Append('=').Append(value);
            return;
        }

        if (!quotedMountFieldsSupported)
            throw new NotSupportedException($"The runtime cannot mount '{value}' because the path contains a comma or a quote.");

        // The whole descriptor is one CSV record, so a field holding a separator is quoted and its own quotes doubled.
        descriptor.Append('"').Append(key).Append('=').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
    }
}
