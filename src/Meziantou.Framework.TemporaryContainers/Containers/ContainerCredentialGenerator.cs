using System.Security.Cryptography;

namespace Meziantou.Framework.TemporaryContainers;

internal static class ContainerCredentialGenerator
{
    private const string UppercaseCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LowercaseCharacters = "abcdefghijklmnopqrstuvwxyz";
    private const string DigitCharacters = "0123456789";
    private const string SymbolCharacters = "!@$%*+-_.?";
    private const string AllCharacters = UppercaseCharacters + LowercaseCharacters + DigitCharacters + SymbolCharacters;

    public static string GenerateStrongPassword(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 4);

        Span<char> password = stackalloc char[length];
        password[0] = UppercaseCharacters[RandomNumberGenerator.GetInt32(UppercaseCharacters.Length)];
        password[1] = LowercaseCharacters[RandomNumberGenerator.GetInt32(LowercaseCharacters.Length)];
        password[2] = DigitCharacters[RandomNumberGenerator.GetInt32(DigitCharacters.Length)];
        password[3] = SymbolCharacters[RandomNumberGenerator.GetInt32(SymbolCharacters.Length)];
        for (var i = 4; i < password.Length; i++)
        {
            password[i] = AllCharacters[RandomNumberGenerator.GetInt32(AllCharacters.Length)];
        }

        for (var i = password.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (password[i], password[j]) = (password[j], password[i]);
        }

        return new string(password);
    }
}
