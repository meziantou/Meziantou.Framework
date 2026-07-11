namespace Meziantou.Framework.TemporaryContainers.Internals;

internal static class DockerCreateArgumentBuilder
{
    public const string ReuseLabel = "meziantou.tc.reuse";

    public static List<string> Build(ContainerDefinition definition, string imageRef, string? pullPolicyValue)
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
            AppendMount(args, mount);

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

    private static void AppendMount(List<string> args, IMount mount)
    {
        switch (mount)
        {
            case BindMount bind:
                args.Add("--mount");
                var descriptor = $"type=bind,source={bind.Source},target={bind.Target}";
                if (bind.ReadOnly)
                    descriptor += ",readonly";
                args.Add(descriptor);
                break;

            case VolumeMount volume:
                args.Add("--mount");
                args.Add($"type=volume,source={volume.Name},target={volume.Target}");
                break;

            case TmpfsMount tmpfs:
                args.Add("--tmpfs");
                args.Add(tmpfs.Target);
                break;

            default:
                throw new NotSupportedException($"Mount type '{mount.GetType()}' is not supported.");
        }
    }
}
