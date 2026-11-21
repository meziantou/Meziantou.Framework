using Meziantou.Framework.Email.Internals.Ltp;

namespace Meziantou.Framework.Email.Internals;

/// <summary>An attachment ready to write: its properties, and its embedded message.</summary>
internal sealed record EncodedAttachment(List<WriterProperty> Properties, EncodedItem? EmbeddedMessage);
