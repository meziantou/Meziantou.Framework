using Meziantou.Framework.Email.Internals.Cfb;

namespace Meziantou.Framework.Email.Internals.Msg;

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

    public static MapiMessage Load(MsgContext context, CfbEntry storage, int embeddingDepth, System.Text.Encoding defaultEncoding)
    {
        MapiMessage.CheckDepth(context.Options, embeddingDepth);
        var properties = MsgPropertySource.Read(context, storage, embeddingDepth == 0 ? 32 : 24, defaultEncoding);
        var flags = (MapiMessageFlags)(properties.GetInt32(PropertyIds.MessageFlags) ?? 0);
        return MapiMessage.Create(new MsgMessageSource(context, storage, flags.HasFlag(MapiMessageFlags.Associated)), properties, embeddingDepth);
    }

    public override List<MapiRecipient> LoadRecipients(MapiMessage message)
    {
        return [.. GetStorages(MsgNames.RecipientPrefix).Select(storage => new MapiRecipient(MsgPropertySource.Read(_context, storage, 8, message.Properties.Encoding)))];
    }

    public override List<MapiAttachment> LoadAttachments(MapiMessage message)
    {
        return [.. GetStorages(MsgNames.AttachmentPrefix).Select(storage => new MapiAttachment(message, new MsgAttachmentSource(_context, storage, message.Properties.Encoding)))];
    }

    /// <summary>Returns the storages of the recipients or of the attachments, in the order of their numbers.</summary>
    private IEnumerable<CfbEntry> GetStorages(string prefix)
    {
        return _storage.Children.Values
            .Where(entry => entry.IsStorage && entry.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);
    }
}
