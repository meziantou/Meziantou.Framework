namespace Meziantou.Framework.NodeJs.Internal;

/// <summary>Reads the result of a call from its UTF-8 JSON representation.</summary>
internal delegate T ResultReader<T>(ReadOnlySpan<byte> utf8Json);
