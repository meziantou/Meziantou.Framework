# Meziantou.Framework.ByteSize

`ByteSize` represent a value in the Byte unit. It can parse, display, and compare values.

````c#
// Create an instance of ByteSize
var size = new ByteSize(10); // 10 bytes
_ = ByteSize.FromKiloBytes(10);
_ = ByteSize.From(10, ByteSizeUnit.GigaByte);
_ = ByteSize.Parse("10MB", CultureInfo.InvariantCulture);
_ = ByteSize.TryParse("10MB", out var parsedSize);
_ = ByteSize.TryParse("10MB", CultureInfo.InvariantCulture, out var parsedSizeInvariant);

// Formatting
size.ToString(); // Automatically find the best unit
size.ToString("MB"); // Display the value in megabytes
                     // Supports B, kB, kiB, MB, MiB, GB, GiB, TB, TiB, PB, PiB, EB, EiB

// Comparisons
var a = ByteSize.FromKiloBytes(1);
var b = ByteSize.FromMegaBytes(1);
_ = a == b;
_ = a != b;
_ = a < b;
_ = a <= b;
_ = a > b;
_ = a >= b;
````