using System.Text;

namespace Meziantou.Framework.Email.Internals.Tnef;

/// <summary>A message read from a TNEF stream: the message of the stream, or an embedded message.</summary>
internal sealed class TnefMessageSource : MessageSource
{
    private readonly TnefContext _context;
    private readonly TnefItemData _data;
    private readonly bool _isAssociated;

    private TnefMessageSource(TnefContext context, TnefItemData data, bool isAssociated)
    {
        _context = context;
        _data = data;
        _isAssociated = isAssociated;
    }

    public override PstReadOptions Options => _context.Options;

    public override uint NodeId => 0;

    public override bool IsAssociated => _isAssociated;

    public static MapiMessage Load(TnefContext context, ReadOnlySpan<byte> data, int embeddingDepth)
    {
        MapiMessage.CheckDepth(context.Options, embeddingDepth);
        var item = TnefParser.Parse(data, context);
        var properties = TnefPropertySource.Create(context, item.Properties, GetEncoding(item.CodePage));
        var flags = (MapiMessageFlags)(properties.GetInt32(PropertyIds.MessageFlags) ?? 0);
        return MapiMessage.Create(new TnefMessageSource(context, item, flags.HasFlag(MapiMessageFlags.Associated)), properties, embeddingDepth);
    }

    public override List<MapiRecipient> LoadRecipients(MapiMessage message)
    {
        return _data.Recipients.ConvertAll(recipient => new MapiRecipient(TnefPropertySource.Create(_context, recipient, message.Properties.Encoding)));
    }

    public override List<MapiAttachment> LoadAttachments(MapiMessage message)
    {
        return _data.Attachments.ConvertAll(attachment => new MapiAttachment(message, new TnefAttachmentSource(_context, TnefPropertySource.Create(_context, attachment, message.Properties.Encoding))));
    }

    private static Encoding GetEncoding(int codePage)
    {
        try
        {
            return PstContext.GetEncoding(codePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return PstContext.GetEncoding(1252);
        }
    }
}
