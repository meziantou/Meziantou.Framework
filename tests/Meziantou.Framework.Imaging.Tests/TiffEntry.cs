namespace Meziantou.Framework.Imaging.Tests;

/// <summary>One TIFF IFD entry used by <see cref="TiffBuilder"/>; the payload is produced for the requested byte order.</summary>
internal sealed record TiffEntry(ushort Tag, ushort Type, uint Count, Func<bool, byte[]> Payload);
