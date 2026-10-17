using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>
/// ML-KEM, the Module-Lattice-Based Key-Encapsulation Mechanism (FIPS 203), with the parameter sets ML-KEM-512,
/// ML-KEM-768 and ML-KEM-1024. The base class library cannot be used: its ML-KEM type is not supported on every platform
/// (e.g. macOS).
/// </summary>
/// <remarks>
/// The coefficients of the polynomials are stored as integers in [0, q). The polynomials in the NTT domain are only
/// computed on the fly: the matrix Â is sampled one entry at a time instead of being stored.
/// </remarks>
internal static class MlKem
{
    /// <summary>The length of the seed d || z of ML-KEM.KeyGen_internal (FIPS 203 §6.1).</summary>
    public const int SeedLength = 64;

    /// <summary>The length of the shared secret key K (FIPS 203 §2.4).</summary>
    public const int SharedKeyLength = 32;

    private const int N = 256;
    private const int Q = 3329;

    // The coefficient bytes of a polynomial encoded with ByteEncode_12 (FIPS 203 §4.2.1)
    private const int EncodedPolynomialLength = 384;

    // FIPS 203 §4.3: ζ^BitRev7(i) mod q for the NTT, and ζ^(2·BitRev7(i) + 1) mod q for MultiplyNTTs (ζ = 17)
    private static readonly int[] Zetas = ComputeZetas(gammas: false);
    private static readonly int[] Gammas = ComputeZetas(gammas: true);

    /// <summary>Gets the length of the encapsulation key of a parameter set (FIPS 203 §8, table 3).</summary>
    public static int GetEncapsulationKeyLength(int parameterSet) => GetParameters(parameterSet).EncapsulationKeyLength;

    /// <summary>Gets the length of the decapsulation key of a parameter set (FIPS 203 §8, table 3).</summary>
    public static int GetDecapsulationKeyLength(int parameterSet) => GetParameters(parameterSet).DecapsulationKeyLength;

    /// <summary>Gets the length of the ciphertext of a parameter set (FIPS 203 §8, table 3).</summary>
    public static int GetCiphertextLength(int parameterSet) => GetParameters(parameterSet).CiphertextLength;

    /// <summary>ML-KEM.KeyGen_internal(d, z) (FIPS 203 §6.1, algorithm 16), from the 64-byte seed d || z.</summary>
    /// <param name="parameterSet">512, 768 or 1024.</param>
    public static (byte[] EncapsulationKey, byte[] DecapsulationKey) GenerateKeyFromSeed(int parameterSet, ReadOnlySpan<byte> seed)
    {
        var parameters = GetParameters(parameterSet);
        if (seed.Length != SeedLength)
            throw new ArgumentException("The seed must be 64 bytes long.", nameof(seed));

        var encapsulationKey = new byte[parameters.EncapsulationKeyLength];
        var decapsulationKey = new byte[parameters.DecapsulationKeyLength];
        var k = parameters.K;

        // K-PKE.KeyGen(d) (algorithm 13): the encryption key is the encapsulation key, and the decryption key is the start
        // of the decapsulation key
        KeyGen(parameters, seed[..32], encapsulationKey, decapsulationKey.AsSpan(0, EncodedPolynomialLength * k));

        // dk = dk_PKE || ek || H(ek) || z
        encapsulationKey.CopyTo(decapsulationKey.AsSpan(EncodedPolynomialLength * k));
        Keccak.Sha3(256, encapsulationKey, decapsulationKey.AsSpan((2 * EncodedPolynomialLength * k) + 32, 32));
        seed[32..].CopyTo(decapsulationKey.AsSpan((2 * EncodedPolynomialLength * k) + 64));
        return (encapsulationKey, decapsulationKey);
    }

    /// <summary>ML-KEM.Encaps(ek) (FIPS 203 §7.2, algorithm 20), with a random message m.</summary>
    /// <exception cref="CryptographicException">The encapsulation key does not pass the input checks of FIPS 203 §7.2.</exception>
    public static (byte[] SharedKey, byte[] Ciphertext) Encapsulate(int parameterSet, ReadOnlySpan<byte> encapsulationKey)
    {
        Span<byte> message = stackalloc byte[32];
        RandomNumberGenerator.Fill(message);
        var result = Encapsulate(parameterSet, encapsulationKey, message);
        CryptographicOperations.ZeroMemory(message);
        return result;
    }

