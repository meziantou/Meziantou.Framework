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
<!-- analyzer-rules -->
