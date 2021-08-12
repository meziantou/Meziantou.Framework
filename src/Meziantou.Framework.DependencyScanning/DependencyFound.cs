using System.Threading.Tasks;

namespace Meziantou.Framework.DependencyScanning;

public delegate ValueTask DependencyFound(Dependency dependency);
