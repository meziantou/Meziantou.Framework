namespace Meziantou.Framework.Email.Internals.Tnef;

/// <summary>An attachment read from a TNEF stream.</summary>
internal sealed class TnefAttachmentSource : AttachmentSource
{
    private readonly TnefContext _context;

    public TnefAttachmentSource(TnefContext context, MapiPropertyCollection properties)
    {
        _context = context;
        Properties = properties;
    }

    public override MapiPropertyCollection Properties { get; }

    public override MapiMessage? LoadEmbeddedMessage(MapiProperty reference, int embeddingDepth)
    {
        // [MS-OXTNEF] 2.1.3.4.1: the value of an object is the identifier of its interface, then its content; a message is a TNEF stream
        var value = reference.GetBytes();
        if (value.Length < 16 || new Guid(value.AsSpan(0, 16)) != TnefAttributes.MessageInterface)
        {
            return null;
        }

        return TnefMessageSource.Load(_context, value.AsSpan(16), embeddingDepth);
    }
}
