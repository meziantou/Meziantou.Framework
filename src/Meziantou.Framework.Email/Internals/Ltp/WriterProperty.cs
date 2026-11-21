namespace Meziantou.Framework.Email.Internals.Ltp;

/// <summary>A property to write: its tag and its bytes, or a stream for a large value.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct WriterProperty(MapiPropertyTag Tag, byte[]? Value, Stream? Stream = null);