    /// <summary>
    /// ML-KEM.Encaps_internal(ek, m) (FIPS 203 §6.2, algorithm 17), after the input checks of ML-KEM.Encaps (§7.2).
    /// </summary>
    /// <param name="message">The 32-byte message m, which must be random outside of the tests.</param>
    /// <exception cref="CryptographicException">The encapsulation key does not pass the input checks of FIPS 203 §7.2.</exception>
    internal static (byte[] SharedKey, byte[] Ciphertext) Encapsulate(int parameterSet, ReadOnlySpan<byte> encapsulationKey, ReadOnlySpan<byte> message)
    {
        var parameters = GetParameters(parameterSet);
        if (message.Length != 32)
            throw new ArgumentException("The message must be 32 bytes long.", nameof(message));

        if (!IsValidEncapsulationKey(parameters, encapsulationKey))
            throw new CryptographicException("The encapsulation key is invalid.");

        // (K, r) = G(m || H(ek))
        Span<byte> input = stackalloc byte[64];
        message.CopyTo(input);
        Keccak.Sha3(256, encapsulationKey, input[32..]);
        Span<byte> keyAndRandomness = stackalloc byte[64];
        Keccak.Sha3(512, input, keyAndRandomness);

        var ciphertext = new byte[parameters.CiphertextLength];
        Encrypt(parameters, encapsulationKey, message, keyAndRandomness[32..], ciphertext);
        var sharedKey = keyAndRandomness[..32].ToArray();
        CryptographicOperations.ZeroMemory(keyAndRandomness);
        return (sharedKey, ciphertext);
    }

    /// <summary>ML-KEM.Decaps(dk, c) (FIPS 203 §7.3, algorithm 21), with its input checks.</summary>
    /// <exception cref="CryptographicException">The decapsulation key or the ciphertext does not pass the input checks of FIPS 203 §7.3.</exception>
    public static byte[] Decapsulate(int parameterSet, ReadOnlySpan<byte> decapsulationKey, ReadOnlySpan<byte> ciphertext)
    {
        var parameters = GetParameters(parameterSet);
        if (ciphertext.Length != parameters.CiphertextLength)
            throw new CryptographicException("The ciphertext length is invalid.");

        if (!IsValidDecapsulationKey(parameters, decapsulationKey))
            throw new CryptographicException("The decapsulation key is invalid.");

        // ML-KEM.Decaps_internal (algorithm 18)
        var k = parameters.K;
        var decryptionKey = decapsulationKey[..(EncodedPolynomialLength * k)];
        var encryptionKey = decapsulationKey.Slice(EncodedPolynomialLength * k, (EncodedPolynomialLength * k) + 32);
        var hash = decapsulationKey.Slice((2 * EncodedPolynomialLength * k) + 32, 32);
        var z = decapsulationKey.Slice((2 * EncodedPolynomialLength * k) + 64, 32);

        // m' = K-PKE.Decrypt(dk_PKE, c), (K', r') = G(m' || h)
        Span<byte> input = stackalloc byte[64];
        Decrypt(parameters, decryptionKey, ciphertext, input[..32]);
        hash.CopyTo(input[32..]);
        Span<byte> keyAndRandomness = stackalloc byte[64];
        Keccak.Sha3(512, input, keyAndRandomness);

        // K̄ = J(z || c)
        Span<byte> rejectionKey = stackalloc byte[SharedKeyLength];
        var sponge = KeccakSponge.CreateShake256();
        sponge.Absorb(z);
        sponge.Absorb(ciphertext);
        sponge.Squeeze(rejectionKey);
        sponge.Clear();

        // c' = K-PKE.Encrypt(ek_PKE, m', r'); if c ≠ c' then K' = K̄ (implicit rejection, in constant time)
        var reencrypted = new byte[parameters.CiphertextLength];
        Encrypt(parameters, encryptionKey, input[..32], keyAndRandomness[32..], reencrypted);
        var difference = 0;
        for (var i = 0; i < reencrypted.Length; i++)
        {
            difference |= reencrypted[i] ^ ciphertext[i];
        }

        // mask is 0xFF when the ciphertexts are equal, 0 otherwise
        var mask = (byte)((difference - 1) >> 31);
        var sharedKey = new byte[SharedKeyLength];
        for (var i = 0; i < SharedKeyLength; i++)
        {
            sharedKey[i] = (byte)((keyAndRandomness[i] & mask) | (rejectionKey[i] & ~mask));
        }

        CryptographicOperations.ZeroMemory(input);
        CryptographicOperations.ZeroMemory(keyAndRandomness);
        CryptographicOperations.ZeroMemory(rejectionKey);
        return sharedKey;
    }

