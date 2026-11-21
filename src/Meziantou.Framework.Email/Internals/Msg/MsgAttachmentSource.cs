using Meziantou.Framework.Email.Internals.Cfb;

namespace Meziantou.Framework.Email.Internals.Msg;

/// <summary>An attachment stored in a storage of an .msg file.</summary>
internal sealed class MsgAttachmentSource : AttachmentSource
{
    private readonly MsgContext _context;
    private readonly CfbEntry _storage;

    public MsgAttachmentSource(MsgContext context, CfbEntry storage, System.Text.Encoding defaultEncoding)
    {
        _context = context;
        _storage = storage;
        Properties = MsgPropertySource.Read(context, storage, 8, defaultEncoding);
    }

    public override MapiPropertyCollection Properties { get; }

    public override MapiMessage? LoadEmbeddedMessage(MapiProperty reference, int embeddingDepth)
    {
        // [MS-OXMSG] 2.2.2.1: the embedded message is in a storage named after the property PidTagAttachDataObject
        var storage = _storage.GetChild(MsgNames.EmbeddedMessage);
        if (storage is not { IsStorage: true })
        {
            throw new InvalidEmailDataException("The storage of the embedded message of an attachment does not exist.");
        }

        return MsgMessageSource.Load(_context, storage, embeddingDepth, Properties.Encoding);
    }
}
