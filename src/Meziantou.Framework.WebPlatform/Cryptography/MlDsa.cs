using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Meziantou.Framework.Hashing;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>
/// ML-DSA, the Module-Lattice-Based Digital Signature Algorithm (FIPS 204), with the parameter sets ML-DSA-44, ML-DSA-65
/// and ML-DSA-87: the pure variant (not HashML-DSA), with a context string. The base class library cannot be used: its
/// ML-DSA type is not supported on every platform (e.g. macOS).
/// </summary>
/// <remarks>
/// The coefficients of the polynomials are stored as integers in [0, q); the norms, the decompositions and the encodings
/// use the centered representatives. The private key of the Web Cryptography API is the 32-byte seed ξ: the signature
/// expands it with ML-DSA.KeyGen_internal.
/// </remarks>
internal static class MlDsa
{
    /// <summary>The length of the seed ξ of ML-DSA.KeyGen_internal (FIPS 204 §6.1).</summary>
    public const int SeedLength = 32;

    /// <summary>The maximum length of the context string (FIPS 204 §5.2).</summary>
    public const int MaxContextLength = 255;

    private const int N = 256;
    private const int Q = 8380417;
    private const int D = 13;
    private const int RandomnessLength = 32;

    // The rates of SHAKE128 and SHAKE256 (FIPS 202 §6.2): the output is read one block at a time
    private const int Shake128BlockSize = 168;
    private const int Shake256BlockSize = 136;

    // FIPS 204 §7.5: ζ^BitRev8(k) mod q (ζ = 1753)
    private static readonly int[] Zetas = ComputeZetas();

    /// <summary>Gets the length of the public key of a parameter set (FIPS 204 §4, table 2).</summary>
    public static int GetPublicKeyLength(int parameterSet) => GetParameters(parameterSet).PublicKeyLength;

    /// <summary>Gets the length of the expanded private key of a parameter set (FIPS 204 §4, table 2).</summary>
    public static int GetPrivateKeyLength(int parameterSet) => GetParameters(parameterSet).PrivateKeyLength;

    /// <summary>Gets the length of the signature of a parameter set (FIPS 204 §4, table 2).</summary>
    public static int GetSignatureLength(int parameterSet) => GetParameters(parameterSet).SignatureLength;