    /// <summary>
    /// The encapsulation key check of ML-KEM.Encaps (FIPS 203 §7.2): the length is 384k + 32 bytes (type check) and
    /// ByteEncode_12(ByteDecode_12(ek[0 : 384k])) = ek[0 : 384k], that is every coefficient is lower than q (modulus check).
    /// </summary>
    public static bool IsValidEncapsulationKey(int parameterSet, ReadOnlySpan<byte> encapsulationKey)
    {
        return IsValidEncapsulationKey(GetParameters(parameterSet), encapsulationKey);
    }

    /// <summary>
    /// The decapsulation key check of ML-KEM.Decaps (FIPS 203 §7.3): the length is 768k + 96 bytes (type check) and
    /// H(dk[384k : 768k + 32]) = dk[768k + 32 : 768k + 64] (hash check).
    /// </summary>
    public static bool IsValidDecapsulationKey(int parameterSet, ReadOnlySpan<byte> decapsulationKey)
    {
        return IsValidDecapsulationKey(GetParameters(parameterSet), decapsulationKey);
    }

    private static bool IsValidEncapsulationKey(Parameters parameters, ReadOnlySpan<byte> encapsulationKey)
    {
        if (encapsulationKey.Length != parameters.EncapsulationKeyLength)
            return false;

        // Two 12-bit coefficients are encoded in 3 bytes (little-endian bit order)
        var encoded = encapsulationKey[..(EncodedPolynomialLength * parameters.K)];
        for (var i = 0; i < encoded.Length; i += 3)
        {
            var first = encoded[i] | ((encoded[i + 1] & 0x0F) << 8);
            var second = (encoded[i + 1] >> 4) | (encoded[i + 2] << 4);
            if (first >= Q || second >= Q)
                return false;
        }

        return true;
    }

    private static bool IsValidDecapsulationKey(Parameters parameters, ReadOnlySpan<byte> decapsulationKey)
    {
        if (decapsulationKey.Length != parameters.DecapsulationKeyLength)
            return false;

        var k = parameters.K;
        Span<byte> hash = stackalloc byte[32];
        Keccak.Sha3(256, decapsulationKey.Slice(EncodedPolynomialLength * k, (EncodedPolynomialLength * k) + 32), hash);
        return CryptographicOperations.FixedTimeEquals(hash, decapsulationKey.Slice((2 * EncodedPolynomialLength * k) + 32, 32));
    }

    /// <summary>K-PKE.KeyGen(d) (FIPS 203 §5.1, algorithm 13).</summary>
    private static void KeyGen(Parameters parameters, ReadOnlySpan<byte> d, Span<byte> encryptionKey, Span<byte> decryptionKey)
    {
        var k = parameters.K;

        // (ρ, σ) = G(d || k)
        Span<byte> input = stackalloc byte[33];
        d.CopyTo(input);
        input[32] = (byte)k;
        Span<byte> rhoSigma = stackalloc byte[64];
        Keccak.Sha3(512, input, rhoSigma);
        var rho = rhoSigma[..32];
        var sigma = rhoSigma[32..];

        Span<int> s = stackalloc int[k * N];
        Span<int> t = stackalloc int[k * N];
        Span<int> entry = stackalloc int[N];
        byte nonce = 0;
        for (var i = 0; i < k; i++)
        {
            var polynomial = s.Slice(i * N, N);
            SamplePolyCbd(parameters.Eta1, sigma, nonce++, polynomial);
            Ntt(polynomial);
        }

        // t̂ = Â ∘ ŝ + ê, with Â[i, j] = SampleNTT(ρ || j || i)
        for (var i = 0; i < k; i++)
        {
            var polynomial = t.Slice(i * N, N);
            SamplePolyCbd(parameters.Eta1, sigma, nonce++, polynomial);
            Ntt(polynomial);
            for (var j = 0; j < k; j++)
            {
                SampleNtt(rho, (byte)j, (byte)i, entry);
                MultiplyNttsAndAdd(entry, s.Slice(j * N, N), polynomial);
            }
        }

        // ek_PKE = ByteEncode_12(t̂) || ρ, dk_PKE = ByteEncode_12(ŝ)
        for (var i = 0; i < k; i++)
        {
            ByteEncode(12, t.Slice(i * N, N), encryptionKey.Slice(i * EncodedPolynomialLength, EncodedPolynomialLength));
            ByteEncode(12, s.Slice(i * N, N), decryptionKey.Slice(i * EncodedPolynomialLength, EncodedPolynomialLength));
        }

        rho.CopyTo(encryptionKey[(k * EncodedPolynomialLength)..]);
        CryptographicOperations.ZeroMemory(rhoSigma);
        s.Clear();
    }

