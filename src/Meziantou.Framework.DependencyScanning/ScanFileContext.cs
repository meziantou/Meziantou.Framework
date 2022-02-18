using System.Runtime.InteropServices;

namespace Meziantou.Framework.DependencyScanning
{
    [StructLayout(LayoutKind.Auto)]
    public readonly struct ScanFileContext : IAsyncDisposable
    {
        private readonly Lazy<Stream> _content;
        private readonly DependencyFound _onDependencyFound;

        internal ScanFileContext(string fullPath, DependencyFound onDependencyFound, IFileSystem fileSystem, CancellationToken cancellationToken)
        {
            FullPath = fullPath;
            _onDependencyFound = onDependencyFound;
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
            return _onDependencyFound(dependency);
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
