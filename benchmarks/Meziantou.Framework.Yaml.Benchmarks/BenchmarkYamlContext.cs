using System.Collections.ObjectModel;
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Benchmarks;

/// <summary>The source-generated metadata of the orders of <see cref="DocumentBenchmarks"/>.</summary>
[YamlSerializable(typeof(Collection<DocumentBenchmarks.Order>))]
internal sealed partial class BenchmarkYamlContext : YamlSerializerContext
{
}