    /// <summary>K-PKE.Encrypt(ek_PKE, m, r) (FIPS 203 §5.2, algorithm 14).</summary>
    private static void Encrypt(Parameters parameters, ReadOnlySpan<byte> encryptionKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> randomness, Span<byte> ciphertext)
    {
        var k = parameters.K;
        Span<int> tHat = stackalloc int[k * N];
        for (var i = 0; i < k; i++)
        {
            ByteDecode(12, encryptionKey.Slice(i * EncodedPolynomialLength, EncodedPolynomialLength), tHat.Slice(i * N, N));
        }

        var rho = encryptionKey.Slice(k * EncodedPolynomialLength, 32);
        Span<int> y = stackalloc int[k * N];
        Span<int> entry = stackalloc int[N];
        Span<int> accumulator = stackalloc int[N];
        Span<int> error = stackalloc int[N];

        byte nonce = 0;
        for (var i = 0; i < k; i++)
        {
            var polynomial = y.Slice(i * N, N);
            SamplePolyCbd(parameters.Eta1, randomness, nonce++, polynomial);
            Ntt(polynomial);
        }

        // u = NTT⁻¹(Âᵀ ∘ ŷ) + e₁, with Âᵀ[i, j] = Â[j, i] = SampleNTT(ρ || i || j); c₁ = ByteEncode_du(Compress_du(u))
        var c1Length = 32 * parameters.Du;
        for (var i = 0; i < k; i++)
        {
            accumulator.Clear();
            for (var j = 0; j < k; j++)
            {
                SampleNtt(rho, (byte)i, (byte)j, entry);
                MultiplyNttsAndAdd(entry, y.Slice(j * N, N), accumulator);
            }

            InverseNtt(accumulator);
            SamplePolyCbd(parameters.Eta2, randomness, nonce++, error);
            for (var n = 0; n < N; n++)
            {
                accumulator[n] = Compress(parameters.Du, (accumulator[n] + error[n]) % Q);
            }

            ByteEncode(parameters.Du, accumulator, ciphertext.Slice(i * c1Length, c1Length));
        }

        // v = NTT⁻¹(t̂ᵀ ∘ ŷ) + e₂ + μ, with μ = Decompress_1(ByteDecode_1(m)); c₂ = ByteEncode_dv(Compress_dv(v))
        accumulator.Clear();
        for (var j = 0; j < k; j++)
        {
            MultiplyNttsAndAdd(tHat.Slice(j * N, N), y.Slice(j * N, N), accumulator);
        }

        InverseNtt(accumulator);
        SamplePolyCbd(parameters.Eta2, randomness, nonce, error);
        for (var n = 0; n < N; n++)
        {
            var bit = (message[n >> 3] >> (n & 7)) & 1;
            accumulator[n] = Compress(parameters.Dv, (accumulator[n] + error[n] + (bit * ((Q + 1) / 2))) % Q);
        }

        ByteEncode(parameters.Dv, accumulator, ciphertext.Slice(k * c1Length, 32 * parameters.Dv));
        y.Clear();
        error.Clear();
    }

    /// <summary>K-PKE.Decrypt(dk_PKE, c) (FIPS 203 §5.3, algorithm 15).</summary>
    private static void Decrypt(Parameters parameters, ReadOnlySpan<byte> decryptionKey, ReadOnlySpan<byte> ciphertext, Span<byte> message)
    {
        var k = parameters.K;
        var c1Length = 32 * parameters.Du;
        Span<int> u = stackalloc int[N];
        Span<int> s = stackalloc int[N];
        Span<int> accumulator = stackalloc int[N];
        accumulator.Clear();

        // w = v' - NTT⁻¹(ŝᵀ ∘ NTT(u')), with u' = Decompress_du(ByteDecode_du(c₁)) and ŝ = ByteDecode_12(dk_PKE)
        for (var i = 0; i < k; i++)
        {
            ByteDecode(parameters.Du, ciphertext.Slice(i * c1Length, c1Length), u);
            for (var n = 0; n < N; n++)
            {
                u[n] = Decompress(parameters.Du, u[n]);
            }

            Ntt(u);
            ByteDecode(12, decryptionKey.Slice(i * EncodedPolynomialLength, EncodedPolynomialLength), s);
            MultiplyNttsAndAdd(s, u, accumulator);
        }

        InverseNtt(accumulator);

        // v' = Decompress_dv(ByteDecode_dv(c₂)), m = ByteEncode_1(Compress_1(w))
        ByteDecode(parameters.Dv, ciphertext.Slice(k * c1Length, 32 * parameters.Dv), u);
        message.Clear();
        for (var n = 0; n < N; n++)
        {
            var w = Decompress(parameters.Dv, u[n]) - accumulator[n] + Q;
            message[n >> 3] |= (byte)(Compress(1, w % Q) << (n & 7));
        }

        s.Clear();
        accumulator.Clear();
    }

