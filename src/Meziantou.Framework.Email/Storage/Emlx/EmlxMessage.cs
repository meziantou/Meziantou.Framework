namespace Meziantou.Framework.Email.Storage;

/// <summary>A message of Apple Mail read from an .emlx file: the message, and the properties Apple Mail stores after it.</summary>
public sealed class EmlxMessage
{
    internal EmlxMessage(MimeMessage message, IReadOnlyDictionary<string, object?> properties, bool isPartial)
    {
        Message = message;
        Properties = properties;
        IsPartial = isPartial;
    }

    /// <summary>Gets the message.</summary>
    public MimeMessage Message { get; }

    /// <summary>Gets the properties of the property list after the message, such as <c>flags</c>, <c>date-received</c> or <c>remote-id</c>: <see cref="string"/>, <see cref="long"/>, <see cref="double"/>, <see cref="bool"/>, <see cref="DateTimeOffset"/>, byte arrays, lists and dictionaries.</summary>
    public IReadOnlyDictionary<string, object?> Properties { get; }

    /// <summary>Gets whether the file is a partial message (<c>.partial.emlx</c>): Apple Mail stores the content of its large attachments in other files, which the message does not contain.</summary>
    public bool IsPartial { get; }

    /// <summary>Gets the value of the <c>flags</c> property, with all its bits.</summary>
    public long RawFlags => Properties.GetValueOrDefault("flags") switch
    {
        long value => value,
        double value => (long)value,
        _ => 0,
    };

    /// <summary>Gets the flags of the message, the low bits of the <c>flags</c> property.</summary>
    public EmlxFlags Flags => (EmlxFlags)(RawFlags & 0x3FF);

    /// <summary>Gets the number of attachments, which Apple Mail stores in the bits 10 to 15 of the <c>flags</c> property.</summary>
    public int AttachmentCount => (int)((RawFlags >> 10) & 0x3F);

    /// <summary>Gets the date the message was received, from the <c>date-received</c> property (seconds since 1970).</summary>
    public DateTimeOffset? DateReceived => Properties.GetValueOrDefault("date-received") switch
    {
        long seconds when seconds is > -62135596800 and < 253402300800 => DateTimeOffset.FromUnixTimeSeconds(seconds),
        double seconds when seconds is > -62135596800 and < 253402300800 => DateTimeOffset.FromUnixTimeSeconds((long)seconds),
        DateTimeOffset date => date,
        _ => null,
    };
}
