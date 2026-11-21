using Meziantou.Framework.Mail.Internals.Ltp;

namespace Meziantou.Framework.Mail.Internals;

/// <summary>An item ready to write: its properties, the properties of its recipients, and its attachments. The values are in memory.</summary>
internal sealed record EncodedItem(List<WriterProperty> Properties, List<List<WriterProperty>> Recipients, List<EncodedAttachment> Attachments);
