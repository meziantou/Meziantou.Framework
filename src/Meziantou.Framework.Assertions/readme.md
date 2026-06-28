# Meziantou.Framework.Assertions

Assertion helpers for .NET tests.

## Analyzer rules

The package ships analyzers and code fixes to help write clearer assertions.

<!-- analyzer-rules -->
| Id | Category | Description | Severity | Enabled |
| -- | -- | -- | :--: | :--: |
| `MFAS0001` | Assertions | Pass the expected value before the actual value | Warning | ✔️ |
| `MFAS0002` | Assertions | Use Assert.Same instead of Assert.ReferenceEquals | Error | ✔️ |
| `MFAS0003` | Assertions | Do not use Assert.IsType with static or abstract types | Error | ✔️ |
| `MFAS0004` | Assertions | Use Assert.Empty for zero count checks | Warning | ✔️ |
| `MFAS0005` | Assertions | Use specialized count assertions | Warning | ✔️ |
| `MFAS0006` | Assertions | Use Assert.Null for null comparisons | Warning | ✔️ |
| `MFAS0007` | Assertions | Use Assert.NotNull for null comparisons | Warning | ✔️ |
| `MFAS0008` | Assertions | Do not use Assert.Null with value types | Error | ✔️ |
| `MFAS0009` | Assertions | Do not use Assert.NotNull with value types | Error | ✔️ |
| `MFAS0010` | Assertions | Do not use Assert.Same with value types | Error | ✔️ |
| `MFAS0011` | Assertions | Do not use Assert.NotSame with value types | Error | ✔️ |
<!-- analyzer-rules -->
