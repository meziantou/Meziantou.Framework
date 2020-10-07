using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Meziantou.Framework.DependencyScanning
{
    [StructLayout(LayoutKind.Auto)]
    public readonly struct ScanFileContext : IAsyncDisposable
    {
        private readonly ChannelWriter<Dependency> _channel;
        private readonly Lazy<Stream> _content;

        internal ScanFileContext(string fullPath, ChannelWriter<Dependency> channel, IFileSystem fileSystem, CancellationToken cancellationToken)
        {
            FullPath = fullPath;
            _channel = channel;
            FileSystem = fileSystem;
            CancellationToken = cancellationToken;

            _content = new Lazy<Stream>(() => fileSystem.OpenRead(fullPath));
        }

        public string FullPath { get; }
        public Stream Content => _content.Value;
        public CancellationToken CancellationToken { get; }
        public IFileSystem FileSystem { get; }

        public ValueTask ReportDependency(Dependency dependency)
        {
            return _channel.WriteAsync(dependency, CancellationToken);
        }

        internal void ResetStream()
        {
            if (_content.IsValueCreated)
            {
                _content.Value.Seek(0, SeekOrigin.Begin);
            }
        }

        public ValueTask DisposeAsync()
        {
            if (_content.IsValueCreated)
            {
                return _content.Value.DisposeAsync();
            }

            return default;
        }
    }
}
