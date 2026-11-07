namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A resource limit (<c>ulimit</c>) of the processes of a container.</summary>
/// <param name="Name">The name of the limit, such as <c>nofile</c> or <c>nproc</c>.</param>
/// <param name="Soft">The soft limit.</param>
/// <param name="Hard">The hard limit.</param>
public sealed record ContainerUlimit(string Name, long Soft, long Hard)
{
    /// <summary>Initializes a new instance of the <see cref="ContainerUlimit"/> class whose soft and hard limits are the same.</summary>
    /// <param name="name">The name of the limit, such as <c>nofile</c> or <c>nproc</c>.</param>
    /// <param name="limit">The soft and hard limit.</param>
    public ContainerUlimit(string name, long limit)
        : this(name, limit, limit)
    {
    }

    /// <summary>Gets the name of the limit.</summary>
    public string Name { get; init => field = ValidateName(value); } = ValidateName(Name);

    /// <summary>Formats the limit the way the CLIs take it: <c>name=soft:hard</c>.</summary>
    internal string Format() => string.Create(CultureInfo.InvariantCulture, $"{Name}={Soft}:{Hard}");

    private static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }
}
