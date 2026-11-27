using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Hashing;

namespace Meziantou.Framework.Benchmarks;

[MemoryDiagnoser]
public class Argon2Benchmarks
{
    private readonly byte[] _destination = new byte[32];

    [Params(Argon2Type.Argon2d, Argon2Type.Argon2id)]
    public Argon2Type Type { get; set; }

    // 4 MiB of memory and 3 passes, in 1 lane or 4 lanes
    [Params(1, 4)]
    public int Parallelism { get; set; }

    [Benchmark]
    public void DeriveBytes() => Argon2.DeriveBytes(Type, "my password"u8, "0123456789abcdef"u8, Parallelism, memorySizeInKiB: 4096, passes: 3, _destination);
}
