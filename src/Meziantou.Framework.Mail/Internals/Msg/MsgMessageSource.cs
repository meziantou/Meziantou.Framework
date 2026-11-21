using Meziantou.Framework.Mail.Internals.Cfb;

namespace Meziantou.Framework.Mail.Internals.Msg;

/// <summary>A message stored in the storages of an .msg file: the root storage, or the storage of an embedded message.</summary>
internal sealed class MsgMessageSource : MessageSource
{
    private readonly MsgContext _context;
    private readonly CfbEntry _storage;
    private readonly bool _isAssociated;

    private MsgMessageSource(MsgContext context, CfbEntry storage, bool isAssociated)
    {
        _context = context;
        _storage = storage;
        _isAssociated = isAssociated;
    }

    public override PstReadOptions Options => _context.Options;

    public override uint NodeId => 0;

    public override bool IsAssociated => _isAssociated;

    public static PstMessage Load(MsgContext context, CfbEntry storage, int embeddingDepth, System.Text.Encoding defaultEncoding)
    {
        PstMessage.CheckDepth(context.Options, embeddingDepth);
        var properties = MsgPropertySource.Read(context, storage, embeddingDepth == 0 ? 32 : 24, defaultEncoding);
        var flags = (PstMessageFlags)(properties.GetInt32(PropertyIds.MessageFlags) ?? 0);
        return PstMessage.Create(new MsgMessageSource(context, storage, flags.HasFlag(PstMessageFlags.Associated)), properties, embeddingDepth);
    }

    public override List<PstRecipient> LoadRecipients(PstMessage message)
    {
        return [.. GetStorages(MsgNames.RecipientPrefix).Select(storage => new PstRecipient(MsgPropertySource.Read(_context, storage, 8, message.Properties.Encoding)))];
    }

    public override List<PstAttachment> LoadAttachments(PstMessage message)
    {
        return [.. GetStorages(MsgNames.AttachmentPrefix).Select(storage => new PstAttachment(message, new MsgAttachmentSource(_context, storage, message.Properties.Encoding)))];
    }

    /// <summary>Returns the storages of the recipients or of the attachments, in the order of their numbers.</summary>
    private IEnumerable<CfbEntry> GetStorages(string prefix)
    {
        return _storage.Children.Values
            .Where(entry => entry.IsStorage && entry.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);
    }
}
