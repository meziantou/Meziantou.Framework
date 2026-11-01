namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A snapshot of <see cref="DockerfileBuildOptions"/>, which cannot change once the image source is created.</summary>
internal sealed class DockerfileBuildSettings
{
    public static DockerfileBuildSettings Default { get; } = new(new DockerfileBuildOptions());

    public DockerfileBuildSettings(DockerfileBuildOptions options)
    {
        BuildArguments = [.. options.BuildArguments.OrderBy(static pair => pair.Key, StringComparer.Ordinal)];
        Labels = [.. options.Labels.OrderBy(static pair => pair.Key, StringComparer.Ordinal)];
        Target = options.Target;
        Platform = options.Platform;
        NoCache = options.NoCache;
        Pull = options.Pull;
        Tag = options.Tag;
    }

    public IReadOnlyList<KeyValuePair<string, string>> BuildArguments { get; }

    public IReadOnlyList<KeyValuePair<string, string>> Labels { get; }

    public string? Target { get; }

    public string? Platform { get; }

    public bool NoCache { get; }

    public bool Pull { get; }

    public string? Tag { get; }

    /// <summary>The platform the image is built for: the one of the options, or the one of the container.</summary>
    public string? GetPlatform(ContainerDefinition definition) => Platform ?? definition.Platform;
}
