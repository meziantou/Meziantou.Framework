using System.Runtime.CompilerServices;

// The tests and the fuzz targets check every SIMD path against the scalar path
[assembly: InternalsVisibleTo("Meziantou.Framework.Csv.Tests")]
[assembly: InternalsVisibleTo("Meziantou.Framework.Csv.Fuzz")]

// The stack buffers are written before they are read: zeroing them is not needed
[module: SkipLocalsInit]
