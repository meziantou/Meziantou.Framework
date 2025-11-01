using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;

#if NET461 || NET462 || NET472
using System.Security.AccessControl;
using System.Security.Principal;
#endif

namespace Meziantou.Framework;

/// <summary>
/// Provides single-instance application functionality by ensuring only one instance of an application can run at a time.
/// </summary>
/// <param name="applicationId">A unique identifier for the application.</param>
public sealed class SingleInstance(Guid applicationId) : IDisposable
{
    private const byte NotifyInstanceMessageType = 1;
    private NamedPipeServerStream? _server;
    private Mutex? _mutex;

    /// <summary>
    /// Occurs when a new instance of the application attempts to start.
    /// </summary>
    public event EventHandler<SingleInstanceEventArgs>? NewInstance;

    private string PipeName { get; } = OperatingSystem.IsWindows() ? $"Local\\Pipe_{applicationId}_{GetSessionId().ToString(CultureInfo.InvariantCulture)}" : null!;

    /// <summary>
    /// Gets or sets a value indicating whether to start the named pipe server for inter-process communication.
    /// </summary>
    public bool StartServer { get; set; } = true;

    /// <summary>
    /// Gets or sets the timeout duration for client connections when notifying the first instance.
    /// </summary>
    public TimeSpan ClientConnectionTimeout { get; set; } = TimeSpan.FromSeconds(3);

    private static int GetSessionId()
    {
        using var currentProcess = Process.GetCurrentProcess();
        return currentProcess.SessionId;
    }

    /// <summary>
    /// Attempts to start the application as the first instance.
    /// </summary>
    /// <returns><see langword="true"/> if this is the first instance and the application can start; otherwise, <see langword="false"/>.</returns>
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
    /// Notifies the first instance of the application with the specified arguments.
    /// </summary>
    /// <param name="args">The arguments to pass to the first instance.</param>
    /// <returns><see langword="true"/> if the first instance was successfully notified; otherwise, <see langword="false"/>.</returns>
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

    public void Dispose()
    {
        _mutex?.Dispose();
        _server?.Dispose();
    }
}
