using System.Security.Cryptography;

namespace Meziantou.Framework.Ntp;

/// <summary>AES-CMAC, as defined by RFC 4493.</summary>
internal static class NtpAesCmac
{
    public const int Size = 16;

    private const int BlockSize = 16;
    private const byte Rb = 0x87;

    public static void Compute(ReadOnlySpan<byte> key, ReadOnlySpan<byte> message, Span<byte> destination)
    {
        using var aes = Aes.Create();
        aes.Key = key.ToArray();

        Span<byte> subkey1 = stackalloc byte[BlockSize];
        Span<byte> subkey2 = stackalloc byte[BlockSize];
        Span<byte> input = stackalloc byte[BlockSize];
        Span<byte> state = stackalloc byte[BlockSize];

        try
        {
            input.Clear();
            aes.EncryptEcb(input, state, PaddingMode.None);
            DoubleBlock(state, subkey1);
            DoubleBlock(subkey1, subkey2);

            var blockCount = (message.Length + BlockSize - 1) / BlockSize;
            bool isLastBlockComplete;
            if (blockCount is 0)
            {
                blockCount = 1;
                isLastBlockComplete = false;
            }
            else
            {
                isLastBlockComplete = message.Length % BlockSize is 0;
            }

            state.Clear();
            for (var i = 0; i < blockCount - 1; i++)
            {
                Xor(state, message.Slice(i * BlockSize, BlockSize), input);
                aes.EncryptEcb(input, state, PaddingMode.None);
            }

            var lastBlock = message[((blockCount - 1) * BlockSize)..];
            if (isLastBlockComplete)
            {
                Xor(lastBlock, subkey1, input);
            }
            else
            {
                // Padding: the remaining bytes, then a single 1 bit, then zeros
                input.Clear();
                lastBlock.CopyTo(input);
                input[lastBlock.Length] = 0x80;
                Xor(input, subkey2, input);
            }

            Xor(input, state, input);
            aes.EncryptEcb(input, destination[..Size], PaddingMode.None);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(subkey1);
            CryptographicOperations.ZeroMemory(subkey2);
            CryptographicOperations.ZeroMemory(input);
            CryptographicOperations.ZeroMemory(state);
        }
    }

    /// <summary>Multiplies a block by x in GF(2^128): a left shift, reduced by Rb on overflow.</summary>
    private static void DoubleBlock(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var overflow = (input[0] & 0x80) is not 0;
        var carry = 0;
        for (var i = BlockSize - 1; i >= 0; i--)
        {
            var value = input[i];
            output[i] = (byte)((value << 1) | carry);
            carry = value >> 7;
        }

        if (overflow)
        {
            output[BlockSize - 1] ^= Rb;
        }
    }

    private static void Xor(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, Span<byte> destination)
    {
        for (var i = 0; i < BlockSize; i++)
        {
            destination[i] = (byte)(left[i] ^ right[i]);
        }
    }
}
