using System.Collections.ObjectModel;
using SYS = System.Text.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Benchmarks;

/// <summary>The metadata that Styml's source generator writes for the orders of <see cref="DocumentBenchmarks"/>.</summary>
[SYS.YamlSerializable(typeof(Collection<DocumentBenchmarks.Order>))]
[SYS.YamlSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal sealed partial class StymlBenchmarkYamlContext : SYS.YamlSerializerContext;
