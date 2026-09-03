using System.Buffers.Text;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Meziantou.Framework;

/// <summary>Ensures that only a single instance of an application can run at a time and provides communication between instances.</summary>
/// <example>
/// Basic usage:
/// <code>
/// var applicationId = new Guid("dfae4e70-179f-4726-aa98-00a832315f5a");
/// using var singleInstance = new SingleInstance(applicationId);
///
/// // Subscribe before calling StartApplication, otherwise a notification raised
/// // between the server starting and the handler being attached is dropped.
/// singleInstance.NewInstance += (sender, e) =>
/// {
///     // Handle notification from another instance
///     Console.WriteLine($"New instance started with {e.Arguments.Length} arguments");
/// };
///
/// if (singleInstance.StartApplication())
/// {
///     // This is the first instance
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
    // Arming the next listener can lose a race against the teardown of the previous one, so it is retried rather
    // than abandoned. The bound keeps a genuinely unusable pipe from spinning a thread pool thread.
    private const int MaxListenerArmAttempts = 5;
    private static readonly TimeSpan ListenerArmRetryDelay = TimeSpan.FromMilliseconds(20);

    private readonly Lock _lock = new();
    private readonly string _mutexName = GetMutexName(applicationId);
    private NamedPipeServerStream? _server;
    private Mutex? _mutex;
    private bool _disposed;
    private bool _started;

    /// <summary>
    /// Occurs when another instance of the application attempts to start.
    /// </summary>
    public event EventHandler<SingleInstanceEventArgs>? NewInstance;

    internal string PipeName { get; } = GetPipeName(applicationId);

    /// <summary>Gets or sets a value indicating whether to start a named pipe server to receive notifications from other instances.</summary>
    /// <value>
    /// <see langword="true"/> to start the server; otherwise, <see langword="false"/>. The default is <see langword="true"/>.
    /// </value>
    public bool StartServer { get; set; } = true;

    /// <summary>Gets or sets the timeout for connecting to the first instance when notifying it.</summary>
    /// <value>The connection timeout. The default is 3 seconds.</value>
    public TimeSpan ClientConnectionTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Computes the name of the mutex that decides which process is the first instance.</summary>
    private static string GetMutexName(Guid applicationId)
    {
        // On Windows the "Local\" prefix scopes the mutex to the logon session, which is exactly the wanted scope.
        // On Unix it scopes it to the process session (getsid), which changes from one terminal or launcher to the
        // next: every launch would then believe it is the first instance. "Global\" is the only prefix that spans
        // sessions there, and its backing directory is shared by every user of the machine, so the scope that
        // "Local\" gives for free on Windows has to be written into the name instead.
        return OperatingSystem.IsWindows()
            ? @"Local\Mutex" + applicationId.ToString()
            : @"Global\" + GetUserScopedName(applicationId);
    }

    /// <summary>Computes the name of the pipe the first instance listens on.</summary>
    private static string GetPipeName(Guid applicationId)
    {
        // On Unix the name becomes a file name under the temporary directory, and the resulting path has to fit in a
        // sockaddr_un: 104 characters on macOS, where the per-user temporary directory already takes about half of
        // them. Hence the compact name rather than the readable Windows one.
        return OperatingSystem.IsWindows()
            ? $@"Local\Pipe_{applicationId}_{GetSessionId().ToString(CultureInfo.InvariantCulture)}"
            : GetUserScopedName(applicationId);
    }

    /// <summary>Builds a short, file-name-safe name that identifies the application for the current user only.</summary>
    private static string GetUserScopedName(Guid applicationId)
    {
        Span<byte> applicationIdBytes = stackalloc byte[16];
        _ = applicationId.TryWriteBytes(applicationIdBytes);

        // The user name can be arbitrarily long and is not necessarily a valid file name, so only a prefix of its hash
        // is used. It separates users, it is not a secret, so 48 bits is plenty.
        Span<byte> userNameHash = stackalloc byte[32];
        _ = SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName), userNameHash);

        return Base64Url.EncodeToString(applicationIdBytes) + "_" + Base64Url.EncodeToString(userNameHash[..6]);
    }

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
    /// The instance is scoped to the current user: two users of the same machine can each run their own instance.
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

        // Message mode is a Windows concept. Byte mode is enough on both: a client opens a connection, writes exactly
        // one message, and closes it, so the end of the message is the end of the stream.
        var transmissionMode = PipeTransmissionMode.Byte;
        if (OperatingSystem.IsWindows())
        {
            transmissionMode = PipeTransmissionMode.Message;
        }

        var server = new NamedPipeServerStream(
                PipeName,
                PipeDirection.In,
                NamedPipeServerStream.MaxAllowedServerInstances,
                transmissionMode,
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

    /// <summary>Arms the listener that replaces the one that has just been consumed, and keeps trying for as long as it is worth it.</summary>
    private void ReplaceListener()
    {
        // Another instance of the pipe can be refused while the previous one is still being torn down. Losing that
        // race used to end the server for good, which is all a client needs to silence an application for the rest
        // of its life: connect, disconnect, and never be heard from again.
        for (var attempt = 1; attempt <= MaxListenerArmAttempts; attempt++)
        {
            lock (_lock)
            {
                if (_disposed)
                    return;
            }

            try
            {
                StartNamedPipeServer();
                return;
            }
            catch (Exception)
            {
                // Runs on a thread pool thread, so the exception must not escape. The wait is short and bounded:
                // the process is deaf until a listener is armed again.
                Thread.Sleep(ListenerArmRetryDelay);
            }
        }
    }

    private void Listen(IAsyncResult ar)
    {
        var server = (NamedPipeServerStream)ar.AsyncState!;

        var connected = false;
        try
        {
            server.EndWaitForConnection(ar);
            connected = true;
        }
        catch (Exception)
        {
            // Either Dispose ran while this listener was waiting, or the client gave up before the connection was
            // established. Only the first means the application is going away, so this listener is replaced like any
            // other; ReplaceListener is what tells the two apart.
        }

        if (!connected)
        {
            server.Dispose();
            ReplaceListener();
            return;
        }

        // Re-arm before reading, so a malformed or truncated message cannot stop the
        // application from accepting notifications from later instances.
        ReplaceListener();

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
        _mutex ??= new Mutex(initiallyOwned: false, name: _mutexName);

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
    /// The first instance must have <see cref="StartServer"/> set to <see langword="true"/> to receive notifications.
    /// The method will timeout after <see cref="ClientConnectionTimeout"/> if the first instance is not responding.
    /// Failing to reach the first instance is reported by returning <see langword="false"/> instead of throwing.
    /// </remarks>
    public bool NotifyFirstInstance(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
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
        catch (SocketException)
        {
            // On Unix the pipe is a Unix domain socket. Anything left over at its path by a previous run, or a peer
            // that goes away mid-write, surfaces as a socket error instead of an IOException.
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
