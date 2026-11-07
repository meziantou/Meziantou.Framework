namespace Meziantou.Framework.TemporaryContainers;

/// <summary>A tmpfs (in-memory) mount inside the container.</summary>
/// <param name="Target">The path inside the container.</param>
public sealed record TmpfsMount(string Target) : IMount
{
    /// <summary>Gets the maximum size of the mount, in bytes. <see langword="null"/> keeps the default of the runtime, which is half of the memory of the host on Linux.</summary>
    /// <remarks>Apple's container runtime does not take options for a tmpfs mount.</remarks>
    public long? Size
    {
        get;
        init
        {
            if (value is { } size)
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size, nameof(value));

            field = value;
        }
    }

    /// <summary>Gets the permissions of the root of the mount. <see langword="null"/> keeps the default of the runtime (<c>1777</c>).</summary>
    /// <remarks>Apple's container runtime does not take options for a tmpfs mount.</remarks>
    public UnixFileMode? Mode { get; init; }

    string IMount.ContainerPath => Target;

    /// <summary>Formats the options the way the runtimes take them: <c>size=…,mode=…</c>, with the mode in octal. Empty when there is none.</summary>
    internal string FormatOptions()
    {
        var options = new List<string>(2);
        if (Size is { } size)
            options.Add("size=" + size.ToString(CultureInfo.InvariantCulture));

        if (Mode is { } mode)
            options.Add("mode=" + Convert.ToString((int)mode, 8));

        return string.Join(',', options);
    }
}