    /// <summary>SampleNTT(ρ || x || y) (FIPS 203 §4.2.2, algorithm 7): rejection sampling from SHAKE128.</summary>
    private static void SampleNtt(ReadOnlySpan<byte> rho, byte x, byte y, Span<int> output)
    {
        var sponge = KeccakSponge.CreateShake128();
        sponge.Absorb(rho);
        sponge.Absorb(x);
        sponge.Absorb(y);

        // A block of SHAKE128 contains 56 groups of 3 bytes
        Span<byte> block = stackalloc byte[KeccakSponge.Rate128];
        var j = 0;
        while (true)
        {
            sponge.Squeeze(block);
            for (var i = 0; i < block.Length; i += 3)
            {
                var d1 = block[i] | ((block[i + 1] & 0x0F) << 8);
                var d2 = (block[i + 1] >> 4) | (block[i + 2] << 4);
                if (d1 < Q)
                {
                    output[j++] = d1;
                    if (j == N)
                        return;
                }

                if (d2 < Q)
                {
                    output[j++] = d2;
                    if (j == N)
                        return;
                }
            }
        }
    }

    /// <summary>
    /// SamplePolyCBD_η(PRF_η(s, b)) (FIPS 203 §4.2.2, algorithm 8, and §4.1): the centered binomial distribution, from the
    /// 64η bytes of SHAKE256(s || b).
    /// </summary>
    private static void SamplePolyCbd(int eta, ReadOnlySpan<byte> seed, byte nonce, Span<int> output)
    {
        Span<byte> bytes = stackalloc byte[64 * 3];
        bytes = bytes[..(64 * eta)];
        var sponge = KeccakSponge.CreateShake256();
        sponge.Absorb(seed);
        sponge.Absorb(nonce);
        sponge.Squeeze(bytes);
        sponge.Clear();

        var bit = 0;
        for (var i = 0; i < N; i++)
        {
            var x = 0;
            for (var j = 0; j < eta; j++, bit++)
            {
                x += (bytes[bit >> 3] >> (bit & 7)) & 1;
            }

            var y = 0;
            for (var j = 0; j < eta; j++, bit++)
            {
                y += (bytes[bit >> 3] >> (bit & 7)) & 1;
            }

            var value = x - y;
            output[i] = value + (Q & (value >> 31));
        }

        CryptographicOperations.ZeroMemory(bytes);
    }

    /// <summary>NTT(f) (FIPS 203 §4.3, algorithm 9), in place.</summary>
    private static void Ntt(Span<int> f)
    {
        Debug.Assert(f.Length == N);
        var zetas = Zetas;
        var i = 1;
        for (var length = 128; length >= 2; length /= 2)
        {
            for (var start = 0; start < N; start += 2 * length)
            {
                var zeta = zetas[i++];
                for (var j = start; j < start + length; j++)
                {
                    var t = zeta * f[j + length] % Q;
                    f[j + length] = (f[j] - t + Q) % Q;
                    f[j] = (f[j] + t) % Q;
                }
            }
        }
    }

    /// <summary>NTT⁻¹(f̂) (FIPS 203 §4.3, algorithm 10), in place.</summary>
    private static void InverseNtt(Span<int> f)
    {
        Debug.Assert(f.Length == N);
        var zetas = Zetas;
        var i = 127;
        for (var length = 2; length <= 128; length *= 2)
        {
            for (var start = 0; start < N; start += 2 * length)
            {
                var zeta = zetas[i--];
                for (var j = start; j < start + length; j++)
                {
                    var t = f[j];
                    f[j] = (t + f[j + length]) % Q;
                    f[j + length] = zeta * (f[j + length] - t + Q) % Q;
                }
            }
        }

        // 3303 = 128⁻¹ mod q
        for (var j = 0; j < N; j++)
        {
            f[j] = f[j] * 3303 % Q;
        }
    }

