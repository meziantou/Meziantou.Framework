using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;

#if NET461 || NET462 || NET472
using System.Security.AccessControl;
using System.Security.Principal;
#endif

namespace Meziantou.Framework;

/// <summary>
/// Ensures that only a single instance of an application can run at a time and provides communication between instances.
/// </summary>
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
    private NamedPipeServerStream? _server;
    private Mutex? _mutex;

    /// <summary>
    /// Occurs when another instance of the application attempts to start.
    /// </summary>
    public event EventHandler<SingleInstanceEventArgs>? NewInstance;

    private string PipeName { get; } = OperatingSystem.IsWindows() ? $"Local\\Pipe_{applicationId}_{GetSessionId().ToString(CultureInfo.InvariantCulture)}" : null!;

    /// <summary>
    /// Gets or sets a value indicating whether to start a named pipe server to receive notifications from other instances.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to start the server; otherwise, <see langword="false"/>. The default is <see langword="true"/>.
    /// </value>
    public bool StartServer { get; set; } = true;

    /// <summary>
    /// Gets or sets the timeout for connecting to the first instance when notifying it.
    /// </summary>
    /// <value>
    /// The connection timeout. The default is 3 seconds.
    /// </value>
    public TimeSpan ClientConnectionTimeout { get; set; } = TimeSpan.FromSeconds(3);

    private static int GetSessionId()
    {
        using var currentProcess = Process.GetCurrentProcess();
        return currentProcess.SessionId;
    }

    /// <summary>
    /// Attempts to start the application as the first instance.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if this is the first instance and the application can start; otherwise, <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// If this method returns <see langword="true"/>, the application should continue running and can receive notifications from other instances through the <see cref="NewInstance"/> event.
    /// If this method returns <see langword="false"/>, the application should call <see cref="NotifyFirstInstance"/> to notify the first instance and then exit.
    /// </remarks>
    public bool StartApplication()
    {
        if (TryAcquireMutex())
        {
            StartNamedPipeServer();
            return true;
        }

        return false;
    }

    private void StartNamedPipeServer()
    {
        if (!StartServer)
            return;

#if NETCOREAPP2_1_OR_GREATER
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The communication with the first instance is only supported on Windows");

        _server = new NamedPipeServerStream(
                PipeName,
                PipeDirection.In,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Message,
                PipeOptions.CurrentUserOnly);
#elif NET461 || NET462 || NET472
        using (var currentIdentity = WindowsIdentity.GetCurrent())
        {
            var identifier = currentIdentity.Owner;

            // Grant full control to the owner so multiple servers can be opened.
            // Full control is the default per MSDN docs for CreateNamedPipe.
            var rule = new PipeAccessRule(identifier, PipeAccessRights.FullControl, AccessControlType.Allow);
            var pipeSecurity = new PipeSecurity();

            pipeSecurity.AddAccessRule(rule);
            pipeSecurity.SetOwner(identifier);

            _server = new NamedPipeServerStream(
                       PipeName,
                       PipeDirection.In,
                       NamedPipeServerStream.MaxAllowedServerInstances,
                       PipeTransmissionMode.Message,
                       PipeOptions.Asynchronous,
                       0,
                       0,
                       pipeSecurity);
        }
#else
#error Platform not supported
#endif
        try
        {
            _server.BeginWaitForConnection(Listen, state: null);
        }
        catch (ObjectDisposedException)
        {
            // The server was disposed before getting a connection
        }
    }

    private void Listen(IAsyncResult ar)
    {
        var server = _server;
        if (server is null)
            return;

        try
        {
            server.EndWaitForConnection(ar);
            StartNamedPipeServer();

            using var binaryReader = new BinaryReader(server);
            if (binaryReader.ReadByte() == NotifyInstanceMessageType)
            {
                var processId = binaryReader.ReadInt32();
                var argCount = binaryReader.ReadInt32();
                if (argCount >= 0)
                {
                    var args = new string[argCount];
                    for (var i = 0; i < argCount; i++)
                    {
                        args[i] = binaryReader.ReadString();
                    }

                    NewInstance?.Invoke(this, new SingleInstanceEventArgs(processId, args));
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            server.Dispose();
        }
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

    /// <summary>
    /// Notifies the first instance of the application that another instance is attempting to start.
    /// </summary>
    /// <param name="args">The command-line arguments to send to the first instance.</param>
    /// <returns>
    /// <see langword="true"/> if the notification was sent successfully; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This method is only supported on Windows. The first instance must have <see cref="StartServer"/> set to <see langword="true"/> to receive notifications.
    /// The method will timeout after <see cref="ClientConnectionTimeout"/> if the first instance is not responding.
    /// </remarks>
    public bool NotifyFirstInstance(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
        try
        {
            client.Connect((int)ClientConnectionTimeout.TotalMilliseconds);

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
            return false;
        }
    }

    /// <summary>
    /// Releases all resources used by the <see cref="SingleInstance"/> object.
    /// </summary>
    public void Dispose()
    {
        _mutex?.Dispose();
        _server?.Dispose();
    }
}
