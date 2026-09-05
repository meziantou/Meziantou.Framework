namespace Meziantou.Framework.TemporaryContainers.Internals;

internal static class DockerCreateArgumentBuilder
{
    public const string ReuseLabel = "meziantou.tc.reuse";

    /// <param name="quotedMountFieldsSupported">Whether the runtime parses the <c>--mount</c> descriptor as a CSV record, which is the only way to express a value containing a comma. Docker does; podman and wslc split on the comma instead.</param>
    public static List<string> Build(ContainerDefinition definition, string imageRef, string? pullPolicyValue, bool quotedMountFieldsSupported)
    {
        var args = new List<string> { "create" };

        AddOption(args, "--name", definition.Name);
        AddOption(args, "--hostname", definition.Hostname);
        AddOption(args, "--user", definition.User);
        AddOption(args, "--workdir", definition.WorkingDirectory);
        AddOption(args, "--pull", pullPolicyValue);

        if (definition.Resources.ReadOnlyRootFilesystem)
            args.Add("--read-only");

        if (definition.Resources.MemoryLimit is { } memory)
            AddOption(args, "--memory", memory.ToString(CultureInfo.InvariantCulture) + "b");

        if (definition.Resources.CpuLimit is { } cpu)
            AddOption(args, "--cpus", cpu.ToString(CultureInfo.InvariantCulture));

        AddOption(args, "--network", definition.Network.Network);
        AddOption(args, "--network-alias", definition.Network.Alias);

        foreach (var (name, value) in definition.Labels)
        {
            args.Add("--label");
            args.Add($"{name}={value}");
        }

        if (definition.ReuseId is { } reuseId)
        {
            args.Add("--label");
            args.Add($"{ReuseLabel}={reuseId}");
        }

        foreach (var (name, value) in definition.Environment)
        {
            args.Add("--env");
            args.Add($"{name}={value}");
        }

        foreach (var port in definition.Ports)
        {
            args.Add("-p");
            args.Add(port.HostPort is { } hostPort
                ? string.Create(CultureInfo.InvariantCulture, $"{hostPort}:{port.Port}")
                : port.Port.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var mount in definition.Mounts)
            AppendMount(args, mount, quotedMountFieldsSupported);

        var entrypoint = new List<string>(definition.Entrypoint);
        if (entrypoint.Count > 0)
            AddOption(args, "--entrypoint", entrypoint[0]);

        args.Add(imageRef);

        for (var i = 1; i < entrypoint.Count; i++)
            args.Add(entrypoint[i]);

        foreach (var token in definition.Command)
            args.Add(token);

        return args;
    }

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
                args.Add("--tmpfs");
                args.Add(tmpfs.Target);
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
