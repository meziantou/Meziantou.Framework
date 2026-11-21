using System.Text;

namespace Meziantou.Framework.Mail.Internals.Tnef;

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

    public static PstMessage Load(TnefContext context, ReadOnlySpan<byte> data, int embeddingDepth)
    {
        PstMessage.CheckDepth(context.Options, embeddingDepth);
        var item = TnefParser.Parse(data, context);
        var properties = TnefPropertySource.Create(context, item.Properties, GetEncoding(item.CodePage));
        var flags = (PstMessageFlags)(properties.GetInt32(PropertyIds.MessageFlags) ?? 0);
        return PstMessage.Create(new TnefMessageSource(context, item, flags.HasFlag(PstMessageFlags.Associated)), properties, embeddingDepth);
    }

    public override List<PstRecipient> LoadRecipients(PstMessage message)
    {
        return _data.Recipients.ConvertAll(recipient => new PstRecipient(TnefPropertySource.Create(_context, recipient, message.Properties.Encoding)));
    }

    public override List<PstAttachment> LoadAttachments(PstMessage message)
    {
        return _data.Attachments.ConvertAll(attachment => new PstAttachment(message, new TnefAttachmentSource(_context, TnefPropertySource.Create(_context, attachment, message.Properties.Encoding))));
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
