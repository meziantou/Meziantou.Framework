namespace Meziantou.Framework.Mail.Internals.Tnef;

/// <summary>The identifiers of the attributes of a TNEF stream ([MS-OXTNEF] 2.1.3.3): the type in the high word, the identifier in the low word.</summary>
internal static class TnefAttributes
{
    public const uint Signature = 0x223E9F78;
    public const byte MessageLevel = 0x01;
    public const byte AttachmentLevel = 0x02;

    public const uint From = 0x00008000;
    public const uint Subject = 0x00018004;
    public const uint DateSent = 0x00038005;
    public const uint DateReceived = 0x00038006;
    public const uint MessageClass = 0x00078008;
    public const uint Body = 0x0002800C;
    public const uint Priority = 0x0004800D;
    public const uint AttachData = 0x0006800F;
    public const uint AttachTitle = 0x00018010;
    public const uint AttachCreateDate = 0x00038012;
    public const uint AttachModifyDate = 0x00038013;
    public const uint DateModified = 0x00038020;
    public const uint AttachRenderData = 0x00069002;
    public const uint MessageProperties = 0x00069003;
    public const uint RecipientTable = 0x00069004;
    public const uint Attachment = 0x00069005;
    public const uint TnefVersion = 0x00089006;
    public const uint OemCodepage = 0x00069007;

    /// <summary>The interface of an object value that is an embedded message: IID_IMessage.</summary>
    public static readonly Guid MessageInterface = new("00020307-0000-0000-C000-000000000046");
}
