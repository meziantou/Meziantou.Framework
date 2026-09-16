using System.Security.Cryptography;
using System.Text;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Hashes what a container is made of, so a process that adopts a container through <see cref="ContainerDefinition.ReuseId"/> can tell whether it was created from the same definition.</summary>
internal static class ContainerConfigurationHash
{
    /// <summary>Computes the hash of a definition.</summary>
    /// <remarks>Only what ends up in the container counts: how the library waits for it, logs it, or pulls its image does not. A credential the library generated is left out, since every process generates its own; the adopting process reads the value back from the container instead.</remarks>
    public static string Compute(ContainerDefinition definition)
    {
        var builder = new StringBuilder();
        Append(builder, "image", definition.Image switch
        {
            RegistryImage registry => "registry:" + registry.Name,
            DockerfileImage dockerfile => "dockerfile:" + dockerfile.DockerfilePath + "|" + dockerfile.ContextDirectory,
            ArchiveImage archive => "archive:" + archive.ArchivePath,
            ExistingImage existing => "existing:" + existing.ImageId,
            var other => other.ToString(),
        });

        Append(builder, "name", definition.Name);
        Append(builder, "hostname", definition.Hostname);
        Append(builder, "user", definition.User);
        Append(builder, "workdir", definition.WorkingDirectory);

        foreach (var token in definition.Entrypoint)
            Append(builder, "entrypoint", token);

        foreach (var token in definition.Command)
            Append(builder, "command", token);

        foreach (var (name, value) in definition.Environment.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (!definition.IsGeneratedEnvironmentValue(name, value))
                Append(builder, "env", name + "=" + value);
        }

        foreach (var (name, value) in definition.Labels.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            Append(builder, "label", name + "=" + value);

        foreach (var port in definition.Ports.OrderBy(static port => port.Port))
            Append(builder, "port", string.Create(CultureInfo.InvariantCulture, $"{port.HostIp}:{port.HostPort}:{port.Port}"));

        foreach (var mount in definition.Mounts)
        {
            Append(builder, "mount", mount switch
            {
                BindMount bind => string.Create(CultureInfo.InvariantCulture, $"bind|{bind.Source}|{bind.Target}|{bind.ReadOnly}"),
                VolumeMount volume => string.Create(CultureInfo.InvariantCulture, $"volume|{volume.Name}|{volume.Target}|{volume.ReadOnly}"),
                OwnedVolumeMount owned => string.Create(CultureInfo.InvariantCulture, $"volume|{owned.Volume.Name}|{owned.Target}|{owned.ReadOnly}"),
                TmpfsMount tmpfs => "tmpfs|" + tmpfs.Target,
                var other => other.ToString(),
            });
        }

        Append(builder, "network", definition.Network.Network);
        Append(builder, "alias", definition.Network.Alias);
        Append(builder, "memory", definition.Resources.MemoryLimit?.ToString(CultureInfo.InvariantCulture));
        Append(builder, "cpus", definition.Resources.CpuLimit?.ToString(CultureInfo.InvariantCulture));
        Append(builder, "read-only", definition.Resources.ReadOnlyRootFilesystem ? "true" : "false");

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    /// <summary>Appends one field. The length prefix keeps a value containing a separator from being read as two fields.</summary>
    private static void Append(StringBuilder builder, string name, string? value)
    {
        builder.Append(name).Append(':');
        if (value is null)
        {
            builder.Append("null\n");
            return;
        }

        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('\n');
    }
}
