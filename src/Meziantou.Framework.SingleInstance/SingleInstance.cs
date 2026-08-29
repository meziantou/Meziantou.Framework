using System.Diagnostics;
using System.IO.Pipes;

namespace Meziantou.Framework;

/// <summary>Ensures that only a single instance of an application can run at a time and provides communication between instances.</summary>
/// <example>
/// Basic usage:
/// <code>
/// var applicationId = new Guid("dfae4e70-179f-4726-aa98-00a832315f5a");
/// using var singleInstance = new SingleInstance(applicationId);
/// if (singleInstance.StartApplication())
/// {
///     // This is the first instance
///     singleInstance.NewInstance += (sender, e) =>
///     {
///         // Handle notification from another instance
///         Console.WriteLine($"New instance started with {e.Arguments.Length} arguments");
///     };
/// }
/// else
/// {
///     // Notify the first instance
///     singleInstance.NotifyFirstInstance(args);
/// }
/// </code>
/// </example>
public sealed class SingleInstance(Guid applicationId) : IDisposable
{
    private const byte NotifyInstanceMessageType = 1;

    // Upper bound on the number of arguments accepted from the pipe. The message is
    // sender-controlled, so the count is validated before allocating from it.
    private const int MaxArgumentCount = 64 * 1024;
    private readonly Lock _lock = new();
    private NamedPipeServerStream? _server;
    private Mutex? _mutex;
    private bool _disposed;
    private bool _started;

    /// <summary>
    /// Occurs when another instance of the application attempts to start.
    /// </summary>
    public event EventHandler<SingleInstanceEventArgs>? NewInstance;

    internal string PipeName { get; } = OperatingSystem.IsWindows() ? $"Local\\Pipe_{applicationId}_{GetSessionId().ToString(CultureInfo.InvariantCulture)}" : null!;

    /// <summary>Gets or sets a value indicating whether to start a named pipe server to receive notifications from other instances.</summary>
    /// <value>
    /// <see langword="true"/> to start the server; otherwise, <see langword="false"/>. The default is <see langword="true"/>.
    /// </value>
    public bool StartServer { get; set; } = true;

    /// <summary>Gets or sets the timeout for connecting to the first instance when notifying it.</summary>
    /// <value>The connection timeout. The default is 3 seconds.</value>
    public TimeSpan ClientConnectionTimeout { get; set; } = TimeSpan.FromSeconds(3);

    private static int GetSessionId()
    {
        using var currentProcess = Process.GetCurrentProcess();
        return currentProcess.SessionId;
    }

    /// <summary>Attempts to start the application as the first instance.</summary>
    /// <returns>
    /// <see langword="true"/> if this is the first instance and the application can start; otherwise, <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// If this method returns <see langword="true"/>, the application should continue running and can receive notifications from other instances through the <see cref="NewInstance"/> event.
    /// If this method returns <see langword="false"/>, the application should call <see cref="NotifyFirstInstance"/> to notify the first instance and then exit.
    /// </remarks>
    public bool StartApplication()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Starting twice would acquire the re-entrant mutex again and leak the first
        // pipe server, so a successful start is remembered and replayed.
        if (_started)
            return true;

        if (!TryAcquireMutex())
            return false;

