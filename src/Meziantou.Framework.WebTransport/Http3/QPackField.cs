namespace Meziantou.Framework.WebTransport.Http3;

internal readonly record struct QPackField(string Name, string Value)
{
    // The size of a field for SETTINGS_MAX_FIELD_SECTION_SIZE (RFC 9114, section 4.2.2)
    public int Size => Name.Length + Value.Length + 32;
}
