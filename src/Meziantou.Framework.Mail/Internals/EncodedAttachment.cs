using Meziantou.Framework.Mail.Internals.Ltp;

namespace Meziantou.Framework.Mail.Internals;

/// <summary>An attachment ready to write: its properties, and its embedded message.</summary>
internal sealed record EncodedAttachment(List<WriterProperty> Properties, EncodedItem? EmbeddedMessage);