        _started = true;
        StartNamedPipeServer();
        return true;
    }

    private void StartNamedPipeServer()
    {
        if (!StartServer)
            return;

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The communication with the first instance is only supported on Windows");

        var server = new NamedPipeServerStream(
                PipeName,
                PipeDirection.In,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Message,
                PipeOptions.CurrentUserOnly);

        lock (_lock)
        {
            if (_disposed)
            {
                // Dispose ran while this listener was being created. Publishing it would
                // leave a live pipe server behind for the lifetime of the process.
                server.Dispose();
                return;
            }

            _server = server;
        }

        try
        {
            server.BeginWaitForConnection(Listen, state: server);
        }
        catch (ObjectDisposedException)
        {
            // The server was disposed before getting a connection
        }
    }

    private void Listen(IAsyncResult ar)
    {
        var server = (NamedPipeServerStream)ar.AsyncState!;

        try
        {
            server.EndWaitForConnection(ar);
        }
        catch (Exception)
        {
            // The server was disposed while waiting, or the connection failed. Do not re-arm:
            // the application is either shutting down or the pipe is unusable.
            server.Dispose();
            return;
        }

        // Re-arm before reading, so a malformed or truncated message cannot stop the
        // application from accepting notifications from later instances.
        try
        {
            StartNamedPipeServer();
        }
        catch (Exception)
        {
            // Keep handling the current connection even if the next listener could not start.
        }

        SingleInstanceEventArgs? eventArgs;
        try
        {
            eventArgs = ReadNotification(server);
        }
        catch (Exception)
        {
            // Truncated, malformed, or hostile message. This runs on a thread pool thread,
            // so letting the exception escape would terminate the process.
            eventArgs = null;
        }
        finally
        {
            server.Dispose();
        }

        // Raised outside the catch above so an exception thrown by a subscriber is not
        // silently swallowed as if it were a protocol error.
        if (eventArgs is not null)
        {
            NewInstance?.Invoke(this, eventArgs);
        }
    }

    private static SingleInstanceEventArgs? ReadNotification(NamedPipeServerStream server)
    {
        using var binaryReader = new BinaryReader(server);
        if (binaryReader.ReadByte() != NotifyInstanceMessageType)
            return null;

        var processId = binaryReader.ReadInt32();
        var argCount = binaryReader.ReadInt32();
        if (argCount is < 0 or > MaxArgumentCount)
            return null;

        var args = new string[argCount];
        for (var i = 0; i < argCount; i++)
        {
            args[i] = binaryReader.ReadString();
        }

        return new SingleInstanceEventArgs(processId, args);
    }

    private bool TryAcquireMutex()
    {
        if (_mutex is null)
        {
            var mutexName = "Local\\Mutex" + applicationId.ToString();
            _mutex = new Mutex(initiallyOwned: false, name: mutexName);
        }

        try
        {
            return _mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    /// <summary>Notifies the first instance of the application that another instance is attempting to start.</summary>
    /// <param name="args">The command-line arguments to send to the first instance.</param>
    /// <returns>
    /// <see langword="true"/> if the notification was sent successfully; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method is only supported on Windows. The first instance must have <see cref="StartServer"/> set to <see langword="true"/> to receive notifications.
    /// The method will timeout after <see cref="ClientConnectionTimeout"/> if the first instance is not responding.
    /// Failing to reach the first instance is reported by returning <see langword="false"/> instead of throwing.
    /// </remarks>
    public bool NotifyFirstInstance(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(GetConnectionTimeoutInMilliseconds());

            // type, process id, arg length, arg1, arg2, ...
            using var ms = new MemoryStream();
            using (var binaryWriter = new BinaryWriter(ms))
            {
                binaryWriter.Write(NotifyInstanceMessageType);
                binaryWriter.Write(Environment.ProcessId);
                binaryWriter.Write(args.Length);
                foreach (var arg in args)
                {
                    binaryWriter.Write(arg);
                }
            }

            var buffer = ms.ToArray();
            client.Write(buffer, 0, buffer.Length);
            client.Flush();

            return true;
        }
        catch (TimeoutException)
        {
            // The first instance did not accept the connection in time
            return false;
        }
        catch (IOException)
        {
            // The first instance stopped listening while the message was being sent
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // The pipe exists but is not accessible from this process
            return false;
        }
    }

    private int GetConnectionTimeoutInMilliseconds()
    {
        if (ClientConnectionTimeout == Timeout.InfiniteTimeSpan)
            return Timeout.Infinite;

        var milliseconds = ClientConnectionTimeout.TotalMilliseconds;
        return milliseconds <= 0 ? 0 : (int)Math.Min(milliseconds, int.MaxValue);
    }

    /// <summary>
    /// Releases all resources used by the <see cref="SingleInstance"/> object.
    /// </summary>
    public void Dispose()
    {
        Mutex? mutex;
        NamedPipeServerStream? server;

        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            mutex = _mutex;
            server = _server;
            _mutex = null;
            _server = null;
        }

        mutex?.Dispose();
        server?.Dispose();
    }
}