    /// <summary>
    /// h + MultiplyNTTs(f̂, ĝ) (FIPS 203 §4.3.1, algorithms 11 and 12): the products of the 128 polynomials of degree 1
    /// modulo X² - γ, added to <paramref name="accumulator"/>.
    /// </summary>
    private static void MultiplyNttsAndAdd(ReadOnlySpan<int> f, ReadOnlySpan<int> g, Span<int> accumulator)
    {
        var gammas = Gammas;
        for (var i = 0; i < N / 2; i++)
        {
            var a0 = f[2 * i];
            var a1 = f[(2 * i) + 1];
            var b0 = g[2 * i];
            var b1 = g[(2 * i) + 1];
            var c0 = ((a0 * b0) + (a1 * b1 % Q * gammas[i])) % Q;
            var c1 = ((a0 * b1) + (a1 * b0)) % Q;
            accumulator[2 * i] = (accumulator[2 * i] + c0) % Q;
            accumulator[(2 * i) + 1] = (accumulator[(2 * i) + 1] + c1) % Q;
        }
    }

    /// <summary>Compress_d(x) = ⌈(2^d / q) · x⌋ mod 2^d (FIPS 203 §4.2.1), for x in [0, q).</summary>
    private static int Compress(int d, int x)
    {
        // The division by a constant is compiled to a multiplication, which does not depend on the (secret) value
        return (((x << d) + (Q / 2)) / Q) & ((1 << d) - 1);
    }

    /// <summary>Decompress_d(y) = ⌈(q / 2^d) · y⌋ (FIPS 203 §4.2.1).</summary>
    private static int Decompress(int d, int y)
    {
        return ((y * Q) + (1 << (d - 1))) >> d;
    }

    /// <summary>ByteEncode_d(F) (FIPS 203 §4.2.1, algorithm 5): the d low bits of each coefficient, in little-endian bit order.</summary>
    private static void ByteEncode(int d, ReadOnlySpan<int> f, Span<byte> output)
    {
        Debug.Assert(output.Length == 32 * d);
        ulong buffer = 0;
        var bits = 0;
        var index = 0;
        for (var i = 0; i < N; i++)
        {
            buffer |= (ulong)(uint)f[i] << bits;
            bits += d;
            while (bits >= 8)
            {
                output[index++] = (byte)buffer;
                buffer >>= 8;
                bits -= 8;
            }
        }
    }

    /// <summary>ByteDecode_d(B) (FIPS 203 §4.2.1, algorithm 6): the coefficients are reduced modulo q when d is 12.</summary>
    private static void ByteDecode(int d, ReadOnlySpan<byte> input, Span<int> f)
    {
        Debug.Assert(input.Length == 32 * d);
        ulong buffer = 0;
        var bits = 0;
        var index = 0;
        var mask = (1UL << d) - 1;
        for (var i = 0; i < N; i++)
        {
            while (bits < d)
            {
                buffer |= (ulong)input[index++] << bits;
                bits += 8;
            }

            var value = (int)(buffer & mask);
            f[i] = d == 12 ? value % Q : value;
            buffer >>= d;
            bits -= d;
        }
    }

    private static int[] ComputeZetas(bool gammas)
    {
        var result = new int[128];
        for (var i = 0; i < 128; i++)
        {
            // BitRev7(i)
            var reversed = 0;
            for (var bit = 0; bit < 7; bit++)
            {
                reversed |= ((i >> bit) & 1) << (6 - bit);
            }

            var exponent = gammas ? (2 * reversed) + 1 : reversed;
            var value = 1;
            for (var e = 0; e < exponent; e++)
            {
                value = value * 17 % Q;
            }

            result[i] = value;
        }

        return result;
    }

    private static Parameters GetParameters(int parameterSet)
    {
        // FIPS 203 §8, table 2: (k, η₁, η₂, d_u, d_v)
        return parameterSet switch
        {
            512 => new Parameters(2, 3, 2, 10, 4),
            768 => new Parameters(3, 2, 2, 10, 4),
            1024 => new Parameters(4, 2, 2, 11, 5),
            _ => throw new ArgumentOutOfRangeException(nameof(parameterSet)),
        };
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Parameters(int K, int Eta1, int Eta2, int Du, int Dv)
    {
        public int EncapsulationKeyLength => (EncodedPolynomialLength * K) + 32;

        public int DecapsulationKeyLength => (2 * EncodedPolynomialLength * K) + 96;

        public int CiphertextLength => 32 * ((Du * K) + Dv);
    }
}
