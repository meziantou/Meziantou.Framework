using System.Text;
using Meziantou.Framework.Mail.Internals.Cfb;

namespace Meziantou.Framework.Mail.Internals.Msg;

/// <summary>What the objects of an .msg file share: the options and the named properties of the file.</summary>
internal sealed class MsgContext
{
    public MsgContext(CompoundFile file, PstReadOptions options)
    {
        Options = options;
        var nameId = file.Root.GetChild(MsgNames.NameId);
        NameToIdMap = nameId is null
            ? NameToIdMap.Empty
            : NameToIdMap.FromStreams(
                ReadStream(nameId, NameToIdMap.GuidStreamId),
                ReadStream(nameId, NameToIdMap.EntryStreamId),
                ReadStream(nameId, NameToIdMap.StringStreamId));
    }

    public PstReadOptions Options { get; }

    public NameToIdMap NameToIdMap { get; }

    public Encoding DefaultEncoding { get; } = PstContext.GetEncoding(1252);

    private static byte[]? ReadStream(CfbEntry storage, ushort id)
    {
        return storage.GetChild(MsgNames.Stream(new PstPropertyTag(id, PstPropertyType.Binary)))?.ReadStream();
    }
}
