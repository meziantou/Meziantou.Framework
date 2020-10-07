using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Meziantou.Framework.DependencyScanning
{
    public sealed class Dependency
    {
        public Dependency(string name, string version, DependencyType type, Location location)
        {
            Name = name;
            Version = version;
            Type = type;
            Location = location;
        }

        public string Name { get; }
        public string Version { get; }
        public DependencyType Type { get; }
        public Location Location { get; }

        public Task UpdateAsync(string newVersion, CancellationToken cancellationToken = default)
        {
            return Location.UpdateAsync(newVersion, cancellationToken);
        }

        public Task UpdateAsync(Stream stream, string newVersion, CancellationToken cancellationToken = default)
        {
            return Location.UpdateAsync(stream, newVersion, cancellationToken);
        }

        public override string ToString()
        {
            return $"{Type}:{Name}@{Version}:{Location}";
        }
    }
}
