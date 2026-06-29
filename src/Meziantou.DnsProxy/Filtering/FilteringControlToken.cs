using System.Security.Cryptography;
using System.Text;

namespace Meziantou.DnsProxy.Filtering;

internal sealed class FilteringControlToken
{
    public const string FormFieldName = "controlToken";

    private readonly byte[] _tokenBytes;
    private readonly string _value;

    public FilteringControlToken()
    {
        _value = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _tokenBytes = Encoding.ASCII.GetBytes(_value);
    }

    public string Value => _value;

    public bool IsValid(string? value)
    {
        if (value is null || value.Length != _value.Length)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[_tokenBytes.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (character > sbyte.MaxValue)
            {
                return false;
            }

            bytes[i] = (byte)character;
        }

        return CryptographicOperations.FixedTimeEquals(bytes, _tokenBytes);
    }
}
