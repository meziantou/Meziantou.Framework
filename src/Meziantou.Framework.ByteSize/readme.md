# Meziantou.Framework.ByteSize

`ByteSize` represent a value in the Byte unit. It can parse, display, and compare values.

````c#
// Create an instance of ByteSize
var size = new ByteSize(10); // 10 bytes
_ = ByteSize.FromKiloByte(10);
_ = ByteSize.From(10, ByteSizeUnit.GigaByte);
_ = ByteSize.Parse("10MB");
var parsed = ByteSize.TryParse("10MB", out var size);
var parsed = ByteSize.TryParse("10MB", CultureInfo.InvariantCulture, out var size);

// Formatting
size.ToString(); // Automatically find the best unit
size.ToString("MB"); // Display the value in megabytes
                     // Supports B, kB, kiB, MB, MiB, GB, GiB, TB, TiB, EB, EiB

// Comparisons
ByteSize a;
ByteSize b;
_ = a == b;
_ = a != b;
_ = a < b;
_ = a <= b;
_ = a > b;
_ = a >= b;
````