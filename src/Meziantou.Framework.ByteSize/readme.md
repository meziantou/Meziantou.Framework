# Meziantou.Framework.ByteSize

`ByteSize` represent a value in the Byte unit. It can parse, display, and compare values. `kB`, `MB`, ... are powers of 1000, and `kiB`, `MiB`, ... are powers of 1024.

<!-- ref:../../tests/Meziantou.Framework.ByteSize.Tests/ByteSizeTests.cs;region=ByteSize_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// Create an instance of ByteSize
var size = new ByteSize(10); // 10 bytes
Assert.Equal(new ByteSize(10_000), ByteSize.FromKiloBytes(10));
Assert.Equal(new ByteSize(10_000_000_000), ByteSize.From(10, ByteSizeUnit.GigaByte));
Assert.Equal(ByteSize.FromMegaBytes(10), ByteSize.Parse("10MB", CultureInfo.InvariantCulture));
Assert.True(ByteSize.TryParse("10MB", out var parsedSize)); // Uses the current culture
Assert.True(ByteSize.TryParse("10MB", CultureInfo.InvariantCulture, out var parsedSizeInvariant));
Assert.Equal(ByteSize.FromMegaBytes(10), parsedSizeInvariant);

// Formatting
Assert.Equal("10B", size.ToString(CultureInfo.InvariantCulture)); // Automatically find the best unit
Assert.Equal("1.5MB", ByteSize.FromKiloBytes(1500).ToString("MB", CultureInfo.InvariantCulture)); // Display the value in megabytes
// Supports B, kB, kiB, MB, MiB, GB, GiB, TB, TiB, PB, PiB, EB, EiB

// Comparisons
var a = ByteSize.FromKiloBytes(1);
var b = ByteSize.FromMegaBytes(1);
Assert.False(a == b);
Assert.True(a != b);
Assert.True(a < b);
Assert.True(a <= b);
Assert.False(a > b);
Assert.False(a >= b);
```
<!-- endref -->