    /// <summary>ML-DSA.KeyGen_internal(ξ) (FIPS 204 §6.1, algorithm 6).</summary>
    /// <param name="parameterSet">44, 65 or 87.</param>
    public static (byte[] PublicKey, byte[] ExpandedPrivateKey) GenerateKeyFromSeed(int parameterSet, ReadOnlySpan<byte> seed)
    {
        var parameters = GetParameters(parameterSet);
        ValidateSeed(seed);

        var k = parameters.K;
        var l = parameters.L;
        var publicKey = new byte[parameters.PublicKeyLength];
        var privateKey = new byte[parameters.PrivateKeyLength];
        var buffer = ArrayPool<int>.Shared.Rent((l + k + k) * N);
        try
        {
            var s1 = buffer.AsSpan(0, l * N);
            var s2 = buffer.AsSpan(l * N, k * N);
            var t0 = buffer.AsSpan((l + k) * N, k * N);
            var privateKeySpan = privateKey.AsSpan();
            KeyGen(parameters, seed, privateKeySpan[..32], privateKeySpan.Slice(32, 32), privateKeySpan.Slice(64, 64), s1, s2, t0, publicKey);

            // skEncode (algorithm 24): ρ || K || tr || BitPack(s₁, η, η) || BitPack(s₂, η, η) || BitPack(t₀, 2^(d-1) - 1, 2^(d-1))
            var offset = 128;
            var etaLength = 32 * parameters.EtaBits;
            Span<int> packed = stackalloc int[N];
            for (var i = 0; i < l + k; i++)
            {
                var polynomial = i < l ? s1.Slice(i * N, N) : s2.Slice((i - l) * N, N);
                for (var n = 0; n < N; n++)
                {
                    packed[n] = parameters.Eta - Centered(polynomial[n]);
                }

                PackBits(packed, parameters.EtaBits, privateKeySpan.Slice(offset, etaLength));
                offset += etaLength;
            }

            for (var i = 0; i < k; i++)
            {
                var polynomial = t0.Slice(i * N, N);
                for (var n = 0; n < N; n++)
                {
                    packed[n] = (1 << (D - 1)) - Centered(polynomial[n]);
                }

                PackBits(packed, D, privateKeySpan.Slice(offset, 32 * D));
                offset += 32 * D;
            }

            Debug.Assert(offset == privateKey.Length);
            packed.Clear();
            return (publicKey, privateKey);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>
    /// ML-DSA.Sign(sk, M, ctx) (FIPS 204 §5.2, algorithm 2), the hedged variant with a random rnd, where sk is the private
    /// key expanded from the seed ξ.
    /// </summary>
    /// <param name="parameterSet">44, 65 or 87.</param>
    /// <param name="context">The context string, at most 255 bytes.</param>
    public static byte[] Sign(int parameterSet, ReadOnlySpan<byte> seed, ReadOnlySpan<byte> message, ReadOnlySpan<byte> context)
    {
        Span<byte> randomness = stackalloc byte[RandomnessLength];
        RandomNumberGenerator.Fill(randomness);
        var signature = Sign(parameterSet, seed, message, context, randomness);
        CryptographicOperations.ZeroMemory(randomness);
        return signature;
    }

    /// <summary>
    /// ML-DSA.Sign(sk, M, ctx) (FIPS 204 §5.2, algorithm 2) with an explicit rnd: 32 random bytes for the hedged variant, or
    /// 32 zero bytes for the deterministic variant.
    /// </summary>
    internal static byte[] Sign(int parameterSet, ReadOnlySpan<byte> seed, ReadOnlySpan<byte> message, ReadOnlySpan<byte> context, ReadOnlySpan<byte> randomness)
    {
        var parameters = GetParameters(parameterSet);
        ValidateSeed(seed);
        if (context.Length > MaxContextLength)
            throw new ArgumentException("The context must be at most 255 bytes long.", nameof(context));

        if (randomness.Length != RandomnessLength)
            throw new ArgumentException("The randomness must be 32 bytes long.", nameof(randomness));

        var k = parameters.K;
        var l = parameters.L;

        // A (k·ℓ), ŝ₁ (ℓ), ŝ₂ (k), t̂₀ (k), y (ℓ), ŷ (ℓ), w (k), z (ℓ), r (k), ct₀ (k), ĉ
        var polynomialCount = (k * l) + (4 * l) + (5 * k) + 1;
        var buffer = ArrayPool<int>.Shared.Rent(polynomialCount * N);
        Span<byte> secrets = stackalloc byte[32 + 32 + 64 + 64 + 64];
        var signature = new byte[parameters.SignatureLength];
        try
        {
            var span = buffer.AsSpan(0, polynomialCount * N);
            span.Clear();
            var matrix = TakePolynomials(ref span, k * l);
            var s1 = TakePolynomials(ref span, l);
            var s2 = TakePolynomials(ref span, k);
            var t0 = TakePolynomials(ref span, k);
            var y = TakePolynomials(ref span, l);
            var yHat = TakePolynomials(ref span, l);
            var w = TakePolynomials(ref span, k);
            var z = TakePolynomials(ref span, l);
            var r = TakePolynomials(ref span, k);
            var ct0 = TakePolynomials(ref span, k);
            var c = TakePolynomials(ref span, 1);

            var rho = secrets[..32];
            var key = secrets.Slice(32, 32);
            var tr = secrets.Slice(64, 64);
            var mu = secrets.Slice(128, 64);
            var rhoPrimePrime = secrets.Slice(192, 64);

            // The private key (ρ, K, tr, s₁, s₂, t₀) is expanded from the seed
            var publicKey = ArrayPool<byte>.Shared.Rent(parameters.PublicKeyLength);
            KeyGen(parameters, seed, rho, key, tr, s1, s2, t0, publicKey.AsSpan(0, parameters.PublicKeyLength));
            ArrayPool<byte>.Shared.Return(publicKey);

            // ML-DSA.Sign_internal(sk, M', rnd) (algorithm 7), with M' = IntegerToBytes(0, 1) || IntegerToBytes(|ctx|, 1) || ctx || M
            NttAll(s1);
            NttAll(s2);
            NttAll(t0);
            ExpandA(parameters, rho, matrix);

            // μ = H(BytesToBits(tr) || M', 64)
            var sponge = new Shake(256);
            sponge.AppendData(tr);
            sponge.AppendData(0);
            sponge.AppendData((byte)context.Length);
            sponge.AppendData(context);
            sponge.AppendData(message);
            sponge.Read(mu);

            // ρ'' = H(K || rnd || μ, 64)
            sponge = new Shake(256);
            sponge.AppendData(key);
            sponge.AppendData(randomness);
            sponge.AppendData(mu);
            sponge.Read(rhoPrimePrime);
            sponge.Reset();

            Span<byte> commitment = stackalloc byte[32 * 6 * 8];
            commitment = commitment[..(32 * parameters.W1Bits * k)];
            Span<byte> hints = stackalloc byte[8 * N];
            hints = hints[..(k * N)];
            Span<int> packed = stackalloc int[N];
            var cTilde = signature.AsSpan(0, parameters.CTildeLength);
            var gamma2 = parameters.Gamma2;

            for (var kappa = 0; ; kappa += l)
            {
                // y = ExpandMask(ρ'', κ), w = NTT⁻¹(Â ∘ NTT(y)), w₁ = HighBits(w)
                ExpandMask(parameters, rhoPrimePrime, kappa, y);
                y.CopyTo(yHat);
                NttAll(yHat);
                MultiplyMatrixVector(matrix, yHat, w, k, l);
                InverseNttAll(w);
                for (var i = 0; i < k; i++)
                {
                    var polynomial = w.Slice(i * N, N);
                    for (var n = 0; n < N; n++)
                    {
                        packed[n] = HighBits(polynomial[n], gamma2);
                    }

                    PackBits(packed, parameters.W1Bits, commitment.Slice(i * 32 * parameters.W1Bits, 32 * parameters.W1Bits));
                }

                // c̃ = H(μ || w1Encode(w₁), λ/4), c = SampleInBall(c̃)
                sponge = new Shake(256);
                sponge.AppendData(mu);
                sponge.AppendData(commitment);
                sponge.Read(cTilde);
                SampleInBall(parameters, cTilde, c);
                Ntt(c);

                // z = y + ⟨⟨cs₁⟩⟩; reject when ‖z‖∞ ≥ γ₁ - β
                MultiplyEach(c, s1, z);
                InverseNttAll(z);
                if (!AddAndCheckNorm(y, z, parameters.Gamma1 - parameters.Beta))
                    continue;

                // r = w - ⟨⟨cs₂⟩⟩, r₀ = LowBits(r); reject when ‖r₀‖∞ ≥ γ₂ - β
                MultiplyEach(c, s2, r);
                InverseNttAll(r);
                var valid = true;
                for (var i = 0; i < r.Length && valid; i++)
                {
                    var value = w[i] - r[i];
                    r[i] = value + (Q & (value >> 31));
                    valid = Math.Abs(LowBits(r[i], gamma2)) < gamma2 - parameters.Beta;
                }

                if (!valid)
                    continue;

                // ⟨⟨ct₀⟩⟩; reject when ‖ct₀‖∞ ≥ γ₂ or when the hint h = MakeHint(-ct₀, w - cs₂ + ct₀) has more than ω ones
                MultiplyEach(c, t0, ct0);
                InverseNttAll(ct0);
                var hintCount = 0;
                for (var i = 0; i < ct0.Length && valid; i++)
                {
                    valid = Math.Abs(Centered(ct0[i])) < gamma2;
                    var sum = (r[i] + ct0[i]) % Q;
                    var hint = HighBits(sum, gamma2) != HighBits(r[i], gamma2);
                    hints[i] = hint ? (byte)1 : (byte)0;
                    hintCount += hints[i];
                }

                if (!valid || hintCount > parameters.Omega)
                    continue;

                // σ = sigEncode(c̃, z mod± q, h) (algorithm 26)
                EncodeSignature(parameters, z, hints, signature);
                packed.Clear();
                return signature;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secrets);
            ArrayPool<int>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>
    /// ML-DSA.Verify(pk, M, σ, ctx) (FIPS 204 §5.3, algorithm 3). A malformed public key, signature or context is not an
    /// error: the signature is not valid.
    /// </summary>
    /// <param name="parameterSet">44, 65 or 87.</param>
    public static bool Verify(int parameterSet, ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> context)
    {
        var parameters = GetParameters(parameterSet);
        if (publicKey.Length != parameters.PublicKeyLength || signature.Length != parameters.SignatureLength || context.Length > MaxContextLength)
            return false;

        var k = parameters.K;
        var l = parameters.L;

        // z (ℓ), t₁ (k), w (k), ĉ, a matrix entry
        var polynomialCount = l + k + k + 2;
        var buffer = ArrayPool<int>.Shared.Rent(polynomialCount * N);
        try
        {
            var span = buffer.AsSpan(0, polynomialCount * N);
            span.Clear();
            var z = TakePolynomials(ref span, l);
            var t1 = TakePolynomials(ref span, k);
            var w = TakePolynomials(ref span, k);
            var c = TakePolynomials(ref span, 1);
            var entry = TakePolynomials(ref span, 1);

            // sigDecode (algorithm 27): c̃, z = BitUnpack(z, γ₁ - 1, γ₁), h = HintBitUnpack(h)
            var cTilde = signature[..parameters.CTildeLength];
            var zLength = 32 * parameters.Gamma1Bits;
            for (var i = 0; i < l; i++)
            {
                var polynomial = z.Slice(i * N, N);
                UnpackBits(signature.Slice(parameters.CTildeLength + (i * zLength), zLength), parameters.Gamma1Bits, polynomial);
                for (var n = 0; n < N; n++)
                {
                    var value = parameters.Gamma1 - polynomial[n];

                    // ‖z‖∞ ≥ γ₁ - β
                    if (Math.Abs(value) >= parameters.Gamma1 - parameters.Beta)
                        return false;

                    polynomial[n] = value + (Q & (value >> 31));
                }
            }

            Span<byte> hints = stackalloc byte[8 * N];
            hints = hints[..(k * N)];
            if (!DecodeHints(parameters, signature[(parameters.CTildeLength + (l * zLength))..], hints))
                return false;

            // pkDecode (algorithm 23): ρ, t₁ = SimpleBitUnpack(t₁, 2^(bitlen(q-1) - d) - 1)
            var rho = publicKey[..32];
            for (var i = 0; i < k; i++)
            {
                UnpackBits(publicKey.Slice(32 + (i * 320), 320), 10, t1.Slice(i * N, N));
            }

            // tr = H(pk, 64), μ = H(BytesToBits(tr) || M', 64)
            Span<byte> tr = stackalloc byte[64];
            var sponge = new Shake(256);
            sponge.AppendData(publicKey);
            sponge.Read(tr);
            Span<byte> mu = stackalloc byte[64];
            sponge = new Shake(256);
            sponge.AppendData(tr);
            sponge.AppendData(0);
            sponge.AppendData((byte)context.Length);
            sponge.AppendData(context);
            sponge.AppendData(message);
            sponge.Read(mu);

            // w'_Approx = NTT⁻¹(Â ∘ NTT(z) - NTT(c) ∘ NTT(t₁ · 2^d)), w'₁ = UseHint(h, w'_Approx)
            SampleInBall(parameters, cTilde, c);
            Ntt(c);
            NttAll(z);
            Span<byte> seed = stackalloc byte[34];
            rho.CopyTo(seed);
            Span<byte> commitment = stackalloc byte[32 * 6 * 8];
            commitment = commitment[..(32 * parameters.W1Bits * k)];
            Span<int> packed = stackalloc int[N];
            for (var i = 0; i < k; i++)
            {
                var accumulator = w.Slice(i * N, N);
                for (var j = 0; j < l; j++)
                {
                    seed[32] = (byte)j;
                    seed[33] = (byte)i;
                    RejNttPoly(seed, entry);
                    MultiplyAdd(entry, z.Slice(j * N, N), accumulator);
                }

                var t1Polynomial = t1.Slice(i * N, N);
                for (var n = 0; n < N; n++)
                {
                    t1Polynomial[n] <<= D;
                }

                Ntt(t1Polynomial);
                for (var n = 0; n < N; n++)
                {
                    var value = accumulator[n] - (int)((long)c[n] * t1Polynomial[n] % Q);
                    accumulator[n] = value + (Q & (value >> 31));
                }

                InverseNtt(accumulator);
                for (var n = 0; n < N; n++)
                {
                    packed[n] = UseHint(hints[(i * N) + n] != 0, accumulator[n], parameters.Gamma2);
                }

                PackBits(packed, parameters.W1Bits, commitment.Slice(i * 32 * parameters.W1Bits, 32 * parameters.W1Bits));
            }

            // c̃' = H(μ || w1Encode(w'₁), λ/4)
            Span<byte> expected = stackalloc byte[64];
            expected = expected[..parameters.CTildeLength];
            sponge = new Shake(256);
            sponge.AppendData(mu);
            sponge.AppendData(commitment);
            sponge.Read(expected);
            return CryptographicOperations.FixedTimeEquals(expected, cTilde);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>
    /// ML-DSA.KeyGen_internal(ξ) (FIPS 204 §6.1, algorithm 6): computes ρ, K, tr, s₁, s₂ and t₀ (the coefficients in
    /// [0, q)), and the encoded public key.
    /// </summary>
    private static void KeyGen(Parameters parameters, ReadOnlySpan<byte> seed, Span<byte> rho, Span<byte> key, Span<byte> tr, Span<int> s1, Span<int> s2, Span<int> t0, Span<byte> publicKey)
    {
        var k = parameters.K;
        var l = parameters.L;

        // (ρ, ρ', K) = H(ξ || IntegerToBytes(k, 1) || IntegerToBytes(ℓ, 1), 128)
        Span<byte> expanded = stackalloc byte[128];
        var sponge = new Shake(256);
        sponge.AppendData(seed);
        sponge.AppendData((byte)k);
        sponge.AppendData((byte)l);
        sponge.Read(expanded);
        sponge.Reset();
        expanded[..32].CopyTo(rho);
        expanded[96..].CopyTo(key);

        // (s₁, s₂) = ExpandS(ρ') (algorithm 33)
        Span<byte> rhoPrime = stackalloc byte[66];
        expanded.Slice(32, 64).CopyTo(rhoPrime);
        for (var r = 0; r < l + k; r++)
        {
            rhoPrime[64] = (byte)r;
            rhoPrime[65] = (byte)(r >> 8);
            RejBoundedPoly(parameters, rhoPrime, r < l ? s1.Slice(r * N, N) : s2.Slice((r - l) * N, N));
        }

        CryptographicOperations.ZeroMemory(expanded);
        CryptographicOperations.ZeroMemory(rhoPrime);

        // t = NTT⁻¹(Â ∘ NTT(s₁)) + s₂, (t₁, t₀) = Power2Round(t), pk = pkEncode(ρ, t₁)
        var buffer = ArrayPool<int>.Shared.Rent((l + 2) * N);
        try
        {
            var s1Hat = buffer.AsSpan(0, l * N);
            var entry = buffer.AsSpan(l * N, N);
            var packed = buffer.AsSpan((l + 1) * N, N);
            s1.CopyTo(s1Hat);
            NttAll(s1Hat);

            Span<byte> matrixSeed = stackalloc byte[34];
            rho.CopyTo(matrixSeed);
            rho.CopyTo(publicKey);
            for (var i = 0; i < k; i++)
            {
                var t = t0.Slice(i * N, N);
                t.Clear();
                for (var j = 0; j < l; j++)
                {
                    matrixSeed[32] = (byte)j;
                    matrixSeed[33] = (byte)i;
                    RejNttPoly(matrixSeed, entry);
                    MultiplyAdd(entry, s1Hat.Slice(j * N, N), t);
                }

                InverseNtt(t);
                var s2Polynomial = s2.Slice(i * N, N);
                for (var n = 0; n < N; n++)
                {
                    // Power2Round (algorithm 35): r₀ = r⁺ mod± 2^d, r₁ = (r⁺ - r₀) / 2^d
                    var value = (t[n] + s2Polynomial[n]) % Q;
                    var low = value & ((1 << D) - 1);
                    low -= (1 << D) & (((1 << (D - 1)) - low) >> 31);
                    packed[n] = (value - low) >> D;
                    t[n] = low + (Q & (low >> 31));
                }

                PackBits(packed, 10, publicKey.Slice(32 + (i * 320), 320));
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(buffer, clearArray: true);
        }

        // tr = H(pk, 64)
        sponge = new Shake(256);
        sponge.AppendData(publicKey);
        sponge.Read(tr);
    }

    /// <summary>ExpandA(ρ) (FIPS 204 §7.3, algorithm 32): Â[r, s] = RejNTTPoly(ρ || IntegerToBytes(s, 1) || IntegerToBytes(r, 1)).</summary>
    private static void ExpandA(Parameters parameters, ReadOnlySpan<byte> rho, Span<int> matrix)
    {
        Span<byte> seed = stackalloc byte[34];
        rho.CopyTo(seed);
        for (var r = 0; r < parameters.K; r++)
        {
            for (var s = 0; s < parameters.L; s++)
            {
                seed[32] = (byte)s;
                seed[33] = (byte)r;
                RejNttPoly(seed, matrix.Slice(((r * parameters.L) + s) * N, N));
            }
        }
    }

    /// <summary>
    /// RejNTTPoly(ρ) (FIPS 204 §7.3, algorithm 30): rejection sampling from SHAKE128 of the coefficients
    /// CoeffFromThreeBytes (algorithm 14) lower than q.
    /// </summary>
    private static void RejNttPoly(ReadOnlySpan<byte> seed, Span<int> output)
    {
        var sponge = new Shake(128);
        sponge.AppendData(seed);
        Span<byte> block = stackalloc byte[Shake128BlockSize];
        var j = 0;
        while (true)
        {
            sponge.Read(block);
            for (var i = 0; i < block.Length; i += 3)
            {
                var value = block[i] | (block[i + 1] << 8) | ((block[i + 2] & 0x7F) << 16);
                if (value < Q)
                {
                    output[j++] = value;
                    if (j == N)
                        return;
                }
            }
        }
    }

    /// <summary>
    /// RejBoundedPoly(ρ) (FIPS 204 §7.3, algorithm 31): rejection sampling from SHAKE256 of the coefficients
    /// CoeffFromHalfByte (algorithm 15) in [-η, η], stored modulo q.
    /// </summary>
    private static void RejBoundedPoly(Parameters parameters, ReadOnlySpan<byte> seed, Span<int> output)
    {
        var sponge = new Shake(256);
        sponge.AppendData(seed);
        Span<byte> block = stackalloc byte[Shake256BlockSize];
        var eta = parameters.Eta;
        var j = 0;
        while (j < N)
        {
            sponge.Read(block);
            for (var i = 0; i < block.Length && j < N; i++)
            {
                var z0 = CoeffFromHalfByte(eta, block[i] & 0x0F);
                if (z0 != int.MinValue)
                {
                    output[j++] = z0 + (Q & (z0 >> 31));
                }

                var z1 = CoeffFromHalfByte(eta, block[i] >> 4);
                if (z1 != int.MinValue && j < N)
                {
                    output[j++] = z1 + (Q & (z1 >> 31));
                }
            }
        }

        sponge.Reset();
        CryptographicOperations.ZeroMemory(block);
    }

    /// <summary>CoeffFromHalfByte(b) (FIPS 204 §7.1, algorithm 15), or <see cref="int.MinValue"/> for ⊥.</summary>
    private static int CoeffFromHalfByte(int eta, int value)
    {
        if (eta == 2)
            return value < 15 ? 2 - (value % 5) : int.MinValue;

        return value < 9 ? 4 - value : int.MinValue;
    }

    /// <summary>
    /// ExpandMask(ρ'', κ) (FIPS 204 §7.3, algorithm 34): y[r] = BitUnpack(H(ρ'' || IntegerToBytes(κ + r, 2), 32c), γ₁ - 1, γ₁),
    /// stored modulo q.
    /// </summary>
    private static void ExpandMask(Parameters parameters, ReadOnlySpan<byte> rhoPrimePrime, int kappa, Span<int> y)
    {
        var bits = parameters.Gamma1Bits;
        Span<byte> bytes = stackalloc byte[32 * 20];
        bytes = bytes[..(32 * bits)];
        for (var r = 0; r < parameters.L; r++)
        {
            var sponge = new Shake(256);
            sponge.AppendData(rhoPrimePrime);
            sponge.AppendData((byte)(kappa + r));
            sponge.AppendData((byte)((kappa + r) >> 8));
            sponge.Read(bytes);
            sponge.Reset();

            var polynomial = y.Slice(r * N, N);
            UnpackBits(bytes, bits, polynomial);
            for (var n = 0; n < N; n++)
            {
                var value = parameters.Gamma1 - polynomial[n];
                polynomial[n] = value + (Q & (value >> 31));
            }
        }

        CryptographicOperations.ZeroMemory(bytes);
    }

    /// <summary>
    /// SampleInBall(ρ) (FIPS 204 §7.3, algorithm 29): a polynomial with τ coefficients in {-1, 1} and the other ones 0,
    /// stored modulo q.
    /// </summary>
    private static void SampleInBall(Parameters parameters, ReadOnlySpan<byte> seed, Span<int> c)
    {
        c.Clear();
        var sponge = new Shake(256);
        sponge.AppendData(seed);
        Span<byte> signBytes = stackalloc byte[8];
        sponge.Read(signBytes);
        var signs = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(signBytes);
        for (var i = N - parameters.Tau; i < N; i++)
        {
            int j;
            do
            {
                j = sponge.ReadByte();
            }
            while (j > i);

            c[i] = c[j];
            c[j] = (signs & 1) != 0 ? Q - 1 : 1;
            signs >>= 1;
        }
    }

    /// <summary>sigEncode(c̃, z, h) (FIPS 204 §7.2, algorithm 26); c̃ is already at the start of the signature.</summary>
    private static void EncodeSignature(Parameters parameters, ReadOnlySpan<int> z, ReadOnlySpan<byte> hints, Span<byte> signature)
    {
        var offset = parameters.CTildeLength;
        var zLength = 32 * parameters.Gamma1Bits;
        Span<int> packed = stackalloc int[N];
        for (var i = 0; i < parameters.L; i++)
        {
            var polynomial = z.Slice(i * N, N);
            for (var n = 0; n < N; n++)
            {
                packed[n] = parameters.Gamma1 - Centered(polynomial[n]);
            }

            PackBits(packed, parameters.Gamma1Bits, signature.Slice(offset, zLength));
            offset += zLength;
        }

        // HintBitPack (algorithm 20): the indices of the ones of each polynomial, then the number of indices after each
        // polynomial
        var output = signature[offset..];
        output.Clear();
        var index = 0;
        for (var i = 0; i < parameters.K; i++)
        {
            for (var n = 0; n < N; n++)
            {
                if (hints[(i * N) + n] != 0)
                {
                    output[index++] = (byte)n;
                }
            }

            output[parameters.Omega + i] = (byte)index;
        }
    }

    /// <summary>HintBitUnpack(y) (FIPS 204 §7.1, algorithm 21); returns <see langword="false"/> for ⊥ (a malformed hint).</summary>
    private static bool DecodeHints(Parameters parameters, ReadOnlySpan<byte> input, Span<byte> hints)
    {
        Debug.Assert(input.Length == parameters.Omega + parameters.K);
        hints.Clear();
        var omega = parameters.Omega;
        var index = 0;
        for (var i = 0; i < parameters.K; i++)
        {
            var end = (int)input[omega + i];
            if (end < index || end > omega)
                return false;

            var first = index;
            while (index < end)
            {
                // The indices are strictly increasing
                if (index > first && input[index - 1] >= input[index])
                    return false;

                hints[(i * N) + input[index]] = 1;
                index++;
            }
        }

        for (var i = index; i < omega; i++)
        {
            if (input[i] != 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Decompose(r) (FIPS 204 §7.4, algorithm 36), for r in [0, q): r₀ = r mod± 2γ₂, r₁ = (r - r₀) / 2γ₂, with the special
    /// case r - r₀ = q - 1. Returns r₁ and writes r₀.
    /// </summary>
    private static int Decompose(int value, int gamma2, out int low)
    {
        low = value % (2 * gamma2);
        if (low > gamma2)
        {
            low -= 2 * gamma2;
        }

        if (value - low == Q - 1)
        {
            low--;
            return 0;
        }

        return (value - low) / (2 * gamma2);
    }

    /// <summary>HighBits(r) (FIPS 204 §7.4, algorithm 37).</summary>
    private static int HighBits(int value, int gamma2) => Decompose(value, gamma2, out _);

    /// <summary>LowBits(r) (FIPS 204 §7.4, algorithm 38).</summary>
    private static int LowBits(int value, int gamma2)
    {
        Decompose(value, gamma2, out var low);
        return low;
    }

    /// <summary>UseHint(h, r) (FIPS 204 §7.4, algorithm 40).</summary>
    private static int UseHint(bool hint, int value, int gamma2)
    {
        var m = (Q - 1) / (2 * gamma2);
        var high = Decompose(value, gamma2, out var low);
        if (!hint)
            return high;

        return low > 0 ? (high + 1) % m : (high - 1 + m) % m;
    }

    /// <summary>NTT(w) (FIPS 204 §7.5, algorithm 41), in place.</summary>
    private static void Ntt(Span<int> w)
    {
        Debug.Assert(w.Length == N);
        var zetas = Zetas;
        var m = 0;
        for (var length = 128; length >= 1; length /= 2)
        {
            for (var start = 0; start < N; start += 2 * length)
            {
                m++;
                long zeta = zetas[m];
                for (var j = start; j < start + length; j++)
                {
                    var t = (int)(zeta * w[j + length] % Q);
                    var difference = w[j] - t;
                    w[j + length] = difference + (Q & (difference >> 31));
                    var sum = w[j] + t;
                    w[j] = sum - Q + (Q & ((sum - Q) >> 31));
                }
            }
        }
    }

    /// <summary>NTT⁻¹(ŵ) (FIPS 204 §7.5, algorithm 42), in place.</summary>
    private static void InverseNtt(Span<int> w)
    {
        Debug.Assert(w.Length == N);
        var zetas = Zetas;
        var m = N;
        for (var length = 1; length < N; length *= 2)
        {
            for (var start = 0; start < N; start += 2 * length)
            {
                m--;
                long zeta = Q - zetas[m];
                for (var j = start; j < start + length; j++)
                {
                    var t = w[j];
                    var sum = t + w[j + length];
                    w[j] = sum - Q + (Q & ((sum - Q) >> 31));
                    var difference = t - w[j + length];
                    difference += Q & (difference >> 31);
                    w[j + length] = (int)(zeta * difference % Q);
                }
            }
        }

        // 8347681 = 256⁻¹ mod q
        for (var j = 0; j < N; j++)
        {
            w[j] = (int)(8347681L * w[j] % Q);
        }
    }

    private static void NttAll(Span<int> polynomials)
    {
        for (var i = 0; i < polynomials.Length; i += N)
        {
            Ntt(polynomials.Slice(i, N));
        }
    }

    private static void InverseNttAll(Span<int> polynomials)
    {
        for (var i = 0; i < polynomials.Length; i += N)
        {
            InverseNtt(polynomials.Slice(i, N));
        }
    }

    /// <summary>accumulator += a ∘ b (the product in the NTT domain is coefficient-wise, FIPS 204 §7.6).</summary>
    private static void MultiplyAdd(ReadOnlySpan<int> a, ReadOnlySpan<int> b, Span<int> accumulator)
    {
        for (var n = 0; n < N; n++)
        {
            var sum = accumulator[n] + (int)((long)a[n] * b[n] % Q);
            accumulator[n] = sum - Q + (Q & ((sum - Q) >> 31));
        }
    }

    /// <summary>output = Â ∘ v (FIPS 204 §7.6, algorithm 48).</summary>
    private static void MultiplyMatrixVector(ReadOnlySpan<int> matrix, ReadOnlySpan<int> vector, Span<int> output, int k, int l)
    {
        output.Clear();
        for (var i = 0; i < k; i++)
        {
            var accumulator = output.Slice(i * N, N);
            for (var j = 0; j < l; j++)
            {
                MultiplyAdd(matrix.Slice(((i * l) + j) * N, N), vector.Slice(j * N, N), accumulator);
            }
        }
    }

    /// <summary>output[i] = ĉ ∘ v[i] for each polynomial of the vector.</summary>
    private static void MultiplyEach(ReadOnlySpan<int> c, ReadOnlySpan<int> vector, Span<int> output)
    {
        for (var i = 0; i < vector.Length; i++)
        {
            output[i] = (int)((long)c[i % N] * vector[i] % Q);
        }
    }

    /// <summary>z = y + z, and checks ‖z‖∞ &lt; <paramref name="bound"/>.</summary>
    private static bool AddAndCheckNorm(ReadOnlySpan<int> y, Span<int> z, int bound)
    {
        var valid = true;
        for (var i = 0; i < z.Length; i++)
        {
            var sum = y[i] + z[i];
            sum -= Q & ((Q - 1 - sum) >> 31);
            z[i] = sum;
            valid &= Math.Abs(Centered(sum)) < bound;
        }

        return valid;
    }

    /// <summary>The representative of x in (-q/2, q/2] (r mod± q, FIPS 204 §2.3), for x in [0, q).</summary>
    private static int Centered(int value)
    {
        return value - (Q & (((Q - 1) / 2 - value) >> 31));
    }

    /// <summary>Packs coefficients in [0, 2^bits) in little-endian bit order (SimpleBitPack and BitPack, FIPS 204 §7.1).</summary>
    private static void PackBits(ReadOnlySpan<int> values, int bits, Span<byte> output)
    {
        Debug.Assert(output.Length == 32 * bits);
        ulong buffer = 0;
        var count = 0;
        var index = 0;
        for (var i = 0; i < N; i++)
        {
            buffer |= (ulong)(uint)values[i] << count;
            count += bits;
            while (count >= 8)
            {
                output[index++] = (byte)buffer;
                buffer >>= 8;
                count -= 8;
            }
        }
    }

    /// <summary>Unpacks coefficients of <paramref name="bits"/> bits in little-endian bit order (FIPS 204 §7.1).</summary>
    private static void UnpackBits(ReadOnlySpan<byte> input, int bits, Span<int> values)
    {
        Debug.Assert(input.Length == 32 * bits);
        ulong buffer = 0;
        var count = 0;
        var index = 0;
        var mask = (1UL << bits) - 1;
        for (var i = 0; i < N; i++)
        {
            while (count < bits)
            {
                buffer |= (ulong)input[index++] << count;
                count += 8;
            }

            values[i] = (int)(buffer & mask);
            buffer >>= bits;
            count -= bits;
        }
    }

    private static Span<int> TakePolynomials(ref Span<int> span, int count)
    {
        var result = span[..(count * N)];
        span = span[(count * N)..];
        return result;
    }

    private static void ValidateSeed(ReadOnlySpan<byte> seed)
    {
        if (seed.Length != SeedLength)
            throw new ArgumentException("The seed must be 32 bytes long.", nameof(seed));
    }

    private static int[] ComputeZetas()
    {
        var result = new int[N];
        for (var i = 0; i < N; i++)
        {
            // BitRev8(i)
            var reversed = 0;
            for (var bit = 0; bit < 8; bit++)
            {
                reversed |= ((i >> bit) & 1) << (7 - bit);
            }

            long value = 1;
            for (var e = 0; e < reversed; e++)
            {
                value = value * 1753 % Q;
            }

            result[i] = (int)value;
        }

        return result;
    }

    private static Parameters GetParameters(int parameterSet)
    {
        // FIPS 204 §4, table 1: (τ, λ, γ₁, γ₂, k, ℓ, η, ω)
        return parameterSet switch
        {
            44 => new Parameters(Tau: 39, Lambda: 128, Gamma1: 1 << 17, Gamma2: (Q - 1) / 88, K: 4, L: 4, Eta: 2, Omega: 80),
            65 => new Parameters(Tau: 49, Lambda: 192, Gamma1: 1 << 19, Gamma2: (Q - 1) / 32, K: 6, L: 5, Eta: 4, Omega: 55),
            87 => new Parameters(Tau: 60, Lambda: 256, Gamma1: 1 << 19, Gamma2: (Q - 1) / 32, K: 8, L: 7, Eta: 2, Omega: 75),
            _ => throw new ArgumentOutOfRangeException(nameof(parameterSet)),
        };
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Parameters(int Tau, int Lambda, int Gamma1, int Gamma2, int K, int L, int Eta, int Omega)
    {
        /// <summary>β = τ · η.</summary>
        public int Beta => Tau * Eta;

        /// <summary>The length of the commitment hash c̃: λ/4 bytes.</summary>
        public int CTildeLength => Lambda / 4;

        /// <summary>1 + bitlen(γ₁ - 1): the number of bits of the coefficients of y and z.</summary>
        public int Gamma1Bits => Gamma1 == 1 << 17 ? 18 : 20;

        /// <summary>bitlen((q - 1) / (2γ₂) - 1): the number of bits of the coefficients of w₁.</summary>
        public int W1Bits => Gamma2 == (Q - 1) / 88 ? 6 : 4;

        /// <summary>bitlen(2η): the number of bits of the coefficients of s₁ and s₂.</summary>
        public int EtaBits => Eta == 2 ? 3 : 4;

        public int PublicKeyLength => 32 + (320 * K);

        public int PrivateKeyLength => 128 + (32 * EtaBits * (K + L)) + (32 * D * K);

        public int SignatureLength => CTildeLength + (32 * Gamma1Bits * L) + Omega + K;
    }
}
