using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;

#if NET461
using System.Security.AccessControl;
using System.Security.Principal;
#endif

namespace Meziantou.Framework
{
    public sealed class SingleInstance : IDisposable
    {
        private const byte NotifyInstanceMessageType = 1;

        private readonly Guid _applicationId;
        private NamedPipeServerStream? _server;
        private Mutex? _mutex;

        public event EventHandler<SingleInstanceEventArgs>? NewInstance;

        public SingleInstance(Guid applicationId)
        {
            _applicationId = applicationId;
        }

        private string PipeName => "Local\\Pipe" + _applicationId.ToString();

        public bool StartServer { get; set; } = true;

        public TimeSpan ClientConnectionTimeout { get; set; } = TimeSpan.FromSeconds(3);

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

#if NETCOREAPP2_1 || NETCOREAPP3_1
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                throw new PlatformNotSupportedException("The communication with the first instance is only supported on Windows");

            _server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Message,
                    PipeOptions.CurrentUserOnly);
#elif NET461
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
                _server.BeginWaitForConnection(Listen, state: null!); // TODO-NULLABLE https://github.com/dotnet/runtime/pull/42442
            }
            catch (ObjectDisposedException)
            {
                // The server was disposed before getting a connection
            }
        }

        private void Listen(IAsyncResult ar)
        {
            var server = _server;
            if (server == null)
                return;

            try
            {
                try
                {
                    server.EndWaitForConnection(ar);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

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
            finally
            {
                server.Dispose();
            }
        }

        private bool TryAcquireMutex()
        {
            if (_mutex == null)
            {
                var mutexName = "Local\\Mutex" + _applicationId.ToString();
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

        public bool NotifyFirstInstance(string[] args)
        {
            if (args is null)
                throw new ArgumentNullException(nameof(args));

            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            try
            {
                client.Connect((int)ClientConnectionTimeout.TotalMilliseconds);

                // type, process id, arg length, arg1, arg2, ...
                using (var ms = new MemoryStream())
                {
                    using (var binaryWriter = new BinaryWriter(ms))
                    {
                        binaryWriter.Write(NotifyInstanceMessageType);
                        binaryWriter.Write(GetCurrentProcessId());
                        binaryWriter.Write(args.Length);
                        foreach (var arg in args)
                        {
                            binaryWriter.Write(arg);
                        }
                    }

                    var buffer = ms.ToArray();
                    client.Write(buffer, 0, buffer.Length);
                    client.Flush();
                }

                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        private static int GetCurrentProcessId()
        {
#if NET5_0
            return Environment.ProcessId;
#elif NET461 || NETCOREAPP3_1
            return System.Diagnostics.Process.GetCurrentProcess().Id;
#else
#error Platform not supported
#endif
        }

        public void Dispose()
        {
            _mutex?.Dispose();
            _server?.Dispose();
        }
    }
}
