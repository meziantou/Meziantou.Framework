using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Hashing;

namespace Meziantou.Framework.Benchmarks;

[MemoryDiagnoser]
public class BcryptBenchmarks
{
    private const string Salt = "$2b$10$Nf6s3UzdgM8Pu9UjNWsrPe";

    [Benchmark]
    public string HashPassword() => Bcrypt.HashPassword("my password", Salt);
}
