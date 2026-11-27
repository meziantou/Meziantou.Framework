namespace Meziantou.Framework.WasmEngine.Wasi;

/// <summary>Options of a <see cref="WasiContext"/>: what the module sees of its environment.</summary>
public sealed class WasiOptions
{
    /// <summary>
    /// Gets the command-line arguments of the module (<c>args_get</c>). By convention, the first argument is the name of
    /// the program. The default is no argument.
    /// </summary>
    public IList<string> Arguments { get; init; } = [];

    /// <summary>Gets the environment variables of the module (<c>environ_get</c>). The default is no variable.</summary>
    public IDictionary<string, string> EnvironmentVariables { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the stream that the module reads as its standard input (file descriptor 0), or null for an empty input.</summary>
    public Stream? StandardInput { get; init; }

    /// <summary>Gets the stream where the module writes its standard output (file descriptor 1), or null to discard the output.</summary>
    public Stream? StandardOutput { get; init; }

    /// <summary>Gets the stream where the module writes its standard error (file descriptor 2), or null to discard the output.</summary>
    public Stream? StandardError { get; init; }

    /// <summary>
    /// Gets the directories that the module can access, as the file descriptors 3, 4, ... in this order. The module can
    /// access no file outside of these directories. The default is no directory.
    /// </summary>
    public IList<WasiPreopenedDirectory> PreopenedDirectories { get; init; } = [];

    /// <summary>
    /// Gets the clock of the module: the realtime clock is <see cref="TimeProvider.GetUtcNow"/>, the monotonic clock is
    /// <see cref="TimeProvider.GetTimestamp"/>, and <c>poll_oneoff</c> waits with <see cref="TimeProvider.CreateTimer"/>.
    /// The default is <see cref="TimeProvider.System"/>.
    /// </summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// Gets the source of the random bytes of the module (<c>random_get</c>), or null to use
    /// <see cref="System.Security.Cryptography.RandomNumberGenerator.Fill"/>.
    /// </summary>
    public WasiRandomSource? RandomSource { get; init; }

    /// <summary>
    /// Gets the network of a component (<c>wasi:sockets</c> of WASI 0.3), or null for a component without network: its
    /// sockets fail with <c>access-denied</c>. Use <see cref="WasiHostNetwork"/> for the network of the machine (with the
    /// rules of what the component can access), or <see cref="WasiInMemoryNetwork"/> for a network inside the process.
    /// The modules of WASI Preview 1 have no sockets.
    /// </summary>
    public WasiNetwork? Network { get; init; }

    /// <summary>
    /// Gets the HTTP client of a component (<c>wasi:http/client</c> of WASI 0.3), or null for a component that cannot send
    /// requests: they fail with <c>HTTP-request-denied</c>. Use <see cref="WasiHostHttpClient"/> to send the requests over
    /// the network of the machine (with the rules of the requests the component can send), or
    /// <see cref="WasiInMemoryHttpClient"/> to give them to a handler of the process.
    /// </summary>
    public WasiHttpClient? HttpClient { get; init; }
}

/// <summary>Fills a buffer with random bytes (see <see cref="WasiOptions.RandomSource"/>).</summary>
/// <param name="buffer">The buffer to fill.</param>
public delegate void WasiRandomSource(Span<byte> buffer);

/// <summary>A directory that a WASI module can access (see <see cref="WasiOptions.PreopenedDirectories"/>).</summary>
public sealed class WasiPreopenedDirectory
{
    /// <summary>Initializes a new instance of the <see cref="WasiPreopenedDirectory"/> class.</summary>
    /// <param name="guestPath">
    /// The path of the directory for the module (<c>fd_prestat_dir_name</c>), such as <c>/</c>, <c>/data</c> or <c>.</c>:
    /// the C library of WASI (wasi-libc) opens the paths that start with it in this directory.
    /// </param>
    /// <param name="fileSystem">The files of the directory.</param>
    /// <exception cref="ArgumentException"><paramref name="guestPath"/> is empty or contains a null character.</exception>
    public WasiPreopenedDirectory(string guestPath, WasiFileSystem fileSystem)
    {
        ArgumentException.ThrowIfNullOrEmpty(guestPath);
        ArgumentNullException.ThrowIfNull(fileSystem);
        if (guestPath.Contains('\0', StringComparison.Ordinal))
            throw new ArgumentException("The path contains a null character", nameof(guestPath));

        GuestPath = guestPath;
        FileSystem = fileSystem;
    }

    /// <summary>Gets the path of the directory for the module.</summary>
    public string GuestPath { get; }

    /// <summary>Gets the files of the directory.</summary>
    public WasiFileSystem FileSystem { get; }
}